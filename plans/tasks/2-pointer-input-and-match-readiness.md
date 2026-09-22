---
artifact_type: task
artifact_id: task_sahkku_2_pointer_input_and_match_readiness_v1
task_family_id: pointer-input-and-match-readiness
sequence_key: "2"
task_id: 2-pointer-input-and-match-readiness
title: "Fix Pointer Input Shadowing and Match Controller Presentation Readiness"
status: implementable
phase: phase1
target_files:
  - "Sahkku/Assets/Scripts/GameInteraction.cs"
prd_ref: null
plan_ref: plans/2026-09-22-sahkku-start-screen-and-gameplay-fix-plan.md
system_context_ref: null
owners:
  - "tarjeib"
doc_bubble_id: null
impl_bubble_id: null
supersedes: []
superseded_by: null
archive_group: 2026-09-22-sahkku-start-screen-and-gameplay-fix
---

# Task: Fix Pointer Input Shadowing and Match Controller Presentation Readiness

## L0 - Policy

### Goal

Eliminate match startup hangs and input freezing in `Game.unity` on WebGL by:
1. Preventing synchronous Addressable localization calls in `GameInteraction.SetupRerollButtons()`, allowing `Start()` to finish and assert `IsReady = true` so `GameLogic.WaitForPresentationAsync()` does not hang indefinitely.
2. Enabling mouse and touch input coexistence in `GameInteraction.ReadPointer()`, ensuring active mouse clicks are not discarded when `Touchscreen.current` is non-null but idle.

### Domain / Invariant Summary

1. **Presentation Readiness**:
   - `GameLogic.WaitForPresentationAsync()` awaits `GameInteraction.Instance != null && GameInteraction.Instance.IsReady`.
   - `GameInteraction.Start()` must complete without unhandled exceptions under WebGL execution.
2. **WebGL Addressables Asynchrony**:
   - Unity WebGL does not support synchronous Addressable loading (`WaitForCompletion` / synchronous `.GetLocalizedString()` throws a fatal runtime exception).
   - `GameInteraction.SetButtonLabel()` must never call `LocalizationSettings.StringDatabase.GetLocalizedString(LocalizationTable, localizationKey)`.
   - Instead, assign immediate readable fallback text (e.g. `Reroll_Die` -> "Throw Again", `Keep_Dice` -> "Keep Dice") to `TextMeshProUGUI.text`, and wire `LocalizeStringEvent.StringReference.SetReference(LocalizationTable, localizationKey)` while ensuring `localize.enabled = true` so Unity Localization resolves strings asynchronously and on language changes.
3. **Pointer Input Coexistence**:
   - `ReadPointer()` must check both `Touchscreen` and `Mouse` devices without allowing a non-null but idle `Touchscreen.current` to shadow or block `Mouse.current`.
   - In desktop browsers running WebGL builds, `Touchscreen.current` may be instantiated by the browser or input system even when the user interacts via mouse.
   - If a touch was pressed this frame (`Touchscreen.current != null && Touchscreen.current.touches.Count > 0 && Touchscreen.current.touches[0].press.wasPressedThisFrame`), evaluate touch interaction.
   - Otherwise, if mouse left button was pressed (`Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame`), evaluate mouse interaction with `Mouse.current.position.ReadValue()`.
4. **Non-Breaking Invariant**:
   - Headless test suite (`dotnet test Tools/RulesTests/RulesTests.csproj`) must pass with 0 regressions.
   - Scene wiring and component references in `Game.unity` must remain compatible.

## L1 - Architecture & Contract

### 1. GameInteraction.cs - SetupRerollButtons & SetButtonLabel
- In `SetButtonLabel(GameObject button, string localizationKey)`:
  - Remove synchronous `LocalizationSettings.StringDatabase.GetLocalizedString(...)`.
  - Provide immediate fallback text:
    - If `localizationKey == RerollKey`: fallback to `"Throw Again"`.
    - If `localizationKey == KeepKey`: fallback to `"Keep Dice"`.
    - Otherwise fallback to `localizationKey`.
  - Assign fallback text to `text.text`.
  - Update `LocalizeStringEvent`:
    - Ensure `LocalizeStringEvent` component is retained/enabled: `localize.enabled = true`.
    - Call `localize.StringReference.SetReference(LocalizationTable, localizationKey)`.
- Ensure `Start()` completes successfully, reaching `UpdatePieces()` and `IsReady = true`.

### 2. GameInteraction.cs - ReadPointer
- Refactor pointer polling:
  ```csharp
  bool interactionThisFrame = false;
  Vector2 interactionPosition = Vector2.zero;

  if (Touchscreen.current != null &&
      Touchscreen.current.touches.Count > 0 &&
      Touchscreen.current.touches[0].press.wasPressedThisFrame)
  {
      interactionThisFrame = true;
      interactionPosition = Touchscreen.current.touches[0].position.ReadValue();
  }
  else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
  {
      interactionThisFrame = true;
      interactionPosition = Mouse.current.position.ReadValue();
  }
  ```
- Ensure raycasting and piece/target selection proceed identically whether triggered by touch or mouse.

## L2 - Implementation Guidance & Verification Commands

### Verification Commands

- `dotnet test Tools/RulesTests/RulesTests.csproj`
- Static verification that synchronous `GetLocalizedString` is eliminated from `GameInteraction.cs`:
  ```bash
  ! grep -F "LocalizationSettings.StringDatabase.GetLocalizedString" Sahkku/Assets/Scripts/GameInteraction.cs
  ```
- Static verification that mouse fallback is not blocked by idle touch:
  ```bash
  grep -A 10 "ReadPointer" Sahkku/Assets/Scripts/GameInteraction.cs
  ```
