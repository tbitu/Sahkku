---
artifact_type: plan
artifact_id: plan_sahkku_start_screen_and_gameplay_fix_v1
plan_id: sahkku-start-screen-and-gameplay-fix
created_on: "2026-09-22"
title: "Fix WebGL Menu Initialization, Input Pointer Handling, and Playwright E2E Verification"
status: in_progress
plan_status: in_progress
prd_ref: null
owners:
  - "tarjeib"
task_order:
  - 1-webgl-localization-and-menu-init
  - 2-pointer-input-and-match-readiness
  - 3-playwright-e2e-game-verification
active_task_id: 3-playwright-e2e-game-verification
last_completed_task_id: 2-pointer-input-and-match-readiness
archive_group: 2026-09-22-sahkku-start-screen-and-gameplay-fix
task_tracker:
  - task_id: 1-webgl-localization-and-menu-init
    task_path: plans/archive/tasks/2026-09-22-sahkku-start-screen-and-gameplay-fix/1-webgl-localization-and-menu-init.md
    status: archived
    notes: "Fix WebGL Addressables synchronous localization loading crashes in MenuManager and LocalizedDropdown; set MainMenuPanel active by default in MainMenu.unity"
  - task_id: 2-pointer-input-and-match-readiness
    task_path: plans/archive/tasks/2026-09-22-sahkku-start-screen-and-gameplay-fix/2-pointer-input-and-match-readiness.md
    status: archived
    notes: "Fix GameInteraction pointer input shadowing between touch and mouse, eliminate synchronous localization crash in SetupRerollButtons, and ensure match loop readiness"
  - task_id: 3-playwright-e2e-game-verification
    task_path: null
    status: not_created
    notes: "Establish dedicated Playwright E2E test harness in Tools/PlaywrightTests/ to verify start menu, options, match start, re-roll, and move execution against WebGL"
---

# Plan: Fix WebGL Menu Initialization, Input Pointer Handling, and Playwright E2E Verification

## Objective

Resolve the broken WebGL game startup and unplayable match state deployed at `https://samiailab.samas.no/sahkku/`, and establish a real Playwright end-to-end (E2E) automated test harness to verify actual game functionality in the browser.

Specifically:
1. Fix the mangled start screen where the game boots directly into a partially generated `OptionsPanel` displaying unlocalized dropdowns ("Option A") and missing the main menu ("Play Solo", "Play Versus", "Rules", "Quit").
2. Ensure the main menu scene behaves consistently on cold launch and when returning from a match via the quit button.
3. Fix the unplayable game state in `Game.unity` caused by unhandled WebGL Addressable localization exceptions in `GameInteraction.Start()`, match readiness stalls in `GameLogic.WaitForPresentationAsync()`, and mouse input suppression in `GameInteraction.ReadPointer()`.
4. Implement an automated Playwright test suite in `Tools/PlaywrightTests/` capable of driving the WebGL build, intercepting console errors/exceptions, and asserting on interactive game states.

## Done Definition

1. Loading the WebGL build on a cold browser cache displays the primary `MainMenuPanel` ("Play Solo", "Play Versus", "Rules", "Language", "Audio", "Fullscreen") without throwing `WebGLPlayer does not support synchronous Addressable loading` exceptions.
2. Clicking "Play Solo" or "Play Versus" transitions cleanly to `OptionsPanel`, where all piece model dropdowns (Women, Men, King) populate with localized strings (e.g., "Wood", "Bone") rather than the fallback placeholder "Option A", and all grown rows ("Throw for start", "Men start", "LLM Opponent", "Endpoint", "Model") render with correct labels.
3. Starting a match enters `Game.unity` cleanly, registers `IsReady = true`, presents re-roll options when Sáhkku (X) is thrown, and allows players to select pieces and destinations via both mouse and touch input.
4. Clicking the quit button ("X") in `Game.unity` returns to `MainMenu.unity` with identical visual and functional state as cold launch.
5. A runnable Playwright E2E suite in `Tools/PlaywrightTests/` executes headlessly, connects to the WebGL game instance, and verifies:
   - Initial main menu render with no WebGL runtime exceptions.
   - Transition to options and verification of localized dropdown contents.
   - Match launch, dice throw presentation, and interaction readiness.

## Capability Closure

| Capability Claim | Closure Classification | Activation Path | Repo-Provided Boundary | External Prerequisites | Last-Mile Proof |
|---|---|---|---|---|---|
| WebGL Menu & Startup Reliability | end_to_end | Browser navigation to WebGL index page | `MenuManager.cs`, `LocalizedDropdown.cs`, `MainMenu.unity` | WebGL-compatible browser | Verified via Playwright headless browser check |
| Mouse and Touch Gameplay Interaction | end_to_end | Pointer/touch input on `#unity-canvas` in `Game.unity` | `GameInteraction.cs`, `GameLogic.cs`, `Game.unity` | None | Verified via Playwright canvas clicks and piece selection |
| Playwright E2E Test Suite | end_to_end | `npm test` inside `Tools/PlaywrightTests/` | Playwright test scripts, config, package.json | Node.js + Playwright browser | Automated test run reporting pass |

## Guiding Principles

1. **Business Invariant**: The Sáhkku digital tabletop must remain immediately playable and fully accessible on both desktop browsers (mouse input) and touch devices (touchscreen input), honoring Sámi craft visuals and localized UI text across Finnish, Sámi, and English.
2. **Control Model**:
   - Scene hierarchy default visibility: The root `MainMenuPanel` is the authoritative default active view in `MainMenu.unity`; `OptionsPanel` is opened only upon explicit user intent ("Play Solo" or "Play Versus").
   - Localization readiness: In Unity WebGL, Addressables and Localization tables load asynchronously. UI scripts must NEVER invoke synchronous `StringDatabase.GetLocalizedString(...)` or `WaitForCompletion` on startup. They must await `LocalizationSettings.InitializationOperation` or subscribe to `SelectedLocaleChanged` and async string events, with safe static fallbacks.
   - Pointer Input: `ReadPointer()` must evaluate both `Touchscreen` and `Mouse` devices without allowing a non-null but idle `Touchscreen.current` to shadow or discard active `Mouse.current` clicks.
3. **Read-Path Rule**:
   - Menu text reads from `UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase` asynchronously or via `LocalizeStringEvent` components, falling back to English defaults if tables are pending.
   - Match state reads exclusively from `RulesEngine` and `GameState`.
4. **Forbidden Fallback**:
   - Never call `AsyncOperationHandle.WaitForCompletion()` or synchronous `.GetLocalizedString()` during `Awake()` or `Start()` in WebGL builds.
   - Never assume `Touchscreen.current != null` implies user interaction is exclusively touch-based.
5. **Allowed Resolution Path**:
   - If localization is still loading at frame 0, render authored UI keys or English fallbacks and update dynamically when `LocalizationSettings.InitializationOperation` finishes.
6. **Missing-Data Rule**:
   - If a localization key or table is unavailable, display the key or safe default string rather than failing fast or aborting subsequent UI setup.
7. **Sequencing / Boundary Note**:
   - **Task 1 (Menu & Localization Foundation)**: Must fix scene hierarchy and asynchronous localization handling in `MainMenu.unity` and `MenuManager.cs` first.
   - **Task 2 (Gameplay & Input Delivery)**: Resolves `GameInteraction.cs` input shadowing, reroll button setup, and match readiness loop.
   - **Task 3 (Playwright E2E Verification)**: Assembles and runs the browser E2E test harness against the build.

## Current Status

### Completed Work

- 139 headless rules tests passing (`dotnet test Tools/RulesTests/RulesTests.csproj`).
- Comprehensive diagnosis completed with Playwright headless session against live deployment `https://samiailab.samas.no/sahkku/`.
- Root cause confirmed:
  1. `MainMenu.unity` authored with `OptionsPanel` active and `MainMenuPanel` inactive.
  2. `MenuManager.Start()` crashes on synchronous `LocalizationSettings.StringDatabase.GetLocalizedString(...)` due to WebGL Addressables `WaitForCompletion` prohibition, leaving `MainMenuPanel` unactivated and dropdowns stuck at "Option A".
  3. `GameInteraction.Start()` crashes on same call in `SetupRerollButtons()`, leaving `IsReady = false` and causing `GameLogic.WaitForPresentationAsync()` to hang indefinitely.
  4. `GameInteraction.ReadPointer()` ignores mouse input because `Touchscreen.current != null` blocks the `else if (Mouse.current != null)` branch.

### Open Work

- [ ] Task 1: Fix WebGL Localization & Menu Startup lifecycle.
- [ ] Task 2: Fix Pointer Input & Match Controller readiness.
- [ ] Task 3: Build automated Playwright E2E verification test suite.

### Deferred / Future Work

- High-polygon mesh optimization for `Soldier_bone_1` to eliminate the PhysX 256-polygon convex hull warning in WebGL.
- Depth-of-field and FSR shader stripping cleanup in URP WebGL graphics settings.

## Open Task List

| Task ID | Task Path | Purpose | Depends On | Closes Gap | Status |
|---|---|---|---|---|---|
| `1-webgl-localization-and-menu-init` | plans/archive/tasks/2026-09-22-sahkku-start-screen-and-gameplay-fix/1-webgl-localization-and-menu-init.md | Fix async localization loading in `MenuManager` and `LocalizedDropdown`, and set `MainMenuPanel` active by default in `MainMenu.unity` | None | Start screen crash, "Option A" dropdowns, scene state discrepancy | archived |
| `2-pointer-input-and-match-readiness` | plans/archive/tasks/2026-09-22-sahkku-start-screen-and-gameplay-fix/2-pointer-input-and-match-readiness.md | Fix `ReadPointer` mouse/touch coexistence, prevent `SetupRerollButtons` crash, and ensure match loop readiness | `1-webgl-localization-and-menu-init` | Unplayable game, piece selection freeze, missing reroll buttons | archived |
| `3-playwright-e2e-game-verification` | null | Implement Playwright E2E test harness in `Tools/PlaywrightTests/` to automate browser verification of menus and gameplay | `2-pointer-input-and-match-readiness` | Automated E2E verification of live/built game function | not_created |

## Coverage Map

| Plan Gap | Closed By | Notes |
|---|---|---|
| Mangled start screen showing "Option A" and nothing else | `1-webgl-localization-and-menu-init` | Ensures `MainMenuPanel` is active by default and prevents startup crash from synchronous localization |
| Quit screen differing from initial start screen | `1-webgl-localization-and-menu-init` | Normalizes state across cold launch and subsequent scene loads |
| Game unplayable in `Game.unity` | `2-pointer-input-and-match-readiness` | Ensures `IsReady` is reached, reroll buttons are created, and mouse clicks register |
| Automated browser verification of game function | `3-playwright-e2e-game-verification` | Provides Playwright test suite to verify UI, canvas interaction, and error-free execution |

## Dependencies and Order

1. `1-webgl-localization-and-menu-init` must precede `2-pointer-input-and-match-readiness` so that menu navigation to options and game match startup can be cleanly reached with valid settings.
2. `2-pointer-input-and-match-readiness` must precede `3-playwright-e2e-game-verification` so that Playwright tests can assert on functional piece selection and move progression.

## Risks and Assumptions

1. **Unity Asset Serialization**: Modifying YAML scene files (`MainMenu.unity`) requires exact GUID and fileID preservation.
2. **WebGL Asynchrony**: Unity Localization on WebGL loads Addressables bundles via HTTP range requests. Async handlers must gracefully tolerate frame delays.
3. **Headless WebGL Rendering**: Playwright running in Docker/Distrobox requires software WebGL rendering (`--enable-unsafe-swiftshader`, `--use-gl=angle`, `--use-angle=swiftshader`) to initialize WebGL 2.0 contexts.

## Validation Strategy

1. **Compilation**: `dotnet build Tools/RulesTests/RulesTests.csproj` and `dotnet test Tools/RulesTests/RulesTests.csproj` must continue passing with 100% success.
2. **Playwright Diagnostic Run**: Execute `node Tools/PlaywrightTests/tests/game.spec.js` asserting:
   - Console contains zero `WebGLPlayer does not support synchronous Addressable loading` error messages.
   - `#unity-canvas` renders `MainMenuPanel` on startup with "Play Solo" visible.
   - Clicking options renders populated dropdowns without "Option A".
   - Clicking "Play" initializes the board, animates dice, and accepts piece selection.
