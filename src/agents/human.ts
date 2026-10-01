/**
 * The human player: the 1:1 port of the retired C# `HumanPlayerAgent`.
 *
 * The agent owns no policy at all — it forwards both decisions to the injected interaction, which the
 * 2D web client implements on top of clicks and buttons. Without an interaction (a headless seat, or
 * a UI that has not been wired yet) it behaves the way the reference does: keep the dice and return no
 * move.
 */

import { RerollDecision, type GameState, type Move, type PieceOwner } from "../rules/domain";
import type { HumanInteraction, PlayerAgent } from "./types";

/** A human playing at the board: every decision is delegated to the configured interaction. */
export class HumanPlayerAgent implements PlayerAgent {
  readonly owner: PieceOwner;
  readonly name: string;

  private readonly interaction: HumanInteraction | null;

  constructor(owner: PieceOwner, name: string | null = null, interaction: HumanInteraction | null = null) {
    this.owner = owner;
    this.name = name ?? "Human";
    this.interaction = interaction;
  }

  decideReroll(state: GameState, signal?: AbortSignal): Promise<RerollDecision> {
    if (this.interaction == null) return Promise.resolve(RerollDecision.KeepDiceAndProceed);
    return this.interaction.requestReroll(state, signal);
  }

  decideMove(state: GameState, legalMoves: readonly Move[], signal?: AbortSignal): Promise<Move | null> {
    if (this.interaction == null) return Promise.resolve(null);
    return this.interaction.requestMove(state, legalMoves, signal);
  }
}
