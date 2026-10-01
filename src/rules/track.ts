/**
 * Track geometry: the 1:1 port of the retired C# `Track`.
 *
 * A lap visits `legs.length * board.width` cells and the middle row is visited twice per lap, so two
 * arcs map onto the same board cell. A piece therefore stores its arc (see `Piece.arc`) and
 * `placeOf` is the only single-valued direction of the mapping.
 */

import { RuleSetException } from "./domain";
import { TrackDirections, type BoardRules, type TrackRules } from "./ruleset";

/** The board's cell numbering, kept in one place so the engine and the track agree. */
export class BoardLayout {
  /** Maps board coordinates onto the S-shaped path index; returns -1 when off the board. */
  static indexFromCoordinates(width: number, height: number, x: number, y: number): number {
    if (x < 0 || x >= width || y < 0 || y >= height) return -1;
    const i = y % 2 === 1 ? width - 1 - x : x;
    return y * width + i;
  }

  static coordinatesFromIndex(width: number, index: number): { x: number; y: number } {
    const y = Math.trunc(index / width);
    const i = index % width;
    const x = y % 2 === 1 ? width - 1 - i : i;
    return { x, y };
  }
}

/**
 * Arc ↔ board-cell mapping for one player, built from the ruleset's `track.legs`.
 *
 * A lap visits `legs.length * width` cells. The middle row is visited twice per lap, so two arcs
 * map onto the same `Place`; a piece must therefore store its arc, and `placeOf` is the only
 * single-valued direction.
 */
export class BoardTrack {
  private readonly width: number;
  private readonly height: number;
  private readonly placeOfArc: number[];
  private readonly legRows: number[];

  private constructor(width: number, height: number, placeOfArc: number[], legRows: number[]) {
    this.width = width;
    this.height = height;
    this.placeOfArc = placeOfArc;
    this.legRows = legRows;
  }

  /** Number of arcs in one lap. */
  get length(): number {
    return this.placeOfArc.length;
  }

  get legCount(): number {
    return this.legRows.length;
  }

  rowOfLeg(leg: number): number {
    return this.legRows[leg];
  }

  /** The row a piece starts on and returns to at the end of a lap. */
  get homeRow(): number {
    return this.legRows[0];
  }

  static build(board: BoardRules, track: TrackRules): BoardTrack {
    const legCount = track.legs.length;
    const placeOfArc = new Array<number>(legCount * board.width);
    const legRows = new Array<number>(legCount);

    for (let leg = 0; leg < legCount; ++leg) {
      const rules = track.legs[leg];
      legRows[leg] = rules.row;
      for (let offset = 0; offset < board.width; ++offset) {
        const x =
          rules.direction === TrackDirections.Right ? offset : board.width - 1 - offset;
        placeOfArc[leg * board.width + offset] = BoardLayout.indexFromCoordinates(
          board.width,
          board.height,
          x,
          rules.row,
        );
      }
    }

    return new BoardTrack(board.width, board.height, placeOfArc, legRows);
  }

  /** The board-mirrored track, i.e. the track of the player at the opposite end. */
  mirror(): BoardTrack {
    const mirrored = new Array<number>(this.placeOfArc.length);
    for (let arc = 0; arc < this.placeOfArc.length; ++arc) {
      const { x, y } = BoardLayout.coordinatesFromIndex(this.width, this.placeOfArc[arc]);
      mirrored[arc] = BoardLayout.indexFromCoordinates(
        this.width,
        this.height,
        this.width - 1 - x,
        this.height - 1 - y,
      );
    }
    return new BoardTrack(this.width, this.height, mirrored, this.mirrorRows());
  }

  private mirrorRows(): number[] {
    const rows = new Array<number>(this.legRows.length);
    for (let i = 0; i < this.legRows.length; ++i) rows[i] = this.height - 1 - this.legRows[i];
    return rows;
  }

  legOf(arc: number): number {
    return Math.trunc(this.normalize(arc) / this.width);
  }

  offsetOf(arc: number): number {
    return this.normalize(arc) % this.width;
  }

  /** The board cell an arc points at. */
  placeOf(arc: number): number {
    return this.placeOfArc[this.normalize(arc)];
  }

  /** The first arc that points at a board cell, or -1 when the cell is not on this track. */
  firstArcOf(place: number): number {
    for (let arc = 0; arc < this.placeOfArc.length; ++arc) {
      if (this.placeOfArc[arc] === place) return arc;
    }
    return -1;
  }

  /** The arc of a cell inside one leg, or -1 when that leg does not cover the cell. */
  arcOfPlaceInLeg(leg: number, place: number): number {
    const start = leg * this.width;
    for (let offset = 0; offset < this.width; ++offset) {
      if (this.placeOfArc[start + offset] === place) return start + offset;
    }
    return -1;
  }

  /**
   * Resolves where a piece crossing between rows lands on the track. A crossing keeps the piece's
   * column and enters the target row at that column; when the target row is visited by two legs (the
   * middle row) the piece continues into the *next* leg, which is what makes a crossing count as
   * "straight on" rather than a turn.
   */
  crossingArc(fromArc: number, targetRow: number, targetPlace: number): number {
    const fromLeg = this.legOf(fromArc);
    let target = -1;
    let covered = 0;
    for (let leg = 0; leg < this.legRows.length; ++leg) {
      if (this.legRows[leg] !== targetRow) continue;
      covered++;
      if (target < 0) target = leg;
      if (leg === fromLeg + 1) target = leg;
    }
    if (covered === 0) return -1;
    if (covered > 1 && target === -1) return -1;
    return this.arcOfPlaceInLeg(target, targetPlace);
  }

  /** The line `offset` lines further along the track, clamped to the lap. */
  arcInHomeRow(offset: number): number {
    if (offset < 0 || offset >= this.width) return -1;
    return offset;
  }

  /** Bring an arc into `[0, length)`, so a piece's stored arc stays canonical. */
  normalize(arc: number): number {
    const length = this.placeOfArc.length;
    if (length === 0) throw new RuleSetException("The track has no legs.");
    const wrapped = arc % length;
    return wrapped < 0 ? wrapped + length : wrapped;
  }
}
