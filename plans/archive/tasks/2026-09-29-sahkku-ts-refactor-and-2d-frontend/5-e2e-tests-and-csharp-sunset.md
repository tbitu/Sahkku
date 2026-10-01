---
artifact_type: task
artifact_id: task_5_e2e_tests_and_csharp_sunset_v1
task_family_id: e2e-tests-and-csharp-sunset
sequence_key: "5"
task_id: 5-e2e-tests-and-csharp-sunset
title: "Playwright E2E Verification, Pairflow CI Alignment, and Legacy C#/Unity Sunset"
status: archived
phase: implementation
target_files:
  - Tools/PlaywrightTests/playwright.config.js
  - Tools/PlaywrightTests/tests/game.spec.js
  - package.json
  - pairflow.toml
  - README.md
prd_ref: null
plan_ref: plans/2026-09-29-sahkku-ts-refactor-and-2d-frontend-plan.md
system_context_ref: null
owners:
  - "tarjeib"
doc_bubble_id: null
impl_bubble_id: 5-e2e-tests-and-csharp-sunset
supersedes: []
superseded_by: null
archive_group: 2026-09-29-sahkku-ts-refactor-and-2d-frontend
---

# Task 5: Playwright E2E Verification, Pairflow CI Alignment, and Legacy C#/Unity Sunset

## L0: Context & Scope

### Problem Summary
With the TypeScript rules engine (`src/rules/`), agents & CLI bench (`src/agents/`, `src/cli/`), 2D web board game interface (`src/ui/`), and game modes/settings/audio/localization (`src/audio/`, `src/locale/`) fully implemented and unit-tested, the project needs final end-to-end integration and repository modernization:
1. **Automated Playwright E2E Browser Testing**:
   - Update `Tools/PlaywrightTests` to run headlessly against the Vite web application.
   - Assert cold launch cleanliness (zero unhandled exceptions, DOM rendered), interactive piece selection and destination cell highlighting, dice rolling, human vs bot gameplay loop, settings modal storage, and multi-language switching.
2. **Pairflow CI & Validation Alignment**:
   - Update `pairflow.toml` validation commands (`build`, `test`, `bootstrap`) from .NET commands to standard npm commands (`npm run build`, `npm test`, `npm install`), removing the .NET SDK prerequisite for Pairflow bubble lifecycle validation.
3. **Legacy C# & Unity Sunset**:
   - Retire legacy C# folders (`Sahkku/`, `Tools/RulesTests/`, `Tools/SahkkuBench/`), eliminating heavy Unity 3D models, textures, scenes, and .NET dependencies while ensuring the repository remains 100% open-license and lightweight.
4. **Documentation**:
   - Update `README.md` to document the new TypeScript architecture, rules verification (`npm test`), headless benchmarking (`npm run bench`), local web server (`npm run dev`), and open-source license attribution.

### Scope Reality Proof
- **Inspected Files**:
  - `Tools/PlaywrightTests/playwright.config.js`: Currently configured for legacy Unity WebGL build server.
  - `pairflow.toml`: References `dotnet build` and `dotnet test`.
  - `Sahkku/`, `Tools/RulesTests/`, `Tools/SahkkuBench/`: Legacy .NET and Unity assets.
  - `README.md`: Describes Unity 6 and C# architecture.
- **Bounded Slice**:
  - Wire Playwright test script into root `package.json` (`"test:e2e"`).
  - Update `Tools/PlaywrightTests/playwright.config.js` and `game.spec.js` to target the Vite 2D app webServer (`npm run preview` or `npm run dev`).
  - Update `pairflow.toml` validation configuration.
  - Remove legacy C# and Unity projects (`Sahkku/`, `Tools/RulesTests/`, `Tools/SahkkuBench/`).
  - Rewrite `README.md` with complete TypeScript instructions.

### Scoped Invariants
- `applies_to`: `Tools/PlaywrightTests/*`, `package.json`, `pairflow.toml`, `README.md`, legacy C# deletion.
- `does_not_apply_to`: Pure rules engine algorithms in `src/rules/` (already complete).
- `proof_surface`: `npm test` passes 236 unit tests; `npm run build` succeeds; Playwright E2E tests pass headlessly; `pairflow.toml` validates cleanly.

---

## L1: Implementation Contract

### 1. Functional Requirements

- **REQ_PLAYWRIGHT_VITE**: In `Tools/PlaywrightTests/playwright.config.js`:
  - Configure `webServer` to launch Vite preview/dev on local port (e.g. `http://localhost:5173` or `4173`).
  - Set `baseURL` accordingly.
- **REQ_E2E_TESTS**: In `Tools/PlaywrightTests/tests/game.spec.js`:
  - Test 1 (Cold Launch): Page loads, title contains "Sáhkku", board renders 3 rows x 15 cells, 3 dice render, zero console errors.
  - Test 2 (Hotseat Interaction): Select piece, verify destination cell highlights, roll dice, execute move, assert turn transition.
  - Test 3 (AI Bot Game): Start single-player match against Heuristic bot, make move, assert bot responds with move and banner updates.
  - Test 4 (Language Switcher): Switch locale to North Sámi (`se`) and Norwegian (`no`), asserting DOM strings update reactively.
  - Test 5 (Settings Modal): Open settings dialog, change LLM URL/model and volume, save, reload page, assert values persisted in `localStorage`.
- **REQ_PAIRFLOW_COMMANDS**: In `pairflow.toml`:
  - `build = "npm run build"`
  - `test = "npm test"`
  - `bootstrap = "npm install"`
- **REQ_CSHARP_SUNSET**:
  - Remove directory `Sahkku/` (Unity project, C# rules engine, scenes, 3D models).
  - Remove directory `Tools/RulesTests/` (.NET NUnit test project).
  - Remove directory `Tools/SahkkuBench/` (.NET console app).
  - Keep `Tools/PlaywrightTests/`.
- **REQ_README_MODERNIZATION**: In `README.md`:
  - Document the modern TypeScript architecture (`src/rules/`, `src/agents/`, `src/cli/`, `src/ui/`, `src/audio/`, `src/locale/`).
  - Document quick start commands: `npm install`, `npm run dev`, `npm test`, `npm run bench`.
  - Maintain attribution: traditional game of the Sámi people under CC BY-NC-SA 4.0 / open license.

### 2. Call-Site Matrix

| Target File | Change Description |
|---|---|
| `Tools/PlaywrightTests/playwright.config.js` | Configure webServer and baseURL for Vite client. |
| `Tools/PlaywrightTests/tests/game.spec.js` | Implement end-to-end tests for 2D web client. |
| `package.json` | Add `"test:e2e"` script pointing to Playwright. |
| `pairflow.toml` | Update `validation.commands` to npm commands. |
| `README.md` | Rewrite repository documentation. |
| `Sahkku/`, `Tools/RulesTests/`, `Tools/SahkkuBench/` | Remove legacy C# and Unity directories. |

### 3. Error and Fallback Handling

- Playwright tests gracefully wait for elements with auto-retrying locators without flaky arbitrary sleeps.
- All 236 Vitest unit tests continue to run and pass.

### 4. Test Matrix

| Test ID | Input / Scenario | Expected Outcome |
|---|---|---|
| `T5_E2E_COLD_LAUNCH` | Playwright cold launch test | Zero console errors, board rendered. |
| `T5_E2E_GAMEPLAY` | Playwright interactive moves | Legal move applied, turn advances. |
| `T5_E2E_SETTINGS_LOCALE` | Playwright locale & settings test | String tables switch reactively; settings persist. |
| `T5_BUILD_AND_TEST` | `npm run build` && `npm test` | Clean build, 236 passing unit tests. |

---

## L2: Hardening Backlog

- Add GitHub Actions CI workflow to run `npm test` and `npm run test:e2e` on pull requests.
- Add GitHub Pages automated deployment workflow for the static Vite dist bundle.
