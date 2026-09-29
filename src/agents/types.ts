/**
 * The player-agent contract: a 1:1 port of `IHumanInteraction`, `IBotRandomSource` and `IPlayerAgent`
 * from `Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs`.
 *
 * An agent is the only thing that *decides*; the rules engine remains the sole authority over what is
 * legal (see the plan's control model). Nothing in here depends on a host, a renderer or a socket, so
 * the browser client, the headless benchmark and the tests all drive their agents through exactly the
 * same three interfaces.
 */

import type { GameState, Move, PieceOwner, RerollDecision } from "../rules/domain";

/**
 * An actor seated at the board. Both decisions are asynchronous and take an optional `AbortSignal`,
 * which is how a host cancels a pending decision when the match ends; an implementation that ignores
 * the signal is still correct, it merely cannot be interrupted while it thinks.
 */
export interface PlayerAgent {
  /** The side this agent plays. The match runner seats agents by owner, never by list position. */
  readonly owner: PieceOwner;

  /** Human-readable name, used by the benchmark's report and by the agent's own log lines. */
  readonly name: string;

  /**
   * The player's answer to the re-roll that is on offer. Called only while
   * `RulesEngine.canReroll(state)` holds — the engine, not the agent, decides whether a question
   * exists at all.
   */
  decideReroll(state: GameState, signal?: AbortSignal): Promise<RerollDecision>;

  /**
   * The player's answer to `legalMoves`, which the engine produced and which must be the only source
   * of legal actions. `null` mirrors the C# `default(Move)`: there was nothing to decide, i.e. the
   * caller offered an empty list. The match runner never asks for a move when the engine offers none.
   */
  decideMove(state: GameState, legalMoves: readonly Move[], signal?: AbortSignal): Promise<Move | null>;
}

/**
 * The human player's half of the interaction: the presentation layer answers these two requests (a
 * click on a piece plus a destination, a click on one of the re-roll buttons) and resolves the
 * returned promise. Keeping it an interface is what lets `HumanPlayerAgent` live outside the browser
 * and be exercised by headless tests with a scripted implementation.
 */
export interface HumanInteraction {
  /** Asks the human whether the sáhkku on offer should be thrown again. Never resolves on its own. */
  requestReroll(state: GameState, signal?: AbortSignal): Promise<RerollDecision>;

  /** Asks the human which of the `legalMoves` to play. */
  requestMove(state: GameState, legalMoves: readonly Move[], signal?: AbortSignal): Promise<Move>;
}

/**
 * Randomness for bot agents. Deliberately wider than the engine's `IRandomSource` (which only draws
 * die faces) so a bot can draw the same two ranges the pre-engine CPU drew, while a test can pin them.
 */
export interface BotRandomSource {
  /**
   * Returns a value in `[minimumInclusive, maximumExclusive)`. An empty range has to yield a value
   * below `minimumInclusive`, which is what the legacy draw relied on before it clamped the result.
   */
  nextInt(minimumInclusive: number, maximumExclusive: number): number;
}
