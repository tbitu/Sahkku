---
artifact_type: task
artifact_id: task_2_ts_agents_and_cli_bench_v1
task_family_id: ts-agents-and-cli-bench
sequence_key: "2"
task_id: 2-ts-agents-and-cli-bench
title: "Port Player Agents (Human, Random, Heuristic, LLM) and Headless CLI Benchmark to TypeScript"
status: approved
phase: implementation
target_files:
  - package.json
  - src/agents/types.ts
  - src/agents/human.ts
  - src/agents/random.ts
  - src/agents/heuristic.ts
  - src/agents/llm.ts
  - src/agents/config.ts
  - src/cli/match-runner.ts
  - src/cli/benchmark-stats.ts
  - src/cli/cli.ts
  - tests/agents/player-agents.test.ts
  - tests/agents/llm-agent.test.ts
  - tests/cli/bench.test.ts
prd_ref: null
plan_ref: plans/2026-09-29-sahkku-ts-refactor-and-2d-frontend-plan.md
system_context_ref: null
owners:
  - "tarjeib"
doc_bubble_id: null
impl_bubble_id: 2-ts-agents-and-cli-bench
supersedes: []
superseded_by: null
archive_group: 2026-09-29-sahkku-ts-refactor-and-2d-frontend
---

# Task 2: Port Player Agents (Human, Random, Heuristic, LLM) and Headless CLI Benchmark to TypeScript

## L0: Context & Scope

### Problem Summary
With the core TypeScript rules engine in place, we need to port the player agent framework and off-engine CLI benchmarking harness from C# ([`Sahkku.RulesBridge`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs) and [`Tools/SahkkuBench`](file:///home/tarjeib/repo/Sahkku/Tools/SahkkuBench/MatchRunner.cs)) to strict TypeScript:
1. **Agent Implementations**: `HumanPlayerAgent` (delegating to interactive handlers), `RandomPlayerAgent` (reproducing the legacy random piece selection), `HeuristicPlayerAgent` (deterministic greedy scoring and queen/activation defense), and `LlmPlayerAgent` (connecting to OpenAI-compatible `/v1/chat/completions` endpoints with structured JSON parsing, prompt formatting via `GameStateFormatter`, and heuristic fallback).
2. **Headless Match Runner & Benchmark CLI**: Headless match orchestrator running games through `RulesEngine` off-browser, calculating comprehensive statistics (win rates, half-moves, captures, rerolls, LLM fallbacks), and exposing a CLI tool callable via `npm run bench`.
3. **Automated Vitest Coverage**: Porting all unit tests from `PlayerAgentTests.cs`, `LlmAgentTests.cs`, and `BenchmarkTests.cs` to Vitest with scripted offline mocks.

### Scope Reality Proof
- **Inspected C# Reference Files**:
  - [`Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs): `IHumanInteraction`, `IBotRandomSource`, `HumanPlayerAgent`, `RandomPlayerAgent`, `HeuristicPlayerAgent`.
  - [`Sahkku/Assets/Scripts/RulesBridge/LlmClient.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/LlmClient.cs): `ILlmTransport`, `HttpClientLlmTransport`, `LlmChatRequest`, `LlmResponseParser`.
  - [`Sahkku/Assets/Scripts/RulesBridge/LlmPlayerAgent.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/LlmPlayerAgent.cs): Structured move/reroll prompting and error fallback.
  - [`Tools/SahkkuBench/MatchRunner.cs`](file:///home/tarjeib/repo/Sahkku/Tools/SahkkuBench/MatchRunner.cs): `MatchConfig`, `MatchResult`, `MatchRunner`.
  - [`Tools/SahkkuBench/BenchmarkStats.cs`](file:///home/tarjeib/repo/Sahkku/Tools/SahkkuBench/BenchmarkStats.cs): Aggregate statistics, ASCII table and JSON formatting.
  - [`Tools/SahkkuBench/Program.cs`](file:///home/tarjeib/repo/Sahkku/Tools/SahkkuBench/Program.cs): Command-line parsing and execution.
  - [`Sahkku/Assets/Tests/Rules/PlayerAgentTests.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Tests/Rules/PlayerAgentTests.cs), [`LlmAgentTests.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Tests/Rules/LlmAgentTests.cs), [`BenchmarkTests.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Tests/Rules/BenchmarkTests.cs).
- **Bounded Slice**:
  - `src/agents/`: Types, Human, Random, Heuristic, LLM agent with fetch transport, and shared JSON config loader.
  - `src/cli/`: `match-runner.ts`, `benchmark-stats.ts`, `cli.ts` executable via `npm run bench`.
  - `tests/agents/`: Comprehensive Vitest coverage for all agent types and offline LLM mocks.
  - `tests/cli/`: Vitest coverage for benchmark runner and stats aggregation.

### Scoped Invariants
- `applies_to`: `src/agents/*`, `src/cli/*`, `tests/agents/*`, `tests/cli/*`, `package.json`.
- `does_not_apply_to`: Browser UI components, DOM/Canvas rendering, Unity scenes.
- `proof_surface`: `npm test` passes all existing rules tests plus all new agent and bench tests; `npm run bench -- --games 10 --p1 heuristic --p2 random` runs headlessly and prints stats.

---

## L1: Implementation Contract

### 1. Functional Requirements

- **REQ_AGENT_CONTRACT**: In `src/agents/types.ts`:
  - `PlayerAgent` interface: `owner: PieceOwner`, `name: string`, `decideReroll(state: GameState, signal?: AbortSignal): Promise<RerollDecision>`, `decideMove(state: GameState, legalMoves: readonly Move[], signal?: AbortSignal): Promise<Move>`.
  - `HumanInteraction` interface: `requestReroll(state: GameState, signal?: AbortSignal): Promise<RerollDecision>`, `requestMove(state: GameState, legalMoves: readonly Move[], signal?: AbortSignal): Promise<Move>`.
  - `BotRandomSource` interface: `nextInt(minInclusive: number, maxExclusive: number): number`.
- **REQ_HUMAN_AGENT**: In `src/agents/human.ts`, `HumanPlayerAgent` forwards `decideReroll` and `decideMove` to the injected `HumanInteraction` implementation. If interaction is missing, defaults to `KeepDiceAndProceed` and default `Move`.
- **REQ_RANDOM_AGENT**: In `src/agents/random.ts`, `RandomPlayerAgent`:
  - Always returns `RerollActiveDie` for `decideReroll`.
  - For `decideMove`, selects a random distinct `pieceId` from legal moves, then a random move for that piece using `botRandomSource.nextInt(0, 4)` clamped to available moves, matching the legacy C# CPU logic.
- **REQ_HEURISTIC_AGENT**: In `src/agents/heuristic.ts`, `HeuristicPlayerAgent`:
  - `chooseReroll(engine, state)`: Keeps die if a piece can be activated or queen is threatened; otherwise rerolls.
  - `chooseMove(engine, state, legalMoves)`: Scores each legal move based on captures (+100 for soldiers, +1000 for queen, +300 for recruiting king), moving queen out of threat (+500), moving pieces closer to enemy territory, and king safety. Deterministic tie-breaking selects the first highest-scoring move.
- **REQ_LLM_AGENT**: In `src/agents/llm.ts`, `LlmPlayerAgent`:
  - Injected `LlmTransport` interface (`postChatCompletion(url, json, signal): Promise<string>`) with default `FetchLlmTransport` using global `fetch`.
  - Formats context via `GameStateFormatter.formatPromptContext`.
  - Single legal move optimization: returns immediately without sending network request.
  - JSON parser reads `{ move_index: N }` or `{ reroll: boolean }`, supporting markdown fences and json within text.
  - On network failure, HTTP non-200, timeout, or unparseable output, logs a warning and falls back to `HeuristicPlayerAgent`.
- **REQ_MATCH_RUNNER**: In `src/cli/match-runner.ts`:
  - `MatchConfig`: `maxHalfMoves`, `seed`, `useRandomSeed`, `startingPlayer`, `throwForStartingPlayer`, `turnTimeoutMs`, `evenOdds`.
  - Runs turns: rolls dice, queries agent for reroll while `canReroll`, queries agent for move, validates legality with `engine.isLegalMove`, applies move via `engine.applyMove`, runs `engine.validateState`.
  - Handles turn-limit draws and timeouts cleanly.
- **REQ_BENCH_STATS**: In `src/cli/benchmark-stats.ts`:
  - Computes P1 wins, P2 wins, draws, average half-moves, captures, rerolls, and fallback counts.
  - Formats results into clean text table and JSON output.
- **REQ_CLI_COMMAND**: In `src/cli/cli.ts` and `package.json`:
  - Expose `npm run bench` with flags: `--games <N>`, `--p1 <human|random|heuristic|llm>`, `--p2 <human|random|heuristic|llm>`, `--endpoint <url>`, `--model <name>`, `--seed <N>`, `--json`.

### 2. Call-Site Matrix

| Target File | Method / Entity | Description |
|---|---|---|
| `package.json` | scripts | Add `"bench": "tsx src/cli/cli.ts"` and devDependency `tsx`. |
| `src/agents/types.ts` | `PlayerAgent` | Define agent and interaction interfaces. |
| `src/agents/human.ts` | `HumanPlayerAgent` | Interactive agent adapter. |
| `src/agents/random.ts` | `RandomPlayerAgent` | Clamped random bot. |
| `src/agents/heuristic.ts` | `HeuristicPlayerAgent` | Deterministic evaluator and fallback. |
| `src/agents/llm.ts` | `LlmPlayerAgent` | LLM NPC with JSON parser and fallback. |
| `src/cli/match-runner.ts` | `MatchRunner` | Simulation match state machine. |
| `src/cli/benchmark-stats.ts` | `BenchmarkStats` | Results aggregation and reporting. |
| `src/cli/cli.ts` | CLI entry point | Process CLI flags and run benchmark suite. |

### 3. Error and Fallback Handling

- `LlmPlayerAgent` must never throw on network failure or malformed JSON; it falls back to `HeuristicPlayerAgent.chooseMove` / `chooseReroll`.
- `MatchRunner` aborts game with explicit violation if an agent returns an illegal move that bypasses `isLegalMove`.

### 4. Test Matrix

| Test ID | Input / Scenario | Expected Outcome |
|---|---|---|
| `T2_AGENT_TESTS` | `npm test -- tests/agents` | Human, Random, Heuristic unit tests pass. |
| `T2_LLM_AGENT_TESTS` | Scripted `LlmTransport` with valid/invalid/timeout inputs | Correct moves chosen, fallbacks triggered on bad outputs. |
| `T2_BENCH_TESTS` | `npm test -- tests/cli` | MatchRunner simulates games, stats agree with assertions. |
| `T2_CLI_RUN` | `npm run bench -- --games 5 --p1 heuristic --p2 random` | Headless CLI runs and prints valid match summary. |

---

## L2: Hardening Backlog

- Add support for Ollama native `/api/generate` endpoint alongside OpenAI `/v1/chat/completions`.
- Implement parallel worker threads for multi-game benchmarks if scaling above 1,000 games.
