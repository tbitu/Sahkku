---
artifact_type: task
artifact_id: task_sahkku_3_playwright_e2e_game_verification_v1
task_family_id: playwright-e2e-game-verification
sequence_key: "3"
task_id: 3-playwright-e2e-game-verification
title: "Establish Playwright E2E Game Verification Test Suite"
status: implementable
phase: phase1
target_files:
  - "Tools/PlaywrightTests/package.json"
  - "Tools/PlaywrightTests/playwright.config.js"
  - "Tools/PlaywrightTests/tests/game.spec.js"
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

# Task: Establish Playwright E2E Game Verification Test Suite

## L0 - Policy

### Goal

Establish a dedicated automated Playwright browser end-to-end (E2E) test harness in `Tools/PlaywrightTests/` to verify WebGL game functionality across cold launch, main menu navigation, options dropdown localization, and in-game match presentation.

### Domain / Invariant Summary

1. **Headless WebGL Browser Execution**:
   - In containerized and Linux environments (such as Distrobox / Ubuntu), headless Chromium must be launched with software WebGL flags (`--enable-unsafe-swiftshader`, `--use-gl=angle`, `--use-angle=swiftshader`, `--no-sandbox`) so that Unity WebGL 2.0 contexts initialize properly without GPU hardware.
2. **Environment & Target URL Configuration**:
   - The test target URL must default to the live deployment `https://samiailab.samas.no/sahkku/`, with an optional environment variable override (e.g. `SAHKKU_WEBGL_URL` or `BASE_URL`) allowing tests to target local build web servers.
3. **Console Error & Exception Interception**:
   - The harness must listen to browser `page.on('console', ...)` and `page.on('pageerror', ...)`.
   - The test suite must assert that zero `WebGLPlayer does not support synchronous Addressable loading` exceptions occur during page load and menu navigation.
4. **Verification Coverage**:
   - **Menu Initialization**: On cold page load, the `#unity-canvas` loads and the initial view renders without Addressables loading errors.
   - **Options & Localization**: Navigating to options ("Play Solo" or "Play Versus") verifies that piece model dropdowns (Women, Men, King) populate with localized strings rather than the fallback placeholder "Option A".
   - **Match Launch & Interaction Readiness**: Starting a match enters `Game.unity`, renders the board, and progresses without hanging.
5. **Non-Breaking Invariant**:
   - Existing headless C# rules tests (`dotnet test Tools/RulesTests/RulesTests.csproj`) must continue to pass with 0 regressions.
   - The harness must be self-contained in `Tools/PlaywrightTests/` and runnable via standard `npm test`.

## L1 - Architecture & Contract

### 1. Project Setup: `Tools/PlaywrightTests/package.json`
- Define package metadata:
  ```json
  {
    "name": "sahkku-playwright-tests",
    "version": "1.0.0",
    "private": true,
    "scripts": {
      "test": "playwright test"
    },
    "devDependencies": {
      "@playwright/test": "^1.40.0"
    }
  }
  ```

### 2. Configuration: `Tools/PlaywrightTests/playwright.config.js`
- Configure Chromium with required launch arguments for software WebGL in headless mode:
  ```javascript
  // @ts-check
  const { defineConfig, devices } = require('@playwright/test');

  module.exports = defineConfig({
    testDir: './tests',
    timeout: 120000,
    expect: {
      timeout: 30000
    },
    use: {
      baseURL: process.env.SAHKKU_WEBGL_URL || 'https://samiailab.samas.no/sahkku/',
      trace: 'on-first-retry',
      viewport: { width: 1280, height: 720 },
    },
    projects: [
      {
        name: 'chromium',
        use: {
          ...devices['Desktop Chrome'],
          launchOptions: {
            args: [
              '--enable-unsafe-swiftshader',
              '--use-gl=angle',
              '--use-angle=swiftshader',
              '--no-sandbox',
              '--disable-setuid-sandbox'
            ]
          }
        },
      },
    ],
  });
  ```

### 3. Test Suite: `Tools/PlaywrightTests/tests/game.spec.js`
- Implement browser tests verifying:
  1. **Cold boot & Canvas readiness**:
     - Navigate to target URL.
     - Wait for `#unity-canvas` to attach and become visible.
     - Monitor console logs: assert no fatal Addressable or WebGL initialization exceptions.
  2. **Menu navigation & Localized dropdown validation**:
     - Interact with menu elements on the canvas or UI overlay.
     - Validate that options menus display localized strings and no dropdown options remain stuck at "Option A".
  3. **Match start & presentation readiness**:
     - Launch solo game session.
     - Assert canvas responds and match progresses into active state without stalling.

## L2 - Implementation Guidance & Verification Commands

### Verification Commands

- Run existing rules tests:
  ```bash
  dotnet test Tools/RulesTests/RulesTests.csproj
  ```
- Install dependencies:
  ```bash
  cd Tools/PlaywrightTests && npm install
  ```
- Run Playwright E2E tests:
  ```bash
  cd Tools/PlaywrightTests && npx playwright test
  ```
- Ensure zero synchronous Addressables errors logged during runs.
