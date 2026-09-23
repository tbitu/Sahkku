---
artifact_type: task
artifact_id: task_2_interaction_and_gameplay_flow_v1
task_family_id: interaction-and-gameplay-flow
sequence_key: "2"
task_id: 2-interaction-and-gameplay-flow
status: archived
phase: implementation
target_files:
  - Sahkku/Assets/Scripts/GameInteraction.cs
  - Sahkku/Assets/Scripts/GameLogic.cs
  - Tools/PlaywrightTests/tests/game.spec.js
prd_ref: null
plan_ref: plans/2026-09-23-sahkku-webgl-gameplay-and-locale-fix-plan.md
system_context_ref: null
doc_bubble_id: null
impl_bubble_id: 2-interaction-and-gameplay-flow
supersedes: []
superseded_by: null
archive_group: 2026-09-23-sahkku-webgl-gameplay-and-locale-fix
---

# Task 2: Board Interaction, Turn Status Feedback, and Automated E2E Verification

## L0: Context & Scope

### Problem Summary
1. **Piece Interaction & Reroll Friction**:
   When a human player's turn begins in WebGL, pieces are only selectable if `pendingMove != null`. If the player rolls a Sáhkku ($X$), `RequestRerollAsync` presents the "Throw Again" and "Keep Dice" buttons while `pendingMove` is null. If an uninformed player clicks on a piece instead of the buttons, the click is silently ignored. If the player rolls no Sáhkku on Turn 1, pieces cannot be activated by rule, and the turn immediately passes to Player 2 with no feedback explaining why pieces are unclickable.
2. **Turn Handoff Visibility**:
   When no legal moves exist (or non-Sáhkku starting throw), the turn hands over instantly. Brief visual pacing and status explanation ensure the human player understands that no legal moves were available.
3. **Automated E2E Verification**:
   The Playwright test suite in `Tools/PlaywrightTests/tests/game.spec.js` must be expanded to verify:
   - Cold launch renders without Addressables or Locale Preload errors.
   - Match with Men starting (Bot) completes bot thinking and does not hang on "Thinking...".
   - Match with Women starting (Human) enables interaction and processes reroll/move input cleanly.

### Scope Reality Proof
- **Inspected Entrypoints**:
  - `GameInteraction.ReadPointer()`: lines 517-569.
  - `GameInteraction.UpdateStatusText()`: lines 398-451.
  - `GameLogic.PlayMovePhaseAsync()`: lines 270-289.
  - `GameLogic.ResolveRerollDecisionAsync()`: lines 236-267.
  - `Tools/PlaywrightTests/tests/game.spec.js`.
- **Bounded Slice**:
  - In `GameInteraction.cs`, allow clicking a selectable piece during reroll phase to auto-resolve `KeepDiceAndProceed` and select that piece, making board interaction responsive and intuitive.
  - Provide a clear status notification or brief pacing when a turn passes due to no legal moves.
  - Update `Tools/PlaywrightTests/tests/game.spec.js` to assert cold launch cleanliness, Men start completion, and Women start interaction.

### Scoped Invariants
- `applies_to`: `GameInteraction.cs`, `GameLogic.cs`, `game.spec.js`.
- `does_not_apply_to`: Core move validation rules in `RulesEngine.cs`.
- `proof_surface`: Headless rules tests pass; Playwright test suite passes all scenarios against the WebGL player.

---

## L1: Implementation Contract

### 1. Functional Requirements

- **REQ_CLICK_DURING_REROLL**: If a human player is in `RequestRerollAsync` (reroll buttons shown) and clicks directly on a piece that has legal moves (`piece.IsSelectable()`), `GameInteraction` must auto-resolve the pending reroll with `RerollDecision.KeepDiceAndProceed` and immediately select that piece for the upcoming move phase.
- **REQ_NO_LEGAL_MOVES_FEEDBACK**: When a turn has zero legal moves (such as rolling no Sáhkku on Turn 1), the match loop must display a brief status notice and delay (e.g. 1.0s frame-time) before handing the turn over, so the player sees what was rolled and why the turn passed.
- **REQ_E2E_VERIFICATION_SUITE**: `Tools/PlaywrightTests/tests/game.spec.js` must contain automated tests for:
  - Cold launch with zero `Locales PreloadOperation` and zero `WaitForCompletion` errors.
  - Men start match progression without freezing on "Thinking...".
  - Women start match readiness and interactive piece/button responsiveness.

### 2. Call-Site Matrix

| Target File | Method | Change Description |
|---|---|---|
| `Sahkku/Assets/Scripts/GameInteraction.cs` | `ReadPointer` | Support clicking selectable pieces while reroll decision is pending to auto-choose keep-dice. |
| `Sahkku/Assets/Scripts/GameLogic.cs` | `PlayMovePhaseAsync` / `ResolveRerollDecisionAsync` | Add brief pacing and status feedback when turn passes with zero legal moves. |
| `Tools/PlaywrightTests/tests/game.spec.js` | Test definitions | Update and add test cases verifying bot thinking completion and human piece interaction. |

### 3. Error and Fallback Handling

- All delays must use `UnityTimeDelays.DelayAsync` to maintain WebGL single-threaded compatibility.
- Never block or discard valid re-roll button clicks.

### 4. Test Matrix

| Test ID | Input / Scenario | Expected Outcome |
|---|---|---|
| `T2_RULES_INTEGRITY` | `dotnet test Tools/RulesTests/RulesTests.csproj` | All 139 headless rules tests pass. |
| `T2_E2E_COLD_LAUNCH` | `npx playwright test -g "cold launch"` | Passes with zero unhandled exceptions. |
| `T2_E2E_MEN_START` | Playwright test starting match with Men | Completes bot roll and think without hang. |
| `T2_E2E_WOMEN_START` | Playwright test starting match with Women | Reroll buttons or piece selection responsive. |

---

## L2: Hardening Backlog

- Add touch-drag visual feedback for mobile touch screens.
- Add sound effect when clicking unselectable pieces to signal invalid selection.
