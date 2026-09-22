---
artifact_type: task
artifact_id: task_sahkku_1_webgl_localization_and_menu_init_v1
task_family_id: webgl-localization-and-menu-init
sequence_key: "1"
task_id: 1-webgl-localization-and-menu-init
title: "Fix WebGL Addressables Localization and Author Scene Menu Hierarchy"
status: archived
phase: phase1
target_files:
  - "Sahkku/Assets/Scenes/MainMenu.unity"
  - "Sahkku/Assets/Localization/Scripts/MenuManager.cs"
  - "Sahkku/Assets/Localization/Scripts/LocalizedDropdown.cs"
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

# Task: Fix WebGL Addressables Localization and Author Scene Menu Hierarchy

## L0 - Policy

### Goal

Eliminate startup exceptions in Unity WebGL builds caused by synchronous Addressable localization loading (`WaitForCompletion`), ensure the main menu displays the primary action panel (`MainMenuPanel`) on boot and when returning from a match, and prevent piece model dropdowns from getting stuck on TextMeshPro's default "Option A".

### Domain / Invariant Summary

1. **Scene Hierarchy Default Visibility**:
   - In [`Sahkku/Assets/Scenes/MainMenu.unity`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scenes/MainMenu.unity), GameObject `MainMenuPanel` (`fileID: 1528551882`) must be active (`m_IsActive: 1`).
   - GameObject `OptionsPanel` (`fileID: 1134353240`) must be inactive (`m_IsActive: 0`).
2. **WebGL Addressables Asynchrony**:
   - Unity WebGL does not support synchronous Addressable loading (`WaitForCompletion` throws a fatal exception).
   - Scripts must never invoke synchronous `GetLocalizedString()` during `Awake()`, `Start()`, or `OnEnable()`.
   - `MenuManager.CopyLabel()` must not disable `LocalizeStringEvent` or call synchronous `GetLocalizedString()`. It must set readable fallback text immediately and configure `LocalizeStringEvent.StringReference` so Unity Localization resolves strings asynchronously and on language changes.
3. **Dropdown Robustness**:
   - In [`LocalizedDropdown.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Localization/Scripts/LocalizedDropdown.cs), populate options immediately with model name fallbacks ("Wood", "Bone") before any async operation begins so dropdowns never show "Option A".
   - Refresh options asynchronously using `GetLocalizedStringAsync()` or coroutines, awaiting `LocalizationSettings.InitializationOperation`.
4. **Non-Breaking Invariant**:
   - All YAML serialization headers and GUIDs in `MainMenu.unity` must be preserved.
   - Headless test suite (`dotnet test Tools/RulesTests/RulesTests.csproj`) must pass with 0 regressions.

## L1 - Architecture & Contract

### 1. MainMenu.unity Hierarchy
- Set `MainMenuPanel` (`fileID: 1528551882`): `m_IsActive: 1`.
- Set `OptionsPanel` (`fileID: 1134353240`): `m_IsActive: 0`.

### 2. MenuManager.cs
- In `CopyLabel()`:
  - Assign immediate fallback text to `TextMeshProUGUI.text` (e.g. "Throw for start", "Men start", "LLM Opponent", "Endpoint", "Model").
  - Wire `LocalizeStringEvent.StringReference.SetReference("UI_Text", localizationKey)` and keep `localize.enabled = true`.
  - Remove all synchronous `StringDatabase.GetLocalizedString()` calls.
- In `Start()`:
  - Ensure `AddStartOptions()` and `ShowMainMenu()` execute safely without throwing.

### 3. LocalizedDropdown.cs
- In `OnEnable()`:
  - Populate readable fallback options synchronously (`Wood`, `Bone`) so dropdown is never uninitialized.
  - Start coroutine to await `LocalizationSettings.InitializationOperation` and retrieve localized option labels asynchronously via `GetLocalizedStringAsync()`.
  - Listen to `LocalizationSettings.SelectedLocaleChanged` to re-trigger async refresh when language changes.
- In `OnDisable()`:
  - Cancel any running refresh coroutine and unsubscribe from events.

## L2 - Implementation Guidance & Verification Commands

### Verification Commands

- `dotnet test Tools/RulesTests/RulesTests.csproj`
- Validate YAML integrity:
  ```bash
  python3 -c "
  with open('Sahkku/Assets/Scenes/MainMenu.unity') as f:
      content = f.read()
  assert 'm_IsActive: 1' in content
  "
  ```
