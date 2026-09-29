/**
 * The random CPU: a 1:1 port of `RandomPlayerAgent` in
 * `Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs`.
 *
 * It is the bot the Unity game shipped with, and its draw is deliberately preserved, quirk included:
 * it picks a random movable piece, then draws an index in `[0, 4)` and clamps it to that piece's
 * options — the pre-engine CPU's draw, which is *not* uniform over the real range. It also always
 * throws a sáhkku die again, which is why a turn could only continue via the re-roll button.
 */

import { Move, RerollDecision, type GameState, type PieceOwner } from "../rules/domain";
import type { BotRandomSource, PlayerAgent } from "./types";

export class RandomPlayerAgent implements PlayerAgent {
  readonly owner: PieceOwner;
  readonly name: string;

  private readonly random: BotRandomSource | null;

  constructor(owner: PieceOwner, name: string | null = null, random: BotRandomSource | null = null) {
    this.owner = owner;
    this.name = name ?? "Random";
    this.random = random;
  }

  decideReroll(_state: GameState, _signal?: AbortSignal): Promise<RerollDecision> {
    return Promise.resolve(RerollDecision.RerollActiveDie);
  }

  decideMove(_state: GameState, legalMoves: readonly Move[]): Promise<Move | null> {
    if (legalMoves.length === 0) return Promise.resolve(null);
    if (this.random == null) return Promise.resolve(legalMoves[0]);

    const pieceIds: number[] = [];
    for (const move of legalMoves) {
      if (!pieceIds.includes(move.pieceId)) pieceIds.push(move.pieceId);
    }

    const pieceId = pieceIds[clamp(this.random.nextInt(0, pieceIds.length), 0, pieceIds.length - 1)];

    const targets: number[] = [];
    for (const move of legalMoves) {
      if (move.pieceId === pieceId) targets.push(move.targetPlaceIndex);
    }

    // The original draw clamped the raw [0, 4) roll instead of drawing inside the real range.
    const index = clamp(this.random.nextInt(0, 4), 0, targets.length - 1);
    return Promise.resolve(new Move(pieceId, targets[index]));
  }
}

function clamp(value: number, minimum: number, maximum: number): number {
  if (value < minimum) return minimum;
  if (value > maximum) return maximum;
  return value;
}
