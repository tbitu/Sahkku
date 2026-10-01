/**
 * Aggregate benchmark statistics: the 1:1 port of the retired C# `BenchmarkStats` and
 * `LlmFallbackCounter`.
 *
 * The summary is filled one game at a time (`add`), so a long run never has to keep its results — which
 * matters because a `MatchResult` pins the final board of its game. The fallback counter is the one
 * derived number that cannot be read off a result: it watches the lines the LLM agent logs, and it reads
 * them *structurally*, because a model that merely quotes the agent's wording in its own reasoning must
 * not inflate the count.
 */

import { PieceOwner } from "../rules/domain";
import type { MatchResult } from "./match-runner";

/** How many error lines the printed report shows before it summarises the rest. */
const MaxReportedErrors = 5;

export class BenchmarkSummary {
  totalGames = 0;
  p1Wins = 0;
  p2Wins = 0;

  /** Games that ended as a draw: no winner, either by the turn limit or because both queens went. */
  draws = 0;

  /** Share of all games player one won, draws included in the denominator. */
  p1WinRate = 0;

  p2WinRate = 0;

  averageHalfMoves = 0;
  minHalfMoves = 0;
  maxHalfMoves = 0;

  totalCaptures = 0;

  /** Sum of the per-game wall-clock times, agent decisions included. */
  totalDurationMs = 0;

  /** Games in which an agent proposed an illegal move or a state invariant broke. */
  ruleViolations = 0;

  // ------------------------------------------------------------------ extra counters

  totalHalfMoves = 0;
  totalDiceRolls = 0;
  totalRerollDecisions = 0;

  /** How often an LLM agent gave up on its endpoint and played the heuristic choice. */
  totalLlmFallbacks = 0;

  /** Games that did not complete cleanly, whether by rule violation or by an agent/timeout error. */
  failures = 0;

  /** One line per failed game, in the order the games ran. */
  readonly errors: string[] = [];

  /** Folds one finished game into the totals. */
  add(result: MatchResult | null): void {
    if (result == null) return;

    this.totalGames++;

    if (result.winner === PieceOwner.P1) this.p1Wins++;
    else if (result.winner === PieceOwner.P2) this.p2Wins++;
    else this.draws++;

    if (result.ruleViolationOccurred) this.ruleViolations++;
    if (result.failed) {
      this.failures++;
      this.errors.push(`game ${result.matchIndex}: ${result.errorMessage}`);
    }

    this.totalHalfMoves += result.totalHalfMoves;
    this.totalDiceRolls += result.totalDiceRolls;
    this.totalRerollDecisions += result.totalRerollDecisions;
    this.totalLlmFallbacks += result.llmFallbacks;
    this.totalCaptures += result.totalCaptures;
    this.totalDurationMs += result.durationMs;

    if (this.totalGames === 1 || result.totalHalfMoves < this.minHalfMoves) {
      this.minHalfMoves = result.totalHalfMoves;
    }
    if (this.totalGames === 1 || result.totalHalfMoves > this.maxHalfMoves) {
      this.maxHalfMoves = result.totalHalfMoves;
    }

    this.p1WinRate = this.p1Wins / this.totalGames;
    this.p2WinRate = this.p2Wins / this.totalGames;
    this.averageHalfMoves = this.totalHalfMoves / this.totalGames;
  }

  /** The human-readable report: one aligned label/value column per statistic. */
  toPrettyString(p1Name: string | null, p2Name: string | null): string {
    const p1 = agentName(p1Name);
    const p2 = agentName(p2Name);

    const rows: Array<[string, string]> = [
      ["games", String(this.totalGames)],
      [`P1 wins (${p1})`, `${this.p1Wins}  ${percent(this.p1WinRate)}`],
      [`P2 wins (${p2})`, `${this.p2Wins}  ${percent(this.p2WinRate)}`],
      ["draws", String(this.draws)],
      ["half-moves avg/min/max", `${num(this.averageHalfMoves)} / ${this.minHalfMoves} / ${this.maxHalfMoves}`],
      ["dice rolls", String(this.totalDiceRolls)],
      ["re-roll decisions", String(this.totalRerollDecisions)],
      ["captures", String(this.totalCaptures)],
      ["rule violations", String(this.ruleViolations)],
      ["failures", String(this.failures)],
      ["LLM fallbacks", String(this.totalLlmFallbacks)],
      ["duration", `${num(this.totalDurationMs / 1000)}s`],
    ];

    let width = 0;
    for (const [label] of rows) width = Math.max(width, label.length);

    const header = `Sáhkku benchmark: ${p1} (P1) vs ${p2} (P2)`;
    const lines: string[] = [header, "-".repeat(header.length)];
    for (const [label, value] of rows) lines.push(`${label.padEnd(width)}  ${value}`);

    // A failed game is the one thing a reader must not have to go looking for, so the first few reasons
    // are printed with the table rather than left to the JSON.
    for (let i = 0; i < this.errors.length && i < MaxReportedErrors; i++) {
      lines.push(`  ! ${this.errors[i]}`);
    }
    if (this.errors.length > MaxReportedErrors) {
      lines.push(`  ! ... and ${this.errors.length - MaxReportedErrors} more`);
    }

    return `${lines.join("\n")}\n`;
  }

  /** The machine-readable report, for CI: the same numbers, with the two agent names and the errors. */
  toJsonString(p1Name: string | null, p2Name: string | null): string {
    return JSON.stringify({
      p1: agentName(p1Name),
      p2: agentName(p2Name),
      totalGames: this.totalGames,
      p1Wins: this.p1Wins,
      p2Wins: this.p2Wins,
      draws: this.draws,
      p1WinRate: num(this.p1WinRate),
      p2WinRate: num(this.p2WinRate),
      averageHalfMoves: num(this.averageHalfMoves),
      minHalfMoves: this.minHalfMoves,
      maxHalfMoves: this.maxHalfMoves,
      totalHalfMoves: this.totalHalfMoves,
      totalCaptures: this.totalCaptures,
      totalDiceRolls: this.totalDiceRolls,
      totalRerollDecisions: this.totalRerollDecisions,
      totalLlmFallbacks: this.totalLlmFallbacks,
      ruleViolations: this.ruleViolations,
      failures: this.failures,
      totalDurationSeconds: num(this.totalDurationMs / 1000),
      errors: this.errors,
    });
  }
}

/**
 * Counts how often an LLM agent gave up on its endpoint and played the heuristic choice, by watching the
 * lines the agent logs.
 *
 * `LlmPlayerAgent` logs two kinds of line, and it is the shape of the line that tells them apart:
 *
 * - a decision it took from the model's own answer, which begins with `chose …` and may carry the model's
 *   free-form reasoning after it, and
 * - a fallback, which begins with one of the sentences below.
 *
 * A line is therefore matched structurally, on the message body the agent logged (everything after its
 * `<name>: ` prefix): the body has to *begin* with a fallback sentence — and, for the two that name the
 * failure they absorbed, end with it as well. Searching the line for a phrase anywhere in it would let a
 * model that merely quoted the agent's own wording in its reasoning inflate the number, because such text
 * is a decision line of an entirely different kind.
 */
export class LlmFallbackCounter {
  /** The agent's separator between its name and the line it logged. */
  private static readonly NameSeparator = ": ";

  /**
   * The four fallback lines whose wording is fixed — no endpoint configured, or an answer the agent could
   * not use. Each is the whole body the agent logs.
   */
  private static readonly FallbackSentences = [
    "no endpoint is configured; deciding heuristically.",
    "no endpoint is configured; playing the heuristic choice.",
    "the endpoint did not answer with a usable reroll decision; deciding heuristically.",
    "the endpoint did not answer with a usable move index; playing the heuristic choice.",
  ];

  /**
   * The two fallback lines that name the failure they absorbed, and so carry the transport's own text.
   * Only the opening and the closing sentence are fixed, which is why these are matched on both ends.
   */
  private static readonly FailedRequestOpenings = ["the reroll request failed (", "the move request failed ("];

  /** The two closing sentences, in the same order as the openings above. */
  private static readonly FailedRequestEndings = [
    "); deciding heuristically.",
    "); playing the heuristic choice.",
  ];

  /** How many fallbacks have been observed since the last `reset`. */
  count = 0;

  /** Forgets the fallbacks of the previous game, so each game is reported on its own. */
  reset(): void {
    this.count = 0;
  }

  /**
   * True when `message` is a fallback line, which it then counts. Every other kind of line returns false
   * and counts nothing — including a decision whose model-written reasoning quotes a fallback sentence.
   */
  observe(message: string | null): boolean {
    if (!LlmFallbackCounter.isFallback(message)) return false;
    this.count++;
    return true;
  }

  private static isFallback(message: string | null): boolean {
    if (message == null || message.length === 0) return false;

    // Every line the agent logs is prefixed with its name, and no agent name contains ": ", so the first
    // separator is the one that opens the body. A line without it is not the agent's own log.
    const separator = message.indexOf(LlmFallbackCounter.NameSeparator);
    if (separator < 0) return false;
    const body = message.slice(separator + LlmFallbackCounter.NameSeparator.length);

    if (LlmFallbackCounter.FallbackSentences.includes(body)) return true;

    for (let i = 0; i < LlmFallbackCounter.FailedRequestOpenings.length; i++) {
      if (
        body.startsWith(LlmFallbackCounter.FailedRequestOpenings[i]) &&
        body.endsWith(LlmFallbackCounter.FailedRequestEndings[i])
      ) {
        return true;
      }
    }
    return false;
  }
}

// ------------------------------------------------------------------ formatting

function agentName(name: string | null): string {
  return name == null || name.length === 0 ? "agent" : name;
}

/**
 * Four decimals, midpoints away from zero, exactly like the reference formatter. A `number` carries the
 * decision and not a string, so 0.2 is written as `0.2` and never as `0.2000`: both readers agree.
 */
function num(value: number): number {
  if (!Number.isFinite(value)) return 0;
  const scaled = value * 10000;
  const rounded = scaled >= 0 ? Math.floor(scaled + 0.5) : Math.ceil(scaled - 0.5);
  return rounded / 10000;
}

function percent(rate: number): string {
  return `${num(rate * 100)}%`;
}
