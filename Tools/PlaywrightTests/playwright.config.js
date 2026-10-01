// @ts-check
const path = require('node:path');
const { defineConfig, devices } = require('@playwright/test');

/**
 * Playwright configuration for the 2D web client.
 *
 * The harness drives the real client in a real browser — no mocks and no test hooks inside the app —
 * so the only required input is a URL. By default it starts the Vite preview server itself:
 *
 *   npm --prefix Tools/PlaywrightTests install     # once: the harness keeps its own dependencies
 *   npx --prefix Tools/PlaywrightTests playwright install chromium
 *   npm run test:e2e                               # builds the client and tests the bundle
 *
 * Pointing the suite at an already running client skips the local server entirely — that is the
 * supported way to reuse one, and it is explicit rather than assumed:
 *
 *   SAHKKU_E2E_URL=http://localhost:4173 npm run test:e2e
 *   SAHKKU_E2E_URL=https://samiailab.samas.no/sahkku/ npm run test:e2e
 *
 * `SAHKKU_E2E_URL` wins; `BASE_URL` is accepted as an alias because that is the name most CI systems
 * already export.
 */

/** The Vite production build, served by `vite preview`. */
const LOCAL_BASE_URL = 'http://localhost:4173';

/** The repository root, i.e. where the client's own `package.json` and Vite config live. */
const repoRoot = path.resolve(__dirname, '..', '..');

const deployedBaseUrl = process.env.SAHKKU_E2E_URL || process.env.BASE_URL || null;
const baseURL = deployedBaseUrl || LOCAL_BASE_URL;

module.exports = defineConfig({
  testDir: './tests',
  // Each test gets its own browser context with its own `localStorage`, and nothing is shared between
  // them, so they can run in parallel. The suite is pure DOM work — no GPU, no WebGL player to boot.
  fullyParallel: true,
  // A retry keeps a loaded machine from failing the run; `trace: 'on-first-retry'` only records a trace
  // when a retry is configured, so the first attempt stays cheap.
  retries: process.env.CI ? 1 : 0,
  timeout: 60000,
  expect: {
    // Every assertion is an auto-retrying DOM condition: no test sleeps waiting for the client.
    timeout: 15000,
  },
  reporter: [['list']],
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    viewport: { width: 1280, height: 720 },
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        launchOptions: {
          // Containers and CI runners that run the browser as root have no usable setuid sandbox, and
          // a test browser does not need one.
          args: ['--no-sandbox', '--disable-setuid-sandbox'],
        },
      },
    },
  ],
  // Built and served locally unless the caller named a deployed client: the suite then verifies the
  // very bundle `npm run build` ships. Serving takes a moment, so building happens first.
  //
  // `reuseExistingServer` stays off even locally. Playwright only starts `command` when it is not
  // reusing, so a reused server on 4173 is whatever was built earlier — the suite would report on a
  // stale bundle while claiming to have built one. With reuse off, an occupied port fails loudly
  // instead; a client the caller started deliberately is still reached by naming it in
  // `SAHKKU_E2E_URL` (see above), which skips the web server altogether.
  webServer: deployedBaseUrl
    ? undefined
    : {
        command: 'npm run build && npm run preview -- --port 4173 --strictPort',
        cwd: repoRoot,
        url: LOCAL_BASE_URL,
        reuseExistingServer: false,
        timeout: 120000,
        stdout: 'ignore',
        stderr: 'pipe',
      },
});
