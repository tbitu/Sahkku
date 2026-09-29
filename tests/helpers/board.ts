/**
 * Test doubles shared by the agent and benchmark suites — the same `Board` and ruleset loader the C#
 * fixtures share out of `RulesTests.cs` and `TestRuleset.cs`.
 *
 * A `Board` starts from a real `initGame` and then empties every place, so a test can place exactly the
 * pieces its scenario needs while the engine, the track and the ruleset stay the shipped ones.
 */

import {
  DieFace,
  EngineOptions,
  GameState,
  Piece,
  PieceOwner,
  PieceType,
  TurnPhase,
} from "../../src/rules/domain";
import { RulesEngine } from "../../src/rules/engine";
import { loadShippedRuleset, type RuleSet } from "../../src/rules/ruleset";

/** Supplies the shipped ruleset to the agent and benchmark tests. */
export class TestRuleset {
  static load(): RuleSet {
    return loadShippedRuleset();
  }
}

/** The engine every fixture in these suites plays against. */
export function testEngine(): RulesEngine {
  return new RulesEngine(TestRuleset.load());
}

/** A bare board with no pieces, for exercising movement/capture rules in isolation. */
export class Board {
  readonly engine: RulesEngine;
  readonly state: GameState;

  private nextId = 1000;

  constructor(engine: RulesEngine, startingPlayer: PieceOwner = PieceOwner.P1) {
    this.engine = engine;
    this.state = engine.initGame(new EngineOptions(startingPlayer, false));
    for (const place of this.state.places) place.pieces.length = 0;
  }

  add(
    placeIndex: number,
    type: PieceType,
    owner: PieceOwner,
    active = false,
    activatable = false,
  ): Piece {
    const piece = new Piece(this.nextId++, placeIndex, type, owner, active);
    piece.arc = this.engine.arcOfPlace(owner, placeIndex);
    piece.canBeActivated = activatable;
    this.state.places[placeIndex].pieces.push(piece);
    return piece;
  }

  /** Places a piece on a specific arc, i.e. on a specific pass over the board. */
  addOnArc(
    arc: number,
    type: PieceType,
    owner: PieceOwner,
    active = false,
    activatable = false,
  ): Piece {
    const place = this.engine.placeOfArc(owner, arc);
    const piece = new Piece(this.nextId++, place, type, owner, active);
    piece.arc = arc;
    piece.canBeActivated = activatable;
    this.state.places[place].pieces.push(piece);
    return piece;
  }

  /** Sets the dice and resets the active die to the first one. */
  setDice(...faces: DieFace[]): void {
    this.state.dice.length = 0;
    this.state.dice.push(...faces);
    this.state.currentActiveDie = 0;
  }

  phase(phase: TurnPhase): void {
    this.state.turnPhase = phase;
  }

  activeDie(index: number): void {
    this.state.currentActiveDie = index;
  }
}
