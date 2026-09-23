// @ts-check
const { defineConfig, devices } = require('@playwright/test');

/**
 * Playwright configuration for the Sahkku Unity WebGL build.
 *
 * The harness drives the deployed (or locally served) player, so the only required input is a
 * reachable URL:
 *
 *   SAHKKU_WEBGL_URL=http://localhost:8000/ npm test     # local build served from a static server
 *   npm test                                             # live deployment
 *
 * `SAHKKU_WEBGL_URL` wins; `BASE_URL` is accepted as an alias because that is the name most CI
 * systems already export.
 */
module.exports = defineConfig({
  testDir: './tests',
  // The tests boot a multi-megabyte WebGL player each and are CPU bound (software WebGL), so they
  // are run one at a time rather than in parallel: concurrent instances starve each other and
  // turn frame-rate observations into flakes.
  fullyParallel: false,
  workers: 1,
  // A boot that goes wrong is worth exactly one retry: `trace: 'on-first-retry'` only records a
  // trace when a retry is configured.
  retries: 1,
  timeout: 120000,
  expect: {
    timeout: 30000,
  },
  reporter: [['list']],
  use: {
    baseURL: process.env.SAHKKU_WEBGL_URL || process.env.BASE_URL || 'https://samiailab.samas.no/sahkku/',
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
          // Containers and Linux desktops without a GPU (Distrobox, CI runners) have no hardware
          // WebGL, and Unity refuses to start without a context. SwiftShader through ANGLE gives
          // the player a software WebGL 2.0 context; --no-sandbox is required when the browser
          // runs as root inside a container.
          args: [
            '--enable-unsafe-swiftshader',
            '--use-gl=angle',
            '--use-angle=swiftshader',
            '--no-sandbox',
            '--disable-setuid-sandbox',
          ],
        },
      },
    },
  ],
});
