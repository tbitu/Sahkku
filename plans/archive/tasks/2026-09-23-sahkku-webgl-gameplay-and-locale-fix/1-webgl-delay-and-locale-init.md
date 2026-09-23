---
artifact_type: task
artifact_id: task_1_webgl_delay_and_locale_init_v1
task_family_id: webgl-delay-and-locale-init
sequence_key: "1"
task_id: 1-webgl-delay-and-locale-init
status: archived
phase: implementation
target_files:
  - Sahkku/Assets/Scripts/GameLogic.cs
  - Sahkku/Assets/Localization/Scripts/LocaleSwithcer.cs
  - Sahkku/Assets/Scripts/RulesBridge/LlmPlayerAgent.cs
prd_ref: null
plan_ref: plans/2026-09-23-sahkku-webgl-gameplay-and-locale-fix-plan.md
system_context_ref: null
doc_bubble_id: null
impl_bubble_id: 1-webgl-delay-and-locale-init
supersedes: []
superseded_by: null
archive_group: 2026-09-23-sahkku-webgl-gameplay-and-locale-fix
---

# Task 1: Fix WebGL Match Loop Pacing (Task.Delay), LocaleSwitcher Preload, and Bot Timeout Hardening

## L0: Context & Scope

### Problem Summary
1. `GameLogic.cs` uses `System.Threading.Tasks.Task.Delay` in `DelayAsync` and `TimedOutAsync`. In Unity WebGL builds, WebAssembly runs single-threaded and lacks a background .NET ThreadPool timer mechanism. As a consequence, `await Task.Delay(...)` continuations are never invoked on WebGL, deadlocking the match loop during the initial dice settle (`diceSettleSeconds`). When Men (Player 2 / Bot) start, the banner shows `"Player_2_Roll — Thinking..."` forever. When Women start, the same deadlock occurs before `PlayMovePhaseAsync`, leaving `pendingMove == null` and dropping all pointer clicks.
2. `LocaleSwitcher.cs` in `Start()` synchronously reads `LocalizationSettings.AvailableLocales.Locales` on frame 0 before `LocalizationSettings.InitializationOperation` finishes, causing Unity Localization to fall back to a synchronous Addressables `WaitForCompletion()` call that throws an unhandled exception in WebGL.
3. `HttpClientLlmTransport` uses standard .NET `HttpClient` which is unsupported in WebGL without custom JS websockets/fetch bridges. If LLM bot is selected, an unhandled network exception can abort the match loop unless cleanly handled and fallen back to heuristic play.

### Scope Reality Proof
- **Inspected Entrypoints**:
  - `GameLogic.DelayAsync`: lines 409-417.
  - `GameLogic.TimedOutAsync`: lines 391-396.
  - `GameLogic.StartMatch`: lines 177-230.
  - `LocaleSwitcher.Start`: lines 8-12.
  - `LocaleSwitcher.Next` / `Previous`: lines 14-26.
  - `LlmPlayerAgent.DecideMoveAsync` / `DecideRerollAsync`.
- **Bounded Slice**:
  - Replace `Task.Delay` in `GameLogic.cs` with a WebGL-safe frame-time delay helper (using `Task.Yield()` while `Time.time < targetTime`, or a custom coroutine-backed/Awaitable mechanism that ticks on Unity's main thread).
  - Implement a WebGL-safe `TimedOutAsync` that polls time across yields rather than awaiting `Task.Delay`.
  - Update `LocaleSwitcher.cs` to run an asynchronous initialization coroutine that awaits `LocalizationSettings.InitializationOperation` before querying `AvailableLocales.Locales`.
  - Ensure any LLM transport exception in `GameLogic.CreateLlmAgent` or agent calls is caught and falls back immediately to heuristic agent without terminating `StartMatch()`.

### Scoped Invariants
- `applies_to`: `GameLogic.cs`, `LocaleSwitcher.cs`, `LlmPlayerAgent.cs`.
- `does_not_apply_to`: Board piece mesh models, rules evaluation logic in `RulesEngine.cs`.
- `proof_surface`: Headless rules tests pass, Playwright tests pass cold launch without synchronous Addressables errors, and match transitions past dice settling.

---

## L1: Implementation Contract

### 1. Functional Requirements

- **REQ_DELAY_WEBGL**: `GameLogic.DelayAsync(float seconds, CancellationToken cancellationToken)` must pace asynchronously by yielding to the Unity main loop (`await Task.Yield()`) until the elapsed Unity `Time.time` (or `Time.unscaledTime`) meets the delay, checking `cancellationToken.IsCancellationRequested` on each tick. Under no circumstances may `Task.Delay` be invoked on WebGL.
- **REQ_TIMEOUT_WEBGL**: `GameLogic.TimedOutAsync(Task pending, CancellationToken cancellationToken)` must evaluate timeout using Unity frame time ticks (`Time.time`) and `Task.Yield()` rather than `Task.Delay` and `Task.WhenAny(pending, Task.Delay(...))`.
- **REQ_LOCALE_ASYNC**: `LocaleSwitcher.cs` must not access `LocalizationSettings.AvailableLocales.Locales` synchronously in `Start()`. It must start a coroutine or async method waiting for `LocalizationSettings.InitializationOperation`, and only after completion determine `currentIndex`. If `Next()` or `Previous()` is called before initialization finishes, it must safely no-op or guard against `NullReferenceException`.
- **REQ_LLM_WEBGL_GUARD**: If `LlmBot` fails to initialize or make HTTP requests on WebGL, `GameLogic.CreateAgent` and `RequestMoveAsync` / `RequestRerollAsync` must catch the exception and substitute `HeuristicPlayerAgent` cleanly.

### 2. Call-Site Matrix

| Target File | Method | Change Description |
|---|---|---|
| `Sahkku/Assets/Scripts/GameLogic.cs` | `DelayAsync` | Replace `Task.Delay` with a loop yielding `Task.Yield()` until `Time.time >= finishTime`. |
| `Sahkku/Assets/Scripts/GameLogic.cs` | `TimedOutAsync` | Replace `Task.WhenAny` on `Task.Delay` with frame-yield polling until either `pending.IsCompleted` or timeout elapsed. |
| `Sahkku/Assets/Localization/Scripts/LocaleSwithcer.cs` | `Start` | Replace synchronous locale lookup with a coroutine waiting for `LocalizationSettings.InitializationOperation`. |
| `Sahkku/Assets/Localization/Scripts/LocaleSwithcer.cs` | `Next` / `Previous` | Guard with null/readiness checks so rapid clicks before initialization never throw. |

### 3. Error and Fallback Handling

- If `cancellationToken.IsCancellationRequested` fires during `DelayAsync` or `TimedOutAsync`, throw `OperationCanceledException` to unwind `StartMatch` cleanly.
- If `LocalizationSettings.AvailableLocales` is null or empty, default `currentIndex = 0` safely.

### 4. Test Matrix

| Test ID | Input / Scenario | Expected Outcome |
|---|---|---|
| `T1_RULES_REGRESSION` | `dotnet test Tools/RulesTests/RulesTests.csproj` | All 139 headless rules tests pass. |
| `T1_COLD_LAUNCH_LOCALE` | Playwright cold launch on WebGL player | Zero `Locales PreloadOperation` errors, zero `WaitForCompletion` exceptions. |
| `T1_DELAY_PACING` | Match start in `Game.unity` | Pacing proceeds past dice settle without deadlock on WebGL. |

---

## L2: Hardening Backlog

- Add automated unit test for `UnityTimeDelays` cancellation responsiveness.
- Consider migrating from `TaskCompletionSource` to UniTask or Unity 6 `Awaitable` when full engine update is scheduled.
