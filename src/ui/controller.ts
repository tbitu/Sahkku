/**
 * The interactive match controller: the bridge between clicks and the pure `RulesEngine`.
 *
 * The controller owns the match loop — roll, offer the optional re-roll, play out the moves, notice
 * the end of the game — and exposes the result as a plain `MatchViewModel` snapshot. It contains no
 * rendering code and touches no DOM type, so the whole interaction model can be exercised headlessly
 * by the test suite while the browser layer (`board.ts`, `dice.ts`, `main.ts`) only has to paint the
 * snapshot it is handed.
 *
 * Two invariants shape every method here:
 *
 * 1. **The engine stays the sole authority.** Legal actions are read from
 *    `RulesEngine.evaluateAllowedPlaces` / `legalMoves`, and a move is committed only after
 *    `RulesEngine.isLegalMove` confirmed it. A click on an illegal cell is dropped silently — the UI
 *    must never be able to corrupt the board.
 * 2. **The controller never advances the turn by itself.** Handing over is a rules decision, so it
 *    only ever happens inside `RulesEngine.applyMove`, `nextPlayerTurn` or
 *    `applyRerollDecision`. The controller mirrors whatever the engine decided.
 */

import {
  DieFace,
  Move,
  PieceOwner,
  PieceType,
  RerollDecision,
  RuleEvent,
  RuleEventKind,
  TurnPhase,
  WinReason,
  type EngineOptions,
  type GameState,
  type IRandomSource,
} from "../rules/domain";
import type { RulesEngine } from "../rules/engine";
import { ownerName, pieceName } from "./pieces";

// ---------------------------------------------------------------------------------- view model

/** One piece as the board should draw it. */
export interface PieceTokenView {
  id: number;
  type: PieceType;
  owner: PieceOwner;

  /** Activated pieces are drawn raised and bright. */
  isActive: boolean;

  /** Seated in the home row but unlocked; the next click may activate it. */
  canBeActivated: boolean;

  /** Belongs to the player to move and has at least one legal destination. */
  selectable: boolean;

  selected: boolean;
}

/** One board cell with the pieces standing on it. */
export interface CellView {
  placeIndex: number;
  x: number;
  y: number;

  /** A legal destination of the currently selected piece. */
  highlight: boolean;

  /** Bottom to top; `pieces[0]` is the piece the engine treats as the top of a stack. */
  pieces: PieceTokenView[];
}

export interface DieView {
  index: number;
  face: DieFace;

  /** Already spent this throw. */
  spent: boolean;

  /** The die up for spending right now. */
  active: boolean;
}

export interface DiceViewState {
  /** False while the dice are still to be thrown this turn. */
  rolled: boolean;

  currentActiveDie: number;
  dice: DieView[];

  /** The Roll Dice button is live. */
  canRoll: boolean;

  /** The Reroll Die / Keep & Move decision is on offer. */
  canReroll: boolean;

  /** Human-readable spending-order progress. */
  orderText: string;
}

export interface GameOverView {
  winner: PieceOwner;
  winnerLabel: string;
  reason: WinReason;
  reasonLabel: string;
}

/** Everything the presentation layer needs to draw one frame. */
export interface MatchViewModel {
  boardWidth: number;
  boardHeight: number;
  cells: CellView[];

  turnPhase: TurnPhase;
  currentPlayer: PieceOwner;
  currentPlayerLabel: string;
  phase: "roll" | "move" | "gameover";
  statusText: string;

  dice: DiceViewState;
  captures: { p1: number; p2: number };

  selectedPieceId: number | null;
  selectablePieceIds: number[];
  highlightedPlaces: number[];

  /** The events of the last committed action, for the banner. */
  events: RuleEvent[];
  eventMessages: string[];

  gameOver: GameOverView | null;
}

export type MatchListener = (view: MatchViewModel) => void;

// ---------------------------------------------------------------------------------- controller

export class WebMatchController {
  private readonly engine: RulesEngine;
  private readonly random: IRandomSource;
  private readonly options: EngineOptions;

  private game: GameState;
  private selected: number | null = null;
  private lastEvents: RuleEvent[] = [];
  private readonly listeners = new Set<MatchListener>();

  /**
   * @param state An already-built state to sit on. Omitted by the browser client, which lets the
   *   engine deal a fresh game; the tests pass a hand-built position.
   */
  constructor(
    engine: RulesEngine,
    random: IRandomSource,
    options: EngineOptions,
    state?: GameState,
  ) {
    this.engine = engine;
    this.random = random;
    this.options = options;
    this.game = state ?? engine.initGame(options, random);
    this.syncDerived();
  }

  // ------------------------------------------------------------------ read-only surface

  /** The live state. Read it; never mutate it — every change goes through the engine. */
  get state(): GameState {
    return this.game;
  }

  /** The Roll Dice button is live. */
  get canRoll(): boolean {
    return !this.game.gameOver && this.game.isRollPhase;
  }

  get selectedPieceId(): number | null {
    return this.selected;
  }

  /** Legal destinations of the selected piece, as board cell indices. */
  get highlightedPlaces(): number[] {
    const piece = this.selected == null ? null : this.engine.findPiece(this.game, this.selected);
    return piece == null ? [] : [...piece.allowedPlaces];
  }

  /** Every legal action for the player to move, computed from scratch by the engine. */
  get legalMoves(): Move[] {
    return this.engine.legalMoves(this.game);
  }

  /**
   * A fresh snapshot for the presentation layer. Reading it re-derives the board's legal-move cache
   * (`Piece.allowedPlaces`) and drops a selection that the engine no longer allows; it never changes
   * the turn or the board itself.
   */
  get viewModel(): MatchViewModel {
    this.syncDerived();
    return this.buildViewModel();
  }

  /** Registers a listener for state changes; returns the unsubscribe function. */
  subscribe(listener: MatchListener): () => void {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  }

  // ------------------------------------------------------------------ match loop

  /** Throws the three dice. Returns false when it is not the active player's roll phase. */
  roll(): boolean {
    if (this.game.gameOver || !this.game.isRollPhase) return false;

    this.engine.rollDice(this.game, this.random);
    this.selected = null;
    this.lastEvents = [];

    // A sáhkku on the table opens the optional re-throw; anything else goes straight to the moves.
    if (!this.engine.canReroll(this.game)) this.beginMovePhase();

    this.publish();
    return true;
  }

  /** Throws the die that is up for spending again. Only legal while `canReroll`. */
  rerollActiveDie(): boolean {
    return this.decideReroll(RerollDecision.RerollActiveDie);
  }

  /** Keeps the throw as it fell and continues to the moves. Always legal in a move phase. */
  keepDice(): boolean {
    return this.decideReroll(RerollDecision.KeepDiceAndProceed);
  }

  /**
   * Applies the player's answer to the re-roll that is on offer. Re-rolling is refused unless the
   * ruleset allows it, so a stray click can never reopen a closed decision.
   */
  decideReroll(decision: RerollDecision): boolean {
    if (this.game.gameOver || this.game.isRollPhase) return false;
    if (decision === RerollDecision.RerollActiveDie && !this.engine.canReroll(this.game)) {
      return false;
    }

    this.engine.applyRerollDecision(
      this.game,
      decision,
      decision === RerollDecision.RerollActiveDie ? this.random : null,
    );
    this.selected = null;

    // A re-throw can produce another sáhkku, which reopens the decision; keeping the dice does not.
    if (!this.engine.canReroll(this.game)) this.beginMovePhase();

    this.publish();
    return true;
  }

  // ------------------------------------------------------------------ piece selection

  /** Selects a selectable piece of the player to move. Returns false for anything else. */
  selectPiece(pieceId: number): boolean {
    if (this.game.gameOver || this.game.isRollPhase) return false;

    const piece = this.engine.findPiece(this.game, pieceId);
    if (piece == null) return false;
    if (piece.owner !== this.game.currentPlayer) return false;
    if (!piece.isSelectable()) return false;

    this.selected = pieceId;
    this.publish();
    return true;
  }

  /** Drops the current selection, if any. */
  clearSelection(): void {
    if (this.selected == null) return;
    this.selected = null;
    this.publish();
  }

  /**
   * Handles a click on a board cell: it commits the selected piece's move when the cell is one of its
   * legal destinations, shifts the selection when the cell holds another selectable piece, and
   * otherwise cancels the selection. Clicks that mean nothing are ignored without touching the state.
   */
  handlePlaceClick(placeIndex: number): boolean {
    if (this.game.gameOver || this.game.isRollPhase) return false;
    if (placeIndex < 0 || placeIndex >= this.game.places.length) return false;

    const selected = this.selected == null ? null : this.engine.findPiece(this.game, this.selected);
    if (selected != null && selected.allowedPlaces.includes(placeIndex)) {
      return this.commitMove(new Move(selected.id, placeIndex));
    }

    const cell = this.game.places[placeIndex];
    const mine = cell.pieces.find(
      (piece) => piece.owner === this.game.currentPlayer && piece.isSelectable(),
    );
    if (mine != null) return this.selectPiece(mine.id);

    if (this.selected != null) {
      this.selected = null;
      this.publish();
      return true;
    }
    return false;
  }

  /** Commits a move the engine confirmed to be legal; an illegal proposal is dropped. */
  commitMove(move: Move): boolean {
    if (this.game.gameOver || this.game.isRollPhase) return false;
    if (!this.engine.isLegalMove(this.game, move)) return false;

    this.lastEvents = this.engine.applyMove(this.game, move);
    this.selected = null;
    this.publish();
    return true;
  }

  /** Deals a fresh game with the same options and clears every decision. */
  resetMatch(): void {
    this.game = this.engine.initGame(this.options, this.random);
    this.selected = null;
    this.lastEvents = [];
    this.publish();
  }

  // ------------------------------------------------------------------ internals

  private publish(): void {
    this.syncDerived();
    const view = this.buildViewModel();
    // Copy the set: a listener is allowed to unsubscribe while it is being notified.
    for (const listener of [...this.listeners]) listener(view);
  }

  /**
   * Refreshes the facts only the engine can compute: the legal-move cache for the player to move and
   * the validity of the current selection. Idempotent, and deliberately unable to hand the turn over
   * — that stays a rules decision.
   */
  private syncDerived(): void {
    if (this.game.gameOver) {
      this.selected = null;
      return;
    }
    if (!this.game.isRollPhase) this.engine.evaluateAllowedPlaces(this.game);

    if (this.selected == null) return;
    const piece = this.engine.findPiece(this.game, this.selected);
    if (piece == null || piece.owner !== this.game.currentPlayer || !piece.isSelectable()) {
      this.selected = null;
    }
  }

  /** Opens the move phase once the dice are settled, handing over when nothing can be spent. */
  private beginMovePhase(): void {
    if (this.game.gameOver || this.game.isRollPhase) return;
    if (!this.engine.evaluateAllowedPlaces(this.game)) this.engine.nextPlayerTurn(this.game);
  }

  private buildViewModel(): MatchViewModel {
    const state = this.game;
    const selected = this.selected == null ? null : this.engine.findPiece(this.game, this.selected);
    const highlightedPlaces = selected == null ? [] : [...selected.allowedPlaces];
    const current = state.currentPlayer;

    const cells: CellView[] = state.places.map((place, index) => ({
      placeIndex: index,
      x: place.x,
      y: place.y,
      highlight: highlightedPlaces.includes(index),
      pieces: place.pieces.map((piece) => ({
        id: piece.id,
        type: piece.type,
        owner: piece.owner,
        isActive: piece.isActive,
        canBeActivated: piece.canBeActivated,
        selectable: piece.owner === current && piece.isSelectable(),
        selected: piece.id === this.selected,
      })),
    }));

    const selectablePieceIds: number[] = [];
    for (const cell of cells) {
      for (const piece of cell.pieces) {
        if (piece.selectable) selectablePieceIds.push(piece.id);
      }
    }

    const rolled = !state.isRollPhase;
    const canReroll = this.engine.canReroll(state);
    const dice: DieView[] = state.dice.map((face, index) => ({
      index,
      face,
      spent: rolled && index < state.currentActiveDie,
      active: rolled && !state.gameOver && index === state.currentActiveDie,
    }));

    return {
      boardWidth: this.engine.rules.board.width,
      boardHeight: this.engine.rules.board.height,
      cells,
      turnPhase: state.turnPhase,
      currentPlayer: current,
      currentPlayerLabel: ownerName(current),
      phase: state.gameOver ? "gameover" : state.isRollPhase ? "roll" : "move",
      statusText: this.statusText(state, selected),
      dice: {
        rolled,
        currentActiveDie: state.currentActiveDie,
        dice,
        canRoll: this.canRoll,
        canReroll,
        orderText: diceOrderText(state, rolled),
      },
      captures: { p1: state.p1Captures, p2: state.p2Captures },
      selectedPieceId: this.selected,
      selectablePieceIds,
      highlightedPlaces,
      events: [...this.lastEvents],
      eventMessages: this.lastEvents.map(describeEvent),
      gameOver: state.gameOver
        ? {
            winner: state.winner,
            winnerLabel: ownerName(state.winner),
            reason: state.winReason,
            reasonLabel: winReasonLabel(state.winReason),
          }
        : null,
    };
  }

  private statusText(state: GameState, selected: { type: PieceType; owner: PieceOwner } | null): string {
    if (state.gameOver) {
      return `${ownerName(state.winner)} wins — ${winReasonLabel(state.winReason)}.`;
    }

    const who = ownerName(state.currentPlayer);
    if (state.isRollPhase) return `${who}: roll the dice.`;
    if (this.engine.canReroll(state)) {
      // Which faces may be re-thrown is the ruleset's call, so the hint names them from there.
      const faces = this.engine.rules.dice.reroll.faces.join(" / ");
      return `${who}: ${faces} is showing — reroll that die, or keep the dice and move.`;
    }
    if (selected != null) {
      return `${who}: ${pieceName(selected.type, selected.owner)} selected — click a highlighted cell to move it.`;
    }
    return `${who}: select a piece to move with die ${state.currentActiveDie + 1} of ${state.dice.length}.`;
  }
}

// ---------------------------------------------------------------------------------- helpers

function describeEvent(event: RuleEvent): string {
  const who = ownerName(event.owner);
  switch (event.kind) {
    case RuleEventKind.PieceMoved:
      return `${who} moved`;
    case RuleEventKind.SoldierCaptured:
      return `${who} captured a soldier`;
    case RuleEventKind.KingRecruited:
      return `${who} recruited the king`;
    case RuleEventKind.QueenCaptured:
      return `${who} captured the queen`;
    case RuleEventKind.GameWon:
      return `${ownerName(event.winner)} wins`;
    default:
      return `${who} acted`;
  }
}

function winReasonLabel(reason: WinReason): string {
  switch (reason) {
    case WinReason.OpponentSoldiersExhausted:
      return "the opponent has no soldiers left";
    case WinReason.QueenCaptured:
      return "the queen was captured";
    default:
      return "the game is over";
  }
}

function diceOrderText(state: GameState, rolled: boolean): string {
  if (state.gameOver) return "The match is over.";
  if (!rolled) return "The dice are still to be thrown this turn.";
  return `Spending die ${state.currentActiveDie + 1} of ${state.dice.length}.`;
}
