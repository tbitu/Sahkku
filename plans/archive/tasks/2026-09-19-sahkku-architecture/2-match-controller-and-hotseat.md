---
artifact_type: task
artifact_id: task_sahkku_phase2_match_controller_and_hotseat_v1
task_family_id: match-controller-and-hotseat
sequence_key: "2"
task_id: 2-match-controller-and-hotseat
title: "Match Controller Decoupling, Asynchronous Player Agents, and 2-Player Hotseat"
status: archived
phase: phase2
target_files:
  - "Sahkku/Assets/Scripts/GameLogic.cs"
  - "Sahkku/Assets/Scripts/GameInteraction.cs"
  - "Sahkku/Assets/Scripts/GameSettings.cs"
  - "Sahkku/Assets/Scripts/RulesBridge/ActionSources.cs"
  - "Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs"
  - "Sahkku/Assets/Localization/Scripts/MenuManager.cs"
prd_ref: null
plan_ref: plans/sahkku-architecture-plan.md
system_context_ref: null
owners:
  - "tarjeib"
doc_bubble_id: null
impl_bubble_id: 2-match-controller-and-hotseat
supersedes: []
superseded_by: null
archive_group: 2026-09-19-sahkku-architecture
---

# Task: Match Controller Decoupling, Asynchronous Player Agents, and 2-Player Hotseat

## L0 - Policy

### Goal

Refactor the Unity gameplay driver ([`GameLogic.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameLogic.cs)) and presentation interaction ([`GameInteraction.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameInteraction.cs)) into an asynchronous match orchestration architecture driven by the [`IPlayerAgent`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs) contract. Eliminate hardcoded single-player and Player 2 AI checks (`if (GameSettings.singlePlayer && ...)`), implement concrete player agents ([`HumanPlayerAgent`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs), [`RandomPlayerAgent`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs), and [`HeuristicPlayerAgent`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs)), and provide full 1-player (Human vs Bot) and 2-player (Human vs Human local hotseat) parity with clear turn announcements, symmetric interaction, and explicit reroll choice UI.

### Domain / Control Model Summary

1. **Business Invariant**: All rule decisions and state transitions remain strictly governed by [`RulesEngine`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs). Neither human input nor AI agents may bypass legal move checks or directly mutate [`GameState`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs#L116-L171).
2. **Control Model**: The match driver manages the turn state machine asynchronously. Two player slots (`Player1Agent` and `Player2Agent`) are queried for decisions (`DecideRerollAsync`, `DecideMoveAsync`). The UI/View layer reacts to state changes and provides input resolution via `TaskCompletionSource`.
3. **Read-Path Rule**: Player agent types are configured in [`GameSettings.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameSettings.cs) (`AgentType { Human, RandomBot, HeuristicBot, LlmBot }`). The match loop queries the agent matching `state.CurrentPlayer`.
4. **Forbidden Fallback**: The engine/driver must never assume Player 2 is always a computer player, nor execute AI decisions synchronously on the main thread.
5. **Allowed Resolution Path**: When a human player has the option to reroll a sáhkku die, `GameInteraction` displays an explicit prompt ("Reroll Die" vs "Keep and Move"), allowing the player to resolve `DecideRerollAsync`.
6. **Missing-Data Rule**: If an agent yields no move when legal moves exist, or an invalid move is returned, the controller catches the exception and falls back to a deterministic heuristic selection to avoid hanging the match.
7. **Phase Boundary**:
   - Unity match orchestration & local agents: Owned here.
   - Remote/Local LLM REST transport: Deferred to Task 3 (`3-llm-agent-and-endpoint`).
   - Headless CLI runner: Deferred to Task 4 (`4-cli-bench-and-eval`).

### Plan Linkage

1. **Parent plan gap closed**: Closes the match orchestration decoupling and 2-player hotseat parity in [`plans/sahkku-architecture-plan.md`](file:///home/tarjeib/repo/Sahkku/plans/sahkku-architecture-plan.md).
2. **Depends on**: `1-engine-decisions-and-state` (closed).
3. **Unlocks / impacts successors**: Unlocks Task 3 (`3-llm-agent-and-endpoint`) by providing the agent slot architecture where `LlmPlayerAgent` plugs in directly.
4. **Inherited validation / exit expectation**: Headless test suite passes, and Unity compilation succeeds with clean 1P and 2P gameplay.

### Scope Reality / Shape Proof

1. **Inspected entrypoints**:
   - [`Sahkku/Assets/Scripts/GameLogic.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameLogic.cs)
   - [`Sahkku/Assets/Scripts/GameInteraction.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameInteraction.cs)
   - [`Sahkku/Assets/Scripts/GameSettings.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameSettings.cs)
   - [`Sahkku/Assets/Scripts/RulesBridge/ActionSources.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/ActionSources.cs)
   - [`Sahkku/Assets/Localization/Scripts/MenuManager.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Localization/Scripts/MenuManager.cs)
2. **Actual touched scope**: `consumer_family_alignment` + `activation_or_read_model` across Unity gameplay MonoBehaviours.
3. **Hidden scope ruled out**: No external network code (reserved for Task 3).

### Mandatory Gates

1. **Complexity-Risk Gate**:
   - `authority_risk`: 1
   - `surface_spread`: 2 (GameLogic, GameInteraction, GameSettings, MenuManager)
   - `identity_join_risk`: 0
   - `activation_coupling`: 1
   - `prerequisite_risk`: 0
   - `acceptance_multiplicity`: 1
   - Total score: 5 (Split recommended, well-bounded to Unity presentation and agent orchestration).
2. **Authority Fan-Out Scan**:
   - `workflow_orchestration_consumers`: `GameLogic` turn state machine.
   - `internal_execution_consumers`: `HumanPlayerAgent`, `HeuristicPlayerAgent`, `RandomPlayerAgent`.
   - `read_model_consumers`: `GameInteraction` board render, turn banners, and reroll UI.
   - All other buckets: absent.
3. **Closure-Budget Gate**:
   - Collapses agent slot orchestration, hotseat UI support, and heuristic bot in the same Unity frontend layer.
   - Safe collapse: Directly replaces legacy tangled `Update()` checks in `GameLogic.cs`.
4. **Bounded-Task-Shape Gate**:
   - Primary shape: `workflow_orchestration_consumers`.
   - Secondary shape: `consumer_family_alignment`.

---

## L1 - Implementation Contract

### 1. Game Settings & Agent Taxonomy ([`Sahkku/Assets/Scripts/GameSettings.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameSettings.cs))

```csharp
public class GameSettings : MonoBehaviour
{
    public enum AgentType
    {
        Human = 0,
        RandomBot = 1,
        HeuristicBot = 2,
        LlmBot = 3
    }

    public static AgentType p1AgentType = AgentType.Human;
    public static AgentType p2AgentType = AgentType.HeuristicBot;
    public static bool throwForStartingPlayer = false;
    public static bool evenOdds = true;
    public static Player startingPlayer = Player.One;
    // ... model styles, audio toggles ...

    public static bool IsHotseat => p1AgentType == AgentType.Human && p2AgentType == AgentType.Human;
    public static bool IsSinglePlayer => (p1AgentType == AgentType.Human) ^ (p2AgentType == AgentType.Human);
}
```

### 2. Player Agent Implementations ([`Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs))

1. **`HumanPlayerAgent`**:
   - Implements `IPlayerAgent`.
   - Communicates with `GameInteraction`:
     - `DecideRerollAsync`: Triggers reroll choice buttons in UI; awaits user click resolving a `TaskCompletionSource<RerollDecision>`.
     - `DecideMoveAsync`: Enables interaction highlight for current player's selectable pieces; awaits piece selection and destination click resolving `TaskCompletionSource<Move>`.
2. **`HeuristicPlayerAgent`**:
   - Implements `IPlayerAgent`.
   - `DecideRerollAsync`: Keeps sáhkku (value 1) if an inactive soldier or queen can be activated or queen is threatened; otherwise rerolls.
   - `DecideMoveAsync`: Scores each legal move by heuristic weights:
     - Immediate capture of enemy Queen: +10,000
     - Move own Queen out of direct capture threat: +5,000
     - Capture enemy soldier: +500
     - Recruit neutral or enemy King: +400
     - Activate inactive soldier: +300
     - Advance piece towards enemy territory: +10 per step
3. **`RandomPlayerAgent`**:
   - Implements `IPlayerAgent` preserving the legacy random draw behavior.

### 3. Match Orchestration Loop ([`Sahkku/Assets/Scripts/GameLogic.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameLogic.cs))

1. Refactor `GameLogic.Update()` into an asynchronous match runner or state machine coroutine:
   - When a turn starts:
     - Roll initial dice via `engine.RollDice(state, randomSource)`.
     - Animate dice throw in `GameInteraction`.
     - Check if `engine.CanReroll(state)` is true:
       - If true, query active player: `await activeAgent.DecideRerollAsync(state, cancellationToken)`.
       - If `RerollActiveDie`, apply `engine.ApplyRerollDecision(state, randomSource, RerollDecision.RerollActiveDie)`, update dice presentation, and repeat reroll check if allowed.
       - If `KeepDiceAndProceed`, apply `engine.ApplyRerollDecision(state, randomSource, RerollDecision.KeepDiceAndProceed)`.
     - For each die:
       - If legal moves exist for the current die:
         - Query active player: `Move move = await activeAgent.DecideMoveAsync(state, legalMoves, cancellationToken)`.
         - Apply move via `engine.ApplyMove(state, move)` and play emitted `RuleEvents`.
         - Update piece transforms in `GameInteraction`.
       - If no legal moves exist, die is forfeit; proceed to next die or end turn.
     - Hand turn to next player or declare winner if `state.gameOver`.

### 4. Presentation & Hotseat UX ([`Sahkku/Assets/Scripts/GameInteraction.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameInteraction.cs))

1. **Turn Banner**:
   - Displays clear player turn status for both Player 1 and Player 2 (e.g. "Player 1's Turn", "Player 2's Turn").
   - Indicates when an AI agent is thinking vs waiting for human input.
2. **Symmetric Input Handling**:
   - Input raycast targets selectable pieces for `state.CurrentPlayer`, enabling seamless hotseat pass-and-play on desktop and touchscreen.
3. **Reroll Dialog**:
   - Provides clean UI buttons ("Reroll Sáhkku" / "Keep Die") when `DecideRerollAsync` is active for a human player.

### 5. Menu Integration ([`Sahkku/Assets/Localization/Scripts/MenuManager.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Localization/Scripts/MenuManager.cs))

1. "Play Solo" configures `p1AgentType = Human`, `p2AgentType = HeuristicBot`.
2. "Play Versus" configures `p1AgentType = Human`, `p2AgentType = Human`.
3. Adds option for "Throw for Start" vs manual starting player selection.

---

## L2 - Hardening Backlog & Review Scope Fence

### Hardening Backlog
- Task 3: Plug `LlmPlayerAgent` into `GameSettings.p2AgentType = AgentType.LlmBot`.
- Task 4: Headless console benchmark harness.

### Review Scope Fence
- Focus solely on the Unity gameplay orchestration, agent implementations, and 2-player hotseat flow.
- Ensure all existing headless tests continue to pass via `dotnet test Tools/RulesTests/RulesTests.csproj`.
