---
artifact_type: plan
artifact_id: plan_sahkku_webgl_gameplay_and_locale_fix_v1
plan_id: sahkku-webgl-gameplay-and-locale-fix
created_on: "2026-09-23"
title: "Fix WebGL Match Loop Pacing (Task.Delay), LocaleSwitcher Preload, and Board Interaction"
status: in_progress
plan_status: in_progress
prd_ref: null
owners:
  - "tarjeib"
task_order:
  - 1-webgl-delay-and-locale-init
  - 2-interaction-and-gameplay-flow
active_task_id: 2-interaction-and-gameplay-flow
last_completed_task_id: 1-webgl-delay-and-locale-init
archive_group: 2026-09-23-sahkku-webgl-gameplay-and-locale-fix
task_tracker:
  - task_id: 1-webgl-delay-and-locale-init
    task_path: plans/tasks/1-webgl-delay-and-locale-init.md
    status: done
    notes: "Replace unsupported Task.Delay in GameLogic with WebGL-compatible frame-time pacing, fix LocaleSwitcher frame-0 synchronous locale loading, and harden bot reply timeouts"
  - task_id: 2-interaction-and-gameplay-flow
    task_path: plans/tasks/2-interaction-and-gameplay-flow.md
    status: not_created
    notes: "Fix board destination place raycasting/layer-masking, add turn-pass/reroll UI clarity, and verify with automated Playwright browser tests"
---

# Plan: Fix WebGL Match Loop Pacing, LocaleSwitcher Preload, and Board Interaction

## Objective

Resolve the two critical gameplay freeze bugs on the Unity WebGL player deployed at `https://samiailab.samas.no/sahkku/`:
1. **"Thinking..." when men start hangs forever**: `GameLogic.cs` awaits `Task.Delay(...)` for dice settling and bot thinking. Because Unity WebGL runs single-threaded WebAssembly without background thread pool timer pumps, `Task.Delay` continuations never resume on WebGL, causing the entire match loop to freeze on frame 0.
2. **When women start (human player), no pieces can be interacted with**: Because the match loop hangs on that exact same initial `Task.Delay`, `GameLogic.StartMatch` never reaches `PlayMovePhaseAsync`, leaving `pendingMove` permanently `null`. In `GameInteraction.ReadPointer()`, `pendingMove == null` immediately drops all piece interaction. Furthermore, `LocaleSwitcher.Start()` triggers a synchronous Addressables `WaitForCompletion` exception on cold launch by reading `AvailableLocales.Locales` before `LocalizationSettings.InitializationOperation` completes.

## Done Definition

1. **Cold Launch Localization Cleanliness**: Loading the WebGL game never logs `Locales PreloadOperation has not been initialized` or `WebGLPlayer does not support synchronous Addressable loading`. `LocaleSwitcher` safely awaits initialization before querying locales.
2. **WebGL Match Loop Continuity**:
   - Starting a match with Men (Player 2 / Bot) starts immediately: dice animate, dice settle, bot thinking executes with WebGL-safe timing, and moves or reroll decisions complete without freezing on "Thinking...".
   - Starting a match with Women (Player 1 / Human) transitions cleanly into reroll or move phase: `pendingMove` is populated, selectable pieces respond to mouse and touch clicks, and destination cells register clicks to execute moves.
3. **Automated Verification**:
   - `dotnet test Tools/RulesTests/RulesTests.csproj` passes with 100% success (zero rules regressions).
   - Playwright E2E tests in `Tools/PlaywrightTests/` pass headlessly against the WebGL build without unhandled exceptions or hangs.

## Capability Closure

| Capability Claim | Closure Classification | Activation Path | Repo-Provided Boundary | External Prerequisites | Last-Mile Proof |
|---|---|---|---|---|---|
| WebGL Match Pacing & Continuations | end_to_end | `GameLogic.StartMatch` in `Game.unity` | `GameLogic.cs`, `UnityTimeDelays.cs` | None | WebGL match loop advances past dice settle and bot think |
| Asynchronous Locale Switcher | end_to_end | Scene load of `MainMenu.unity` | `LocaleSwitcher.cs` | None | Zero `WaitForCompletion` or locale preload errors in console |
| Canvas Board & Piece Interaction | end_to_end | Pointer/mouse click on `#unity-canvas` in `Game.unity` | `GameInteraction.cs` | None | Click on selectable piece highlights cells; click on cell commits move |
| Playwright E2E Validation | end_to_end | `npm test` in `Tools/PlaywrightTests/` | `game.spec.js` | Chromium | Tests for cold launch and gameplay pass green |

## Guiding Principles

1. **Business Invariant**: The digital Sáhkku tabletop must run fluidly in web browsers without locking the UI, freezing on bot turns, or dropping human input on interactive pieces.
2. **Control Model**:
   - Pacing in WebGL: Single-threaded WebGL must NEVER use `System.Threading.Tasks.Task.Delay`. All asynchronous delays must yield to Unity's main thread loop via frame time (`Time.time` / `Time.deltaTime`), Unity coroutines, or `Awaitable.WaitForSecondsAsync`.
   - Locale initialization: `LocalizationSettings.AvailableLocales` must only be queried after `LocalizationSettings.InitializationOperation` has completed.
   - Match state authority: `RulesEngine` remains the sole authority on legal moves, piece activation, and turn progression.
3. **Read-Path Rule**:
   - Input coordinates read from `Mouse.current` / `Touchscreen.current` mapped through `Camera.ScreenPointToRay`.
   - Piece selectability reads from `piece.allowedPlaces.Count > 0` populated by `RulesEngine.EvaluateAllowedPlaces`.
4. **Forbidden Fallback**:
   - Never call `Task.Delay` in Unity WebGL game code.
   - Never query `LocalizationSettings.AvailableLocales` before `LocalizationSettings.InitializationOperation.IsDone`.
5. **Allowed Resolution Path**:
   - If an agent (bot or network LLM) times out or throws, `GameLogic` falls back immediately to `HeuristicPlayerAgent.ChooseMove` / `ChooseReroll`.

## Sequencing and Tasks

1. **Task 1: `1-webgl-delay-and-locale-init`**:
   - Replace `Task.Delay` in `GameLogic.cs` with WebGL-compatible frame-time pacing.
   - Refactor `TimedOutAsync` in `GameLogic.cs` to avoid `Task.Delay`.
   - Fix `LocaleSwitcher.cs` to await `LocalizationSettings.InitializationOperation` before indexing locales.
   - Guard `HttpClientLlmTransport` so it fails gracefully to heuristic bot on WebGL if socket transports are unavailable.
2. **Task 2: `2-interaction-and-gameplay-flow`**:
   - Audit and fix `GameInteraction.ReadPointer()` destination cell (`Place`) hit testing and layer mask interactions.
   - Ensure turn-pass and reroll requirements give clear feedback when pieces cannot yet move.
   - Expand `Tools/PlaywrightTests/tests/game.spec.js` to assert bot move completion (no infinite "Thinking...") and human piece selection in the browser.

## Validation Strategy

1. `dotnet test Tools/RulesTests/RulesTests.csproj` passes.
2. Playwright E2E suite executes cleanly with zero unhandled exceptions and verified state changes.
