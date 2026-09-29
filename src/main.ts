/**
 * Application entry point: wires the pure `RulesEngine` to the 2D presentation layer.
 *
 * The composition is deliberately thin. The engine owns the rules, `WebMatchController` owns the
 * match loop and exposes a plain view model, and the three view classes own the pixels — this file
 * only builds them, subscribes the views to the controller and boots a fresh local match.
 *
 * The match starts from the shipped ruleset's even-odds setup (three soldiers already loose on each
 * side), which is the setup the game is normally played from.
 */

import { EngineOptions, PieceOwner, PieceType, RerollDecision, type IRandomSource } from "./rules/domain";
import { RulesEngine } from "./rules/engine";
import { loadShippedRuleset } from "./rules/ruleset";
import { BoardView } from "./ui/board";
import { WebMatchController, type GameOverView, type MatchViewModel } from "./ui/controller";
import { DiceView } from "./ui/dice";
import { PieceRenderer } from "./ui/pieces";

/** How long a rule-event banner stays on screen. */
const BANNER_TIMEOUT_MS = 5200;

/**
 * Draws the die faces the way the engine's `IRandomSource` contract asks: uniform indices into the
 * ruleset's own face table, so the client never assumes a face count or an ordering of its own.
 */
class UniformRandomSource implements IRandomSource {
  private readonly faceCount: number;

  constructor(faceCount: number) {
    this.faceCount = Math.max(1, faceCount);
  }

  nextDieFaceIndex(): number {
    return Math.floor(Math.random() * this.faceCount);
  }
}

export interface SahkkuApp {
  engine: RulesEngine;
  controller: WebMatchController;
}

/** Builds the whole client against an existing document and starts a local match. */
export function startSahkkuApp(doc: Document = document): SahkkuApp {
  const byId = (id: string): HTMLElement => {
    const element = doc.getElementById(id);
    if (element == null) throw new Error(`The page is missing the #${id} element.`);
    return element;
  };

  const engine = new RulesEngine(loadShippedRuleset());
  const random = new UniformRandomSource(engine.rules.dice.faces.length);
  const controller = new WebMatchController(
    engine,
    random,
    new EngineOptions(PieceOwner.P1, true),
  );

  const board = new BoardView(byId("board"), engine.rules);
  const dice = new DiceView(byId("dice-tray"), byId("dice-actions"));
  const statusBar = byId("status-bar");
  const banner = byId("banner");
  const modalRoot = byId("modal-root");

  renderLegend(byId("legend"), doc);

  byId("restart-button").addEventListener("click", () => {
    controller.resetMatch();
  });

  const presenter = new MatchPresenter(doc, banner, modalRoot, statusBar, controller);

  const render = (view: MatchViewModel): void => {
    board.render(view, {
      onPlaceClick: (placeIndex) => {
        controller.handlePlaceClick(placeIndex);
      },
    });
    dice.render(view, {
      onRoll: () => {
        controller.roll();
      },
      onReroll: () => {
        controller.decideReroll(RerollDecision.RerollActiveDie);
      },
      onKeep: () => {
        controller.decideReroll(RerollDecision.KeepDiceAndProceed);
      },
    });
    presenter.update(view);
  };

  controller.subscribe(render);
  render(controller.viewModel);

  return { engine, controller };
}

// ---------------------------------------------------------------------------------- presentation

/** Owns the transient chrome around the board: the status line, the event banner and the modal. */
class MatchPresenter {
  private readonly doc: Document;
  private readonly statusBar: HTMLElement;
  private readonly banner: HTMLElement;
  private readonly modalRoot: HTMLElement;
  private readonly controller: WebMatchController;

  private bannerSignature = "";
  private bannerTimer: number | null = null;
  private modalSignature = "";

  constructor(
    doc: Document,
    banner: HTMLElement,
    modalRoot: HTMLElement,
    statusBar: HTMLElement,
    controller: WebMatchController,
  ) {
    this.doc = doc;
    this.banner = banner;
    this.modalRoot = modalRoot;
    this.statusBar = statusBar;
    this.controller = controller;
  }

  update(view: MatchViewModel): void {
    this.statusBar.textContent = view.statusText;
    this.statusBar.dataset["phase"] = view.phase;
    this.showBanner(view.eventMessages);
    this.showGameOver(view.gameOver);
  }

  private showBanner(messages: readonly string[]): void {
    const signature = messages.join("|");
    if (signature === this.bannerSignature) return;
    this.bannerSignature = signature;

    if (messages.length === 0) {
      this.banner.hidden = true;
      this.banner.textContent = "";
      return;
    }

    this.banner.hidden = false;
    this.banner.textContent = messages.join(" · ");

    const win = this.doc.defaultView;
    if (win == null) return;
    if (this.bannerTimer != null) win.clearTimeout(this.bannerTimer);
    this.bannerTimer = win.setTimeout(() => {
      this.banner.hidden = true;
      this.bannerSignature = "";
      this.bannerTimer = null;
    }, BANNER_TIMEOUT_MS);
  }

  private showGameOver(gameOver: GameOverView | null): void {
    const signature = gameOver == null ? "" : `${gameOver.winner}:${gameOver.reason}`;
    if (signature === this.modalSignature) return;
    this.modalSignature = signature;

    this.modalRoot.textContent = "";
    if (gameOver == null) return;

    const doc = this.doc;

    const overlay = doc.createElement("div");
    overlay.className = "modal";
    overlay.setAttribute("role", "dialog");
    overlay.setAttribute("aria-modal", "true");
    overlay.setAttribute("aria-label", "Game over");

    const card = doc.createElement("div");
    card.className = "modal-card";

    const title = doc.createElement("h2");
    title.className = "modal-title";
    title.textContent = `${gameOver.winnerLabel} wins`;

    const body = doc.createElement("p");
    body.className = "modal-body";
    body.textContent = `Victory: ${gameOver.reasonLabel}.`;

    const actions = doc.createElement("div");
    actions.className = "modal-actions";

    const again = doc.createElement("button");
    again.type = "button";
    again.className = "button button--primary";
    again.textContent = "Play Again";
    again.addEventListener("click", () => {
      this.controller.resetMatch();
    });
    actions.appendChild(again);

    card.append(title, body, actions);
    overlay.appendChild(card);
    this.modalRoot.appendChild(overlay);
  }
}

/** The piece legend, drawn with the very same tokens the board uses. */
function renderLegend(container: HTMLElement, doc: Document): void {
  const entries: Array<{
    type: PieceType;
    owner: PieceOwner;
    isActive: boolean;
    label: string;
  }> = [
    { type: PieceType.Soldier, owner: PieceOwner.P1, isActive: true, label: "Woman (P1 soldier)" },
    { type: PieceType.Soldier, owner: PieceOwner.P2, isActive: true, label: "Man (P2 soldier)" },
    { type: PieceType.Queen, owner: PieceOwner.P1, isActive: true, label: "P1 queen" },
    { type: PieceType.Queen, owner: PieceOwner.P2, isActive: true, label: "P2 queen" },
    { type: PieceType.King, owner: PieceOwner.None, isActive: true, label: "King (neutral, recruitable)" },
    { type: PieceType.Soldier, owner: PieceOwner.P1, isActive: false, label: "Seated in the home row" },
  ];

  container.textContent = "";
  for (const entry of entries) {
    const item = doc.createElement("span");
    item.className = "legend-item";

    const swatch = doc.createElement("span");
    swatch.className = "legend-swatch";
    swatch.appendChild(
      PieceRenderer.createToken(
        { type: entry.type, owner: entry.owner, isActive: entry.isActive },
        doc,
      ),
    );

    const text = doc.createElement("span");
    text.textContent = entry.label;

    item.append(swatch, text);
    container.appendChild(item);
  }

  const carvingItem = doc.createElement("span");
  carvingItem.className = "legend-item";
  const carvingSwatch = doc.createElement("span");
  carvingSwatch.className = "legend-swatch legend-swatch--carving";
  carvingSwatch.setAttribute("aria-hidden", "true");
  const carvingText = doc.createElement("span");
  carvingText.textContent = "Sacred carved cell";
  carvingItem.append(carvingSwatch, carvingText);
  container.appendChild(carvingItem);
}

if (typeof document !== "undefined") startSahkkuApp();
