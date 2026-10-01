/**
 * Pure presentation of a `GameState`: the 1:1 port of the retired C# `GameStateFormatter`.
 *
 * A formatter is a pure function of the state — same state in, same string out; it reads only from
 * `GameState.places`, `GameState.dice` and the pieces' stored arcs, with no engine calls, no
 * randomness and no side effects.
 */

import { DieFace, GameState, Move, PieceOwner, PieceType, Place, Piece, WinReason } from "./domain";

export class GameStateFormatter {
  /**
   * Renders the board as a grid of lines, one per board row: top line is the highest y (in the
   * shipped ruleset that is player two's home row), bottom line is y=0 (player one). Each cell shows
   * the pieces on that place: '.' when empty, otherwise one token per piece — 'S'/'Q'/'K' plus the
   * owner digit (0 neutral, 1 P1, 2 P2) — with a count for stacks of identical tokens (e.g.
   * 'S1(x2)') and '+' between distinct tokens in stable order.
   */
  static formatBoardAscii(state: GameState): string {
    if (state == null || state.places.length === 0) {
      throw new Error("FormatBoardAscii requires a game state with at least one place.");
    }

    let width = 0;
    let height = 0;
    for (const place of state.places) {
      if (place.x + 1 > width) width = place.x + 1;
      if (place.y + 1 > height) height = place.y + 1;
    }

    const cells = new Array<string | null>(height * width).fill(null);
    let cellWidth = 3;
    for (const place of state.places) {
      if (place.x < 0 || place.x >= width || place.y < 0 || place.y >= height) continue;
      const content = cellContent(place);
      cells[place.y * width + place.x] = content;
      if (content.length > cellWidth) cellWidth = content.length;
    }

    let out = "";
    for (let y = height - 1; y >= 0; --y) {
      if (y < height - 1) out += "\n";
      out += `y=${y} |`;
      for (let x = 0; x < width; ++x) {
        let content = cells[y * width + x];
        if (content == null) content = "."; // a place the state does not declare renders empty
        if (x < width - 1 && content.length < cellWidth) content = pad(content, cellWidth);
        out += ` ${content}`;
      }
    }
    return out;
  }

  /**
   * The board grid without the row labels' padding rules being repeated: the ASCII grid is the
   * contract, so this is a thin, intention-revealing alias of `formatBoardAscii`.
   */
  static formatBoard(state: GameState): string {
    return GameStateFormatter.formatBoardAscii(state);
  }

  /**
   * The dice hand in the ruleset's spending order, one line per die, with the die up for spending
   * marked `[active]`. Before the throw it reports that the dice are still to come.
   */
  static formatDice(state: GameState): string {
    if (state == null) throw new Error("FormatDice requires a game state.");
    if (state.isRollPhase) return "- Not yet thrown this turn.\n";

    let out = "";
    for (let i = 0; i < state.dice.length; ++i) {
      out += i === state.currentActiveDie ? "- [active] " : "- ";
      out += `${dieFaceName(state.dice[i])} (die ${i})\n`;
    }
    return out;
  }

  /**
   * A structured Markdown context for an agent that must decide the current player's action: turn
   * and phase, game-over status, capture scores, the dice in spending order with the active die
   * marked, the board grid, the pieces under the current player's control (with coordinates and
   * activation status), and every legal move as a numbered list. The move numbers are the indices
   * into `legalMoves`, which is what an agent returns from `decideMove`. Deterministic for a given
   * state and move list.
   */
  static formatPromptContext(state: GameState, legalMoves: readonly Move[]): string {
    if (state == null || state.places.length === 0) {
      throw new Error("FormatPromptContext requires a game state with at least one place.");
    }
    if (legalMoves == null) throw new Error("FormatPromptContext requires a list of legal moves.");

    const current = state.currentPlayer;
    let out = "";

    out += "# Sahkku decision context\n";
    out += `- Turn: ${ownerName(current)}, phase: ${state.isRollPhase ? "roll" : "move"}\n`;
    if (state.gameOver) {
      out += `- Game over: yes, winner ${ownerName(state.winner)} (${WinReason[state.winReason]})\n`;
    } else {
      out += "- Game over: no\n";
    }
    out += `- Captures: P1: ${state.p1Captures}, P2: ${state.p2Captures}\n`;

    out += "\n## Dice in spending order\n";
    out += GameStateFormatter.formatDice(state);

    out += `\n## Board\n${GameStateFormatter.formatBoardAscii(state)}\n`;

    out += `\n## Your pieces (${ownerName(current)})\n`;
    let anyPiece = false;
    for (const place of state.places) {
      for (const piece of place.pieces) {
        if (piece.owner !== current) continue;
        anyPiece = true;
        out += `- ${pieceName(piece.type)} #${piece.id} at (x=${place.x}, y=${place.y}), place ${piece.placeIndex}, arc ${piece.arc}`;
        if (piece.isActive) out += ": active";
        else if (piece.canBeActivated) out += ": inactive, may be activated";
        else out += ": inactive";
        out += "\n";
      }
    }
    if (!anyPiece) out += "- (none)\n";

    out += "\n## Legal moves\n";
    if (legalMoves.length === 0) {
      out += "(no legal moves)\n";
    }
    for (let i = 0; i < legalMoves.length; ++i) {
      out += `${moveDescription(state, i, legalMoves[i])}\n`;
    }

    return out;
  }
}

// ------------------------------------------------------------------ internals

/** The cell text for one place: '.', a single token, or a stack summary. */
function cellContent(place: Place): string {
  if (place.pieces.length === 0) return ".";
  if (place.pieces.length === 1) return tokenOf(place.pieces[0]);

  // Stable order for mixed stacks: by piece type, then owner.
  const ordered = place.pieces.slice();
  ordered.sort((a, b) => {
    const byType = a.type - b.type;
    if (byType !== 0) return byType;
    return a.owner - b.owner;
  });

  let out = "";
  for (let i = 0; i < ordered.length; ) {
    let j = i + 1;
    while (j < ordered.length && tokenOf(ordered[j]) === tokenOf(ordered[i])) ++j;
    if (out.length > 0) out += "+";
    out += tokenOf(ordered[i]);
    if (j - i > 1) out += `(x${j - i})`;
    i = j;
  }
  return out;
}

/** 'S'/'Q'/'K' plus the owner digit: S1 is player one's soldier, K0 the neutral king. */
function tokenOf(piece: Piece): string {
  const letter = piece.type === PieceType.Soldier ? "S" : piece.type === PieceType.Queen ? "Q" : "K";
  return `${letter}${piece.owner}`;
}

function moveDescription(state: GameState, index: number, move: Move): string {
  let out = `[${index}] `;

  const located = locatePiece(state, move.pieceId);
  if (located == null) return `${out}(unknown piece)`;
  const { piece, place: source } = located;
  if (move.targetPlaceIndex < 0 || move.targetPlaceIndex >= state.places.length) {
    return `${out}${pieceName(piece.type)} #${piece.id} -> (off-board)`;
  }

  const target = state.places[move.targetPlaceIndex];
  out += `${pieceName(piece.type)} #${piece.id} at line ${source.x}, row ${source.y} (place ${piece.placeIndex}) -> line ${target.x}, row ${target.y} (place ${move.targetPlaceIndex})`;
  if (target.y !== source.y) out += `, crosses to row ${target.y}`;
  return out;
}

function pad(value: string, width: number): string {
  let out = value;
  for (let i = value.length; i < width; ++i) out += " ";
  return out;
}

/** Finds a piece by id together with the place it currently sits on. */
function locatePiece(state: GameState, pieceId: number): { piece: Piece; place: Place } | null {
  for (const place of state.places) {
    for (const piece of place.pieces) {
      if (piece.id === pieceId) return { piece, place };
    }
  }
  return null;
}

function ownerName(owner: PieceOwner): string {
  switch (owner) {
    case PieceOwner.P1:
      return "P1";
    case PieceOwner.P2:
      return "P2";
    default:
      return "neutral";
  }
}

function pieceName(type: PieceType): string {
  switch (type) {
    case PieceType.Soldier:
      return "Soldier";
    case PieceType.Queen:
      return "Queen";
    default:
      return "King";
  }
}

function dieFaceName(face: DieFace): string {
  switch (face) {
    case DieFace.Sahhku:
      return "X";
    case DieFace.Three:
      return "III";
    case DieFace.Two:
      return "II";
    default:
      return "-";
  }
}
