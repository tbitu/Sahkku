---
artifact_type: task
artifact_id: task_sahkku_phase4_cli_bench_and_eval_v1
task_family_id: cli-bench-and-eval
sequence_key: "4"
task_id: 4-cli-bench-and-eval
title: "Headless CLI Match Runner, Evaluation Harness, and Benchmarking Tool"
status: archived
phase: phase4
target_files:
  - "Tools/SahkkuBench/SahkkuBench.csproj"
  - "Tools/SahkkuBench/Program.cs"
  - "Tools/SahkkuBench/MatchRunner.cs"
  - "Tools/SahkkuBench/BenchmarkStats.cs"
  - "Sahkku/Assets/Tests/Rules/BenchmarkTests.cs"
  - "Tools/RulesTests/RulesTests.csproj"
prd_ref: null
plan_ref: plans/sahkku-architecture-plan.md
system_context_ref: null
owners:
  - "tarjeib"
doc_bubble_id: null
impl_bubble_id: null
supersedes: []
superseded_by: null
archive_group: 2026-09-19-sahkku-architecture
---

# Task: Headless CLI Match Runner, Evaluation Harness, and Benchmarking Tool

## L0 - Policy

### Goal

Implement a standalone, headless CLI tool ([`Tools/SahkkuBench`](file:///home/tarjeib/repo/Sahkku/Tools/SahkkuBench)) executable via `dotnet run --project Tools/SahkkuBench` that runs and benchmarks automated Sáhkku matches without requiring Unity. The tool must support matches between any combination of agent types (`heuristic`, `random`, `llm`), simulate complete turn loops (rolls, optional reroll decisions, move decisions), strictly enforce and validate all rules via [`RulesEngine`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs), assert zero [`IllegalMoveException`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs#L68-L71) or invalid state transitions, collect game statistics (win rates, turn counts, captures, roll frequencies, LLM fallback occurrences), and print structured console reports (and optional JSON output). Add automated regression tests in [`Sahkku/Assets/Tests/Rules/BenchmarkTests.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Tests/Rules/BenchmarkTests.cs) executed by `dotnet test Tools/RulesTests/RulesTests.csproj`.

### Domain / Control Model Summary

1. **Business Invariant**: The rules engine ([`RulesEngine`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs)) remains the single authoritative source of truth. The CLI harness orchestrates matches via [`RulesEngine.InitGame`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs), [`RulesEngine.RollDice`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs), [`RulesEngine.ApplyRerollDecision`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs), [`RulesEngine.LegalMoves`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs), and [`RulesEngine.ApplyMove`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs). Under no circumstances may an agent or runner bypass legality checks or directly mutate board state.
2. **Control Model**: [`MatchRunner`](file:///home/tarjeib/repo/Sahkku/Tools/SahkkuBench/MatchRunner.cs) coordinates the match loop:
   - Queries active agent via [`IPlayerAgent.DecideRerollAsync`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs#L248-L250) when `engine.CanReroll(state)` is true.
   - Queries active agent via [`IPlayerAgent.DecideMoveAsync`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs#L252-L255) when `!state.IsRollPhase` and legal moves exist.
   - Validates that every move returned satisfies `engine.IsLegalMove(state, move)` prior to mutation.
   - Asserts `engine.ValidateState(state)` is empty after every state modification.
3. **Read-Path Rule**: Agents inspect read-only [`GameState`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs#L122) and `IReadOnlyList<Move>` snapshots. The CLI reporter reads aggregated match records from [`BenchmarkStats`](file:///home/tarjeib/repo/Sahkku/Tools/SahkkuBench/BenchmarkStats.cs).
4. **Forbidden Fallback**: The harness must never catch and silence an `IllegalMoveException`. If any agent proposes an illegal move that bypasses internal validation, the runner must record a critical rule violation and abort the benchmark with a non-zero exit code.
5. **Allowed Resolution Path**: Agents resolve their own internal fallbacks (e.g. `LlmPlayerAgent` falling back to heuristic on network timeout or invalid syntax). The runner observes whether fallback was triggered without altering match progression.
6. **Missing-Data Rule**: If an agent hangs or exceeds a per-turn timeout, or if a game exceeds the configured `--max-turns` limit, the runner records a turn-limit draw or timeout failure without corrupting runner state.
7. **Phase Boundary**:
   - `Tools/SahkkuBench`: Console executable, CLI argument parsing, match runner loop, metrics aggregation, text/JSON formatters.
   - `Sahkku/Assets/Tests/Rules/BenchmarkTests.cs`: Headless tests verifying match runner correctness, turn limit handling, agent matchups, and stats aggregation.

### Plan Linkage

1. **Parent plan gap closed**: Closes the Headless CLI Match Benchmarking capability claim in [`plans/sahkku-architecture-plan.md`](file:///home/tarjeib/repo/Sahkku/plans/sahkku-architecture-plan.md). Completes the entire 4-phase architecture plan.
2. **Depends on**: `3-llm-agent-and-endpoint` (closed and merged).
3. **Unlocks / impacts successors**: Plan completion (`PlanComplete`). Delivers offline CI evaluation capability for all current and future bot/LLM agents.
4. **Inherited validation / exit expectation**: `dotnet test Tools/RulesTests/RulesTests.csproj` passes with benchmark test fixtures; `dotnet run --project Tools/SahkkuBench -- --games 10` executes cleanly with exit code 0.

### Scope Reality / Shape Proof

1. **Inspected entrypoints**:
   - [`Tools/RulesTests/RulesTests.csproj`](file:///home/tarjeib/repo/Sahkku/Tools/RulesTests/RulesTests.csproj)
   - [`Sahkku/Assets/Scripts/Rules/RulesEngine.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs)
   - [`Sahkku/Assets/Scripts/Rules/Domain.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs)
   - [`Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs)
   - [`Sahkku/Assets/Scripts/RulesBridge/LlmPlayerAgent.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/LlmPlayerAgent.cs)
   - [`Sahkku/Assets/Tests/Rules/PlayerAgentTests.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Tests/Rules/PlayerAgentTests.cs#L140-L177)
2. **Actual touched scope**: New project `Tools/SahkkuBench`, new test fixture in `Sahkku/Assets/Tests/Rules/BenchmarkTests.cs`, and `Tools/RulesTests/RulesTests.csproj` updates.
3. **Hidden scope ruled out**: Zero changes to engine rules (`RulesEngine.cs`), Unity MonoBehaviours, or localization tables.

### Mandatory Gates

1. **Complexity-Risk Gate**:
   - `authority_risk`: 0 (Pure consumer of existing engine & agent APIs).
   - `surface_spread`: 2 (`Tools/SahkkuBench`, `Tools/RulesTests`).
   - `identity_join_risk`: 0.
   - `activation_coupling`: 1 (New CLI entrypoint).
   - `prerequisite_risk`: 0.
   - `acceptance_multiplicity`: 1.
   - Total score: 4 (Clean bounded CLI tool and test suite).
2. **Authority Fan-Out Scan**:
   - `internal_execution_consumers`: `MatchRunner` calls `RulesEngine` and `IPlayerAgent`.
   - `read_model_consumers`: `BenchmarkStats` formats console and JSON output.
   - All other buckets: absent.
3. **Closure-Budget Gate**:
   - Collapses runner, stats aggregation, CLI parsing, and tests into a single project slice.
   - Safe collapse: Pure .NET 8 console application with zero external framework dependencies.
4. **Bounded-Task-Shape Gate**:
   - Primary shape: `activation_or_read_model`.
   - Secondary shape: `consumer_family_alignment` (testing engine & agent integration off-engine).

---

## L1 - Implementation Contract

### 1. Tool Project Definition ([`Tools/SahkkuBench/SahkkuBench.csproj`](file:///home/tarjeib/repo/Sahkku/Tools/SahkkuBench/SahkkuBench.csproj))

Create a standard .NET 8 executable project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>SahkkuBench</AssemblyName>
    <RootNamespace>Sahkku.Bench</RootNamespace>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="../../Sahkku/Assets/Scripts/Rules/**/*.cs" />
    <Compile Include="../../Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs" />
    <Compile Include="../../Sahkku/Assets/Scripts/RulesBridge/LlmClient.cs" />
    <Compile Include="../../Sahkku/Assets/Scripts/RulesBridge/LlmPlayerAgent.cs" />
  </ItemGroup>
</Project>
```

Ruleset loading:
- Search upward from `AppContext.BaseDirectory` and `Environment.CurrentDirectory` for `Sahkku/Assets/Resources/SahkkuRules.json`, or accept an explicit path from `--ruleset <path>` or `SAHKKU_RULESET` environment variable.

### 2. Match Runner ([`Tools/SahkkuBench/MatchRunner.cs`](file:///home/tarjeib/repo/Sahkku/Tools/SahkkuBench/MatchRunner.cs))

```csharp
namespace Sahkku.Bench
{
    public sealed class MatchConfig
    {
        public int MaxHalfMoves = 10000;
        public int Seed = 0;
        public bool UseRandomSeed = true;
        public PieceOwner StartingPlayer = PieceOwner.P1;
        public bool ThrowForStartingPlayer = false;
        public TimeSpan TurnTimeout = TimeSpan.FromSeconds(30);
    }

    public sealed class MatchResult
    {
        public int MatchIndex;
        public PieceOwner Winner; // P1, P2, or None (draw/turn limit)
        public WinReason WinReason;
        public int TotalHalfMoves;
        public int TotalDiceRolls;
        public int TotalRerollDecisions;
        public int TotalCaptures;
        public TimeSpan Duration;
        public string ErrorMessage;
        public bool RuleViolationOccurred;
    }

    public sealed class MatchRunner
    {
        readonly RulesEngine engine;
        readonly IPlayerAgent p1Agent;
        readonly IPlayerAgent p2Agent;

        public MatchRunner(RulesEngine engine, IPlayerAgent p1Agent, IPlayerAgent p2Agent)
        {
            this.engine = engine;
            this.p1Agent = p1Agent;
            this.p2Agent = p2Agent;
        }

        public async Task<MatchResult> RunMatchAsync(int matchIndex, MatchConfig config, CancellationToken cancellationToken)
        {
            // 1. Initialize engine with SeededRandomSource
            // 2. Turn loop while !state.gameOver && halfMoves < config.MaxHalfMoves:
            //    a. If state.IsRollPhase: engine.RollDice(state, random)
            //    b. While engine.CanReroll(state):
            //       - activeAgent.DecideRerollAsync
            //       - engine.ApplyRerollDecision
            //    c. While !state.gameOver && !state.IsRollPhase:
            //       - legalMoves = engine.LegalMoves(state)
            //       - if legalMoves.Count == 0: engine.NextPlayerTurn(state); break;
            //       - move = await activeAgent.DecideMoveAsync(state, legalMoves, token)
            //       - if !engine.IsLegalMove(state, move): throw / mark rule violation
            //       - engine.ApplyMove(state, move)
            //       - stateErrors = engine.ValidateState(state)
            //       - if stateErrors.Count > 0: mark state corruption violation
            // 3. Return MatchResult with stats
        }
    }
}
```

### 3. Benchmark Statistics ([`Tools/SahkkuBench/BenchmarkStats.cs`](file:///home/tarjeib/repo/Sahkku/Tools/SahkkuBench/BenchmarkStats.cs))

```csharp
namespace Sahkku.Bench
{
    public sealed class BenchmarkSummary
    {
        public int TotalGames;
        public int P1Wins;
        public int P2Wins;
        public int Draws;
        public double P1WinRate;
        public double P2WinRate;
        public double AverageHalfMoves;
        public int MinHalfMoves;
        public int MaxHalfMoves;
        public int TotalCaptures;
        public TimeSpan TotalDuration;
        public int RuleViolations;

        public string ToPrettyString(string p1Name, string p2Name);
        public string ToJsonString(string p1Name, string p2Name);
    }
}
```

### 4. CLI Entry Point ([`Tools/SahkkuBench/Program.cs`](file:///home/tarjeib/repo/Sahkku/Tools/SahkkuBench/Program.cs))

Supported CLI flags:
- `--games <N>`: Number of games to run (default: 10).
- `--p1 <heuristic|random|llm>`: Agent type for Player 1 (default: `heuristic`).
- `--p2 <heuristic|random|llm>`: Agent type for Player 2 (default: `heuristic`).
- `--endpoint <url>`: REST endpoint URL for LLM agents (default: `http://localhost:1234/v1`).
- `--model <name>`: Model identifier for LLM agents (default: `lmstudio/model` or empty).
- `--seed <int>`: Base random seed for reproducible runs (optional).
- `--max-turns <N>`: Maximum half-moves before declaring draw (default: 10000).
- `--ruleset <path>`: Explicit path to `SahkkuRules.json` (optional).
- `--json`: Output summary as JSON to stdout instead of table.
- `--verbose`: Print move-by-move or game-by-game progression.
- `--help` / `-h`: Print usage help.

Exit code:
- `0`: All games completed without rule violations or fatal crashes.
- `1`: Invalid arguments, file not found, unhandled exception, or rule violation occurred.

### 5. Automated Tests ([`Sahkku/Assets/Tests/Rules/BenchmarkTests.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Tests/Rules/BenchmarkTests.cs))

Add test suite compiled by `Tools/RulesTests/RulesTests.csproj`:
1. `MatchRunner_PlaysDeterministicHeuristicGameToCompletion`: Asserts a match completes with a decisive winner, 0 violations, and valid final state.
2. `MatchRunner_PlaysRandomBotMatchWithoutRuleViolations`: Runs Random vs Random game, asserts 0 violations.
3. `MatchRunner_EnforcesTurnLimit`: Sets a low turn limit (e.g. 20 half-moves), asserts game terminates with `Winner == None` and `WinReason.None` without infinite loop.
4. `MatchRunner_HeuristicOutperformsRandom`: Runs 10 games of Heuristic vs Random, asserts Heuristic wins >= 70% of games.
5. `BenchmarkStats_AggregatesResultsCorrectly`: Unit tests for win rates, averages, and JSON formatting.

Update [`Tools/RulesTests/RulesTests.csproj`](file:///home/tarjeib/repo/Sahkku/Tools/RulesTests/RulesTests.csproj) to include `Tools/SahkkuBench/MatchRunner.cs` and `Tools/SahkkuBench/BenchmarkStats.cs`.

### 6. Acceptance Criteria

- [AC-1]: `dotnet build Tools/SahkkuBench/SahkkuBench.csproj` succeeds with 0 errors.
- [AC-2]: `dotnet run --project Tools/SahkkuBench -- --games 5 --p1 heuristic --p2 heuristic` runs 5 games and prints summary with 0 rule violations and exit code 0.
- [AC-3]: `dotnet run --project Tools/SahkkuBench -- --games 5 --p1 heuristic --p2 random` runs 5 games and prints win stats.
- [AC-4]: `dotnet run --project Tools/SahkkuBench -- --help` prints usage instructions and exits 0.
- [AC-5]: `dotnet test Tools/RulesTests/RulesTests.csproj` passes all existing and new benchmark tests (100% green).

---

## L2 - Hardening & Edge Cases (Optional hardening, backlog, non-blocking)

1. **Parallel Execution**: Allow running multiple benchmark games in parallel via `Parallel.ForEach` or `Task.WhenAll` when seeds are disjoint.
2. **PGN/Notation Export**: Record games in standard or custom notation for replay/debugging.
3. **Elo Calculation**: Calculate estimated Elo difference between agent configurations.
