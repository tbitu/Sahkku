/**
 * 2D piece tokens.
 *
 * Every token is a small inline SVG drawn with plain SVG primitives — no sprites, no 3D meshes and no
 * image assets, so the whole client stays open-license and dependency-light. The renderer is a pure
 * function of a `PieceTokenSpec`: the same spec always produces the same element, and nothing here
 * reads the game state, the engine or the ruleset.
 *
 * The four traditional pieces are visually distinct:
 *
 * - **Women** (player one's soldiers): a conical token in the warm ochre/crimson motif.
 * - **Men** (player two's soldiers): a tall cylindrical token in the royal blue/teal motif.
 * - **Queen**: a crowned token of the owning side.
 * - **King**: a taller, five-point-crowned token with an aura; neutral stone-grey until a player
 *   recruits it, then wearing the recruiter's colour.
 *
 * A piece that has not been activated yet is drawn seated in its home row — smaller, dimmer and
 * resting lower — while an activated piece is raised and bright.
 */

import { defaultTranslate, type Translator } from "../locale/i18n";
import { PieceOwner, PieceType } from "../rules/domain";

const SVG_NAMESPACE = "http://www.w3.org/2000/svg";

/** Everything the renderer needs to draw one piece. Deliberately free of engine types. */
export interface PieceTokenSpec {
  type: PieceType;
  owner: PieceOwner;

  /** An activated piece is drawn raised and bright; a seated one dimmer and lower. */
  isActive: boolean;

  /** Draws the selection ring. */
  selected?: boolean;

  /** Extra class names, e.g. to size a token in the legend. */
  className?: string;
}

export class PieceRenderer {
  /**
   * Builds one piece token. `doc` defaults to the ambient browser document and can be passed
   * explicitly by a host that renders into another document (or an iframe).
   */
  static createToken(spec: PieceTokenSpec, doc: Document = ambientDocument()): SVGSVGElement {
    const svg = createSvg(doc, "svg", {
      class: tokenClasses(spec),
      viewBox: "0 0 64 64",
      focusable: "false",
    }) as SVGSVGElement;

    svg.setAttribute("aria-hidden", "true");

    if (spec.type === PieceType.King) {
      svg.appendChild(createSvg(doc, "circle", { class: "piece-aura", cx: 32, cy: 30, r: 30 }));
    }

    for (const part of bodyParts(doc, spec)) svg.appendChild(part);
    for (const part of crownParts(doc, spec)) svg.appendChild(part);

    svg.appendChild(
      createSvg(doc, "ellipse", {
        class: "piece-ring",
        cx: 32,
        cy: 52,
        rx: 21,
        ry: 7,
      }),
    );

    return svg;
  }

  /** The human-readable name used in status bars, aria labels and the legend. */
  static describe(spec: PieceTokenSpec, t: Translator = defaultTranslate): string {
    const state = t(spec.isActive ? "pieces.active" : "pieces.seated");
    return t("pieces.describe", { piece: t(pieceNameKey(spec.type, spec.owner)), state });
  }
}

/**
 * The locale key of a side's name: player one fields the Women, player two the Men. Naming goes through
 * the string tables rather than a hardcoded English word, so a piece is called *Nisu* on a Sámi board
 * and *Kvinne* on a Norwegian one — and the legend, the status line and the board's aria labels cannot
 * drift apart, because they all resolve the same key.
 */
export function ownerNameKey(owner: PieceOwner): string {
  switch (owner) {
    case PieceOwner.P1:
      return "owners.p1";
    case PieceOwner.P2:
      return "owners.p2";
    default:
      return "owners.none";
  }
}

/** The locale key of a piece's name, qualified by whose side its kind belongs to. */
export function pieceNameKey(type: PieceType, owner: PieceOwner): string {
  switch (type) {
    case PieceType.Soldier:
      if (owner === PieceOwner.P1) return "pieces.woman";
      if (owner === PieceOwner.P2) return "pieces.man";
      return "pieces.soldier";
    case PieceType.Queen:
      return "pieces.queen";
    default:
      return owner === PieceOwner.None ? "pieces.neutralKing" : "pieces.king";
  }
}

// ---------------------------------------------------------------------------------- internals

function tokenClasses(spec: PieceTokenSpec): string {
  const classes = ["piece", `piece--${typeSlug(spec.type)}`, `piece--${ownerSlug(spec.owner)}`];
  classes.push(spec.isActive ? "piece--active" : "piece--inactive");
  if (spec.selected === true) classes.push("piece--selected");
  if (spec.className != null && spec.className.length > 0) classes.push(spec.className);
  return classes.join(" ");
}

function typeSlug(type: PieceType): string {
  switch (type) {
    case PieceType.Soldier:
      return "soldier";
    case PieceType.Queen:
      return "queen";
    default:
      return "king";
  }
}

function ownerSlug(owner: PieceOwner): string {
  switch (owner) {
    case PieceOwner.P1:
      return "p1";
    case PieceOwner.P2:
      return "p2";
    default:
      return "neutral";
  }
}

/** The trunk of the token: a cone for the Women, a tall cylinder for the Men, a plinth for royals. */
function bodyParts(doc: Document, spec: PieceTokenSpec): SVGElement[] {
  switch (spec.type) {
    case PieceType.Soldier:
      return spec.owner === PieceOwner.P2 ? manParts(doc) : womanParts(doc);
    case PieceType.Queen:
      return royalParts(doc, "M32 22 L45 48 L19 48 Z", 48, 14);
    default:
      return royalParts(doc, "M22 28 L42 28 L46 48 L18 48 Z", 48, 14);
  }
}

/** Player one's soldier: a stylized conical token (the Women). */
function womanParts(doc: Document): SVGElement[] {
  return [
    createSvg(doc, "path", { class: "piece-body", d: "M32 10 L47 45 L17 45 Z" }),
    createSvg(doc, "ellipse", { class: "piece-shade", cx: 32, cy: 46, rx: 15, ry: 6 }),
    createSvg(doc, "circle", { class: "piece-insignia", cx: 32, cy: 31, r: 3.4 }),
  ];
}

/** Player two's soldier: a stylized tall cylindrical token (the Men). */
function manParts(doc: Document): SVGElement[] {
  return [
    createSvg(doc, "rect", {
      class: "piece-body",
      x: 20,
      y: 13,
      width: 24,
      height: 33,
      rx: 7,
    }),
    createSvg(doc, "ellipse", { class: "piece-cap", cx: 32, cy: 13, rx: 12, ry: 5 }),
    createSvg(doc, "ellipse", { class: "piece-shade", cx: 32, cy: 46, rx: 12, ry: 5 }),
    createSvg(doc, "circle", { class: "piece-insignia", cx: 32, cy: 30, r: 3.4 }),
  ];
}

/** A royal piece: a plinth with a crown above it. */
function royalParts(
  doc: Document,
  trunkPath: string,
  baseY: number,
  baseRadiusX: number,
): SVGElement[] {
  return [
    createSvg(doc, "path", { class: "piece-body", d: trunkPath }),
    createSvg(doc, "ellipse", {
      class: "piece-shade",
      cx: 32,
      cy: baseY,
      rx: baseRadiusX,
      ry: 5.5,
    }),
    createSvg(doc, "circle", { class: "piece-insignia", cx: 32, cy: baseY - 12, r: 3 }),
  ];
}

/** The crown: three points for a queen, five for the king, so the two cannot be confused. */
function crownParts(doc: Document, spec: PieceTokenSpec): SVGElement[] {
  if (spec.type === PieceType.Soldier) return [];

  const points =
    spec.type === PieceType.King
      ? "M16 26 L20 11 L26 21 L32 7 L38 21 L44 11 L48 26 Z"
      : "M18 24 L23 12 L28 20 L36 20 L41 12 L46 24 Z";

  return [createSvg(doc, "path", { class: "piece-crown", d: points })];
}

function createSvg(
  doc: Document,
  tag: string,
  attributes: Record<string, string | number>,
): SVGElement {
  const node = doc.createElementNS(SVG_NAMESPACE, tag);
  for (const [name, value] of Object.entries(attributes)) node.setAttribute(name, String(value));
  return node;
}

function ambientDocument(): Document {
  if (typeof document === "undefined") {
    throw new Error(
      "PieceRenderer needs a DOM document; pass one as the second argument outside a browser.",
    );
  }
  return document;
}
