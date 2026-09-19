---
artifact_type: task
artifact_id: task_sahkku_phase1_engine_decisions_and_state_v1
task_family_id: engine-decisions-and-state
sequence_key: "1"
task_id: 1-engine-decisions-and-state
title: "Rules Engine Decision Expansion, Starting Throw Integration, and State Serialization"
status: archived
phase: phase1
target_files:
  - "Sahkku/Assets/Scripts/Rules/Domain.cs"
  - "Sahkku/Assets/Scripts/Rules/RulesEngine.cs"
  - "Sahkku/Assets/Scripts/Rules/GameStateFormatter.cs"
  - "Sahkku/Assets/Tests/Rules/RulesTests.cs"
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

# Task: Rules Engine Decision Expansion, Starting Throw Integration, and State Serialization

## L0 - Policy

### Goal

Extend the pure-C# rules engine ([`Sahkku.Rules`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs)) with explicit turn decision primitives (separating the choice to reroll from the choice to keep and move), integrate the ruleset-defined starting player throw into game initialization options, define the asynchronous player agent contract ([`IPlayerAgent`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs)), and implement a pure-C# state formatter ([`GameStateFormatter`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/GameStateFormatter.cs)) that outputs deterministic ASCII board grids and structured prompt context for LLM agents and CLI runners. All additions must be 100% Unity-free and covered by headless NUnit tests via `dotnet test Tools/RulesTests`.

### Domain / Control Model Summary

1. **Business Invariant**: [`SahkkuRules.json`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Resources/SahkkuRules.json) is the sole authority for game mechanics. Rerolling a sáhkku (X) die is strictly optional per the printed rules: a player may choose to reroll or keep the X to spend on an activation or 1-step move. The engine must explicitly support both choices without forcing automated rerolls.
2. **Control Model**: [`RulesEngine`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs) controls turn state progression. State formatters are pure functions of [`GameState`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs#L116-L171) without side effects.
3. **Read-Path Rule**: `GameStateFormatter` reads directly from `GameState.places`, `GameState.dice`, and the owner's `BoardTrack` to project the authoritative 3x15 board state into ASCII and structured JSON context.
4. **Forbidden Fallback**: The engine must never auto-reroll without an explicit command, nor allow rerolling once a die has been spent (`beforeUsingAnyDie`).
5. **Allowed Resolution Path**: When a player throws for starting player, [`RulesEngine.ThrowForStartingPlayer`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs#L99-L127) rolls until the first sáhkku appears or counts sáhkku per `rules.start.mode`, returning the winning `PieceOwner`.
6. **Missing-Data Rule**: If a piece has no legal moves, `GetAllowedPlaces` returns an empty list, and the turn progression cleanly advances or hands over without exceptions.
7. **Phase Boundary**:
   - Contract & Producer closure: Owned here (`Sahkku.Rules` assembly, `GameStateFormatter`, `IPlayerAgent` interface, and unit tests).
   - Unity Match orchestration: Deferred to Task 2 (`2-match-controller-and-hotseat`).
   - LLM REST client & transport: Deferred to Task 3 (`3-llm-agent-and-endpoint`).

### Plan Linkage

1. **Parent plan gap closed**: Closes the decision completeness and state serialization foundation in [`plans/sahkku-architecture-plan.md`](file:///home/tarjeib/repo/Sahkku/plans/sahkku-architecture-plan.md).
2. **Depends on**: N/A (builds directly on existing verified `Sahkku.Rules`).
3. **Unlocks / impacts successors**: Unlocks Task 2 (`2-match-controller-and-hotseat`) and Task 3 (`3-llm-agent-and-endpoint`).
4. **Inherited validation / exit expectation**: `dotnet test Tools/RulesTests/RulesTests.csproj` passes all existing 66 tests plus new tests for optional rerolls, throw-for-start, and state formatting.

### Scope Reality / Shape Proof

1. **Inspected entrypoints**:
   - [`Sahkku/Assets/Scripts/Rules/Domain.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs)
   - [`Sahkku/Assets/Scripts/Rules/RulesEngine.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs)
   - [`Sahkku/Assets/Tests/Rules/RulesTests.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Tests/Rules/RulesTests.cs)
2. **Actual touched scope**: `contract_or_persisted_authority_foundation` + `authority_producer` in pure-C# engine.
3. **Hidden scope ruled out**: No Unity MonoBehaviours (`GameLogic.cs`, `GameInteraction.cs`) or UI assets touched in this slice.

### Mandatory Gates

1. **Complexity-Risk Gate**:
   - `authority_risk`: 1 (adds decision methods and serialization to existing engine)
   - `surface_spread`: 1 (touches `Sahkku.Rules` and `RulesTests`)
   - `identity_join_risk`: 0
   - `activation_coupling`: 1 (foundation for Task 2/3)
   - `prerequisite_risk`: 0
   - `acceptance_multiplicity`: 1
   - Total score: 4 (Low risk, single task slice appropriate).
2. **Authority Fan-Out Scan**:
   - `authority_producer`: `RulesEngine` decision methods (`DecideReroll`, `InitGameWithThrow`)
   - `internal_execution_consumers`: Future `MatchController` (Task 2)
   - `read_model_consumers`: `GameStateFormatter` (used by future LLM agent and CLI)
   - All other buckets: absent.
3. **Closure-Budget Gate**:
   - Modifies `authority_producer` and adds `GameStateFormatter` read-model helper in the same bounded pure-C# assembly.
   - Safe collapse: Both are pure C# code paths in `Sahkku.Rules` validated by the same headless test runner.
4. **Bounded-Task-Shape Gate**:
   - Primary shape: `contract_or_persisted_authority_foundation`.
   - Secondary shape: `authority_producer`.

---

## L1 - Implementation Contract

### 1. Data Contracts & Agent Interface ([`Sahkku/Assets/Scripts/Rules/Domain.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs))

```csharp
namespace Sahkku.Rules
{
    public enum RerollDecision
    {
        KeepDiceAndProceed = 0,
        RerollActiveDie = 1
    }

    /// <summary>
    /// Asynchronous agent interface implemented by Human, Heuristic, and LLM controllers.
    /// Pure C# interface living in Sahkku.Rules so headless tools can instantiate agents.
    /// </summary>
    public interface IPlayerAgent
    {
        PieceOwner Owner { get; }
        string Name { get; }

        System.Threading.Tasks.Task<RerollDecision> DecideRerollAsync(
            GameState state,
            System.Threading.CancellationToken cancellationToken);

        System.Threading.Tasks.Task<Move> DecideMoveAsync(
            GameState state,
            IReadOnlyList<Move> legalMoves,
            System.Threading.CancellationToken cancellationToken);
    }
}
```

### 2. Engine Decision Primitives ([`Sahkku/Assets/Scripts/Rules/RulesEngine.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs))

1. **Decouple Roll vs Move Transition**:
   - `RollDice(GameState state, IRandomSource random)` rolls dice and puts them into ruleset spending order.
   - `ApplyRerollDecision(GameState state, IRandomSource random, RerollDecision decision)`:
     - If `RerollActiveDie`: verifies `CanReroll(state)`, re-rolls `state.dice[state.currentActiveDie]`, calls `OrderDice(state)`.
     - If `KeepDiceAndProceed`: advances state so rerolling is no longer offered for this throw, moving directly to move evaluation.
2. **Starting Throw Option**:
   - Add `EngineOptions.throwForStartingPlayer` boolean. If true, `InitGame` rolls for the starting player using `ThrowForStartingPlayer(random)` instead of blindly using `options.startingPlayer`.

### 3. State Formatter & Prompt Serializer ([`Sahkku/Assets/Scripts/Rules/GameStateFormatter.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/GameStateFormatter.cs))

Create a pure-C# static utility `GameStateFormatter`:
1. `FormatBoardAscii(GameState state)`:
   Renders a 3-row, 15-column ASCII board showing:
   - Row 2: Player 2 Home (`.`, `S2`, `Q2`, `K2`, `K0`)
   - Row 1: Middle Row / Castle (`.`, `Q1`, `Q2`, `K0`, etc.)
   - Row 0: Player 1 Home (`.`, `S1`, `Q1`, `K1`, `K0`)
   - Stacks indicated where multiple pieces share a line (e.g. `S1(x2)`).
2. `FormatPromptContext(GameState state, IReadOnlyList<Move> legalMoves)`:
   Outputs a structured Markdown/JSON string containing:
   - Current turn & phase
   - Active die & remaining dice in hand
   - Captures score (`P1: X`, `P2: Y`)
   - Pieces under the player's control with coordinates and activation status
   - Numbered list of all legal moves with human-readable descriptions (e.g. `[0] Soldier #5 at line 4 -> line 5 (advance forward)`).

### 4. Headless Test Matrix ([`Sahkku/Assets/Tests/Rules/RulesTests.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Tests/Rules/RulesTests.cs))

| Test ID | Method Name | Expected Behavior |
|---|---|---|
| `T1_REROLL_KEEP` | `RerollDecision_KeepDice_ProceedsToMoveWithoutRerolling` | When rolling a sáhkku, choosing `KeepDiceAndProceed` retains the sáhkku die (value 1) and enables moves without modifying dice. |
| `T1_REROLL_EXEC` | `RerollDecision_Reroll_ReplacesDieAndReorders` | Choosing `RerollActiveDie` re-rolls the active die face, re-orders dice according to ruleset, and maintains invariants. |
| `T1_REROLL_FORBID` | `RerollDecision_AfterDieSpent_RefusesReroll` | Calling reroll after `currentActiveDie > 0` throws `IllegalMoveException`. |
| `T1_THROW_START` | `InitGame_WithThrowForStartingPlayer_SetsValidPlayer` | When `throwForStartingPlayer` option is true, `InitGame` uses `ThrowForStartingPlayer` to select P1 or P2. |
| `T1_FORMAT_ASCII` | `GameStateFormatter_FormatBoardAscii_RendersValidGrid` | ASCII representation accurately reflects positions of soldiers, queens, and king on the 3x15 board. |
| `T1_FORMAT_PROMPT` | `GameStateFormatter_FormatPromptContext_ContainsAllLegalMoves` | Output prompt string contains exact match for each move index in `RulesEngine.LegalMoves(state)`. |

---

## L2 - Hardening Backlog & Review Scope Fence

### Hardening Backlog
- Task 2: Adapt Unity `GameLogic` and `GameInteraction` to consume `IPlayerAgent` and `RerollDecision` dialog.
- Task 3: Implement OpenAI HTTP client connecting `LlmPlayerAgent` to LM Studio on `localhost:1234`.
- Task 4: CLI headless tournament runner.

### Review Scope Fence
- **No Unity API Usage**: The files in `Sahkku/Assets/Scripts/Rules/` and `Tools/RulesTests/` must not import `UnityEngine` or `UnityEditor`.
- **Validation**: Task implementation is complete when `dotnet test Tools/RulesTests/RulesTests.csproj` passes 100% with 0 failures and 0 warnings.
