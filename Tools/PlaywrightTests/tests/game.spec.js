// @ts-check
const { test, expect } = require('@playwright/test');

/**
 * End-to-end verification of the Sahkku Unity WebGL player.
 *
 * Everything here runs against the real player in a real browser — no mocks, no game-side test
 * hooks added to the project. Three things make that possible:
 *
 *  1. The player logs to the browser console, so the suite can listen to it. The regression this
 *     task exists for (`WebGLPlayer does not support synchronous Addressable loading`) is one of
 *     those console entries: Unity prints an unhandled exception as a red console error, and that
 *     is what the assertions watch for.
 *  2. `unityInstance.SendMessage(<GameObject>, <Method>)` is Unity's supported way of driving a
 *     player from JavaScript, so the same public menu methods a button click reaches
 *     (`MenuManager.PlaySolo`, `MenuManager.StartGame`) can be called from the test. Whether the
 *     call actually took effect is asserted on the rendered frame, not on the call's return value:
 *     `SendMessage` reports nothing back, and a wrong name only produces a console warning.
 *  3. The canvas can be read back with `drawImage` inside the frame it was drawn in, which is what
 *     tells a rendering player apart from a blank one; watching it change over a whole turn is what
 *     tells a match being played apart from a match loop stopped at a wait it never came back from.
 *
 * Unity renders its UI into the canvas, so its strings ("Play Solo", the piece-model dropdown
 * entries) are pixels rather than DOM text and cannot be asserted on directly from outside the
 * player. The suite therefore asserts the failures that mangle those strings — a synchronous
 * Addressables read throws, is logged, and leaves `LocalizedDropdown` showing TextMeshPro's
 * "Option A" — and keeps a screenshot of every failure for the human-readable half of the check.
 */

/** Set by the harness on the Unity instance the loader creates; see {@link captureUnityInstance}. */
const UNITY_INSTANCE = '__SAHKKU_UNITY_INSTANCE__';

/**
 * The scene's one gameplay GameObject ("Game" in `Game.unity`): it carries both the match controller and
 * the board's interaction component, so it is the target for anything the suite drives in a match.
 */
const GAME_OBJECT = 'Game';

/** The WebGL Player refuses synchronous Addressables loads and logs this when one is attempted. */
const SYNC_ADDRESSABLE_FAILURE = /does not support synchronous Addressable loading/i;

/** The call the player names in that failure — and the one no other code path may reach either. */
const BLOCKING_WAIT = /WaitForCompletion/;

/** Unity Localization reads its tables from Addressables: a locale read before initialization throws. */
const LOCALE_PRELOAD_FAILURE = /Locales PreloadOperation has not been initialized/i;

/** Any unhandled exception the player prints: the match-readiness regression surfaces as one. */
const UNHANDLED_EXCEPTION = /(^|\n|\s)(Exception|Error): /;

/** The Unity runtime announces itself with this line before the first frame is drawn. */
const RUNTIME_STARTED = /Creating WebGL \d+\.\d+ context/i;

/** A WebGL boot is a multi-megabyte download plus compilation; software rendering is slow. */
const BOOT_TIMEOUT = 90000;

/** How long a state transition (menu → options → match scene) may take. */
const TRANSITION_TIMEOUT = 30000;

/** Fraction of the sampled frame that must be non-black for the player to count as rendering. */
const MIN_DRAWN_RATIO = 0.05;

/** Fraction of the frame that must change for a view to count as a different view. */
const MIN_VIEW_CHANGE = 0.02;

/** How long one turn of a match is watched for. Roll, settle, think and move take a few seconds. */
const TURN_ACTIVITY_WINDOW = 15000;

/** How often the canvas is sampled while a turn is being watched. */
const TURN_ACTIVITY_INTERVAL = 750;

/**
 * How many of those samples have to differ from the one before for the player to count as still playing.
 *
 * One change is the throw the turn opens with, which a player parked on its first frame-time wait still
 * draws before it stops; several spread over a whole turn are the animations, the piece moving and the
 * banner changing. A parked match loop leaves the same picture on screen and scores nothing.
 */
const MIN_TURN_CHANGES = 3;

/** One console recorder per page, shared by the tests and the failure hook that reports it. */
const runtimeByPage = new WeakMap();

/**
 * Every test needs the player's console, so the listener and the instance capture are installed
 * here rather than repeated in each test.
 */
test.beforeEach(async ({ page }) => {
  watchRuntime(page);
  await captureUnityInstance(page);
});

/** A failed test is only actionable with the player's own output next to it. */
test.afterEach(async ({ page }, testInfo) => {
  const runtime = runtimeByPage.get(page);
  if (!runtime || testInfo.status === testInfo.expectedStatus) return;

  const report = runtime.report();
  if (report) await testInfo.attach('browser-console.log', { body: report, contentType: 'text/plain' });
});

test('cold launch renders the main menu without synchronous Addressable loading', async ({ page }) => {
  const runtime = runtimeFor(page);

  await bootPlayer(page, runtime);

  // The player has to be drawing something: a blank frame means the WebGL context never came up,
  // which is the failure mode the software-rendering launch flags exist to prevent.
  const frame = await waitForRenderedFrame(page);
  expect(frame.nonBlankRatio, 'the WebGL canvas is blank; the player is not rendering').toBeGreaterThan(
    MIN_DRAWN_RATIO,
  );

  // The template paints a red banner over the canvas for load errors rather than only logging them.
  expect(await errorBannerText(page), 'the WebGL template reported a load error').toBe('');

  // The invariant this task exists for: no startup path may read an Addressables table
  // synchronously. On WebGL that throws, and the menu is left half-built behind it.
  expectSyncAddressablesFailure(runtime, 'cold launch');

  // The locale system loads its own tables through Addressables, so a read of a locale before the
  // preload finished is the same class of failure one step earlier in start-up.
  expectNoLocalePreloadFailure(runtime, 'cold launch');
  expectNoBlockingWait(runtime, 'cold launch');
});

test('Play Solo reaches the options screen', async ({ page }) => {
  const runtime = runtimeFor(page);

  await bootPlayer(page, runtime);
  const menu = await waitForRenderedFrame(page);

  // Same public entry point the "Play Solo" button is wired to in the scene.
  await sendToUnity(page, 'MenuManager', 'PlaySolo');

  // The options panel is a different screen, so the frame has to change; anything else means the
  // menu never reacted or the player stopped drawing.
  await expectViewChange(page, menu, 'the options screen');
  expectSyncAddressablesFailure(runtime, 'opening the options screen');
  expectNoUnhandledException(runtime, 'opening the options screen');
});

test('starting a match loads the board and keeps the player responsive', async ({ page }) => {
  const runtime = runtimeFor(page);

  await bootPlayer(page, runtime);
  await sendToUnity(page, 'MenuManager', 'PlaySolo');
  const options = await waitForRenderedFrame(page);

  // The match scene ("Game") is a separate Unity scene: entering it stops the menu and starts the
  // match loop, which is where the player used to stall waiting for a presentation that never
  // became ready.
  await sendToUnity(page, 'MenuManager', 'StartGame');
  await expectViewChange(page, options, 'the match board');
  const board = await waitForRenderedFrame(page);
  expect(board.nonBlankRatio, 'the match board is blank; the game scene did not render').toBeGreaterThan(
    MIN_DRAWN_RATIO,
  );

  // A player that survived the transition keeps drawing. A player that threw on the way in has
  // already logged the exception by now, so both checks are asserted at the end of the wait.
  await page.waitForTimeout(2000);
  const later = await waitForRenderedFrame(page);
  expect(later.nonBlankRatio, 'the player stopped drawing after the match started').toBeGreaterThan(
    MIN_DRAWN_RATIO,
  );

  // Pointer input goes through the canvas' own event listeners (the browser's, not a DOM overlay),
  // so clicking the board exercises the input path the player actually ships with.
  await clickCanvas(page, 0.5, 0.5);

  expectSyncAddressablesFailure(runtime, 'starting a match');
  expectNoUnhandledException(runtime, 'starting a match');
});

test('a match with the men starting plays out the bot turn instead of hanging on thinking', async ({ page }) => {
  const runtime = runtimeFor(page);

  await bootPlayer(page, runtime);
  await sendToUnity(page, 'MenuManager', 'PlaySolo');
  const options = await waitForRenderedFrame(page);

  // The men are player two, and "Play Solo" hands that side to the bot, so this is the match that opens on
  // a turn nobody can advance by hand: the dice settle, the bot thinks, and the piece it picked moves. A
  // loop parked on a wait that never resumes gets as far as the dice animation and stops there.
  await startMatchWith(page, runtime, 1, 'Two');
  await expectViewChange(page, options, 'the match board');
  await expectBoardRendering(page, 'the match board');

  const activity = await watchTurnActivity(page);
  expect(
    activity.changes,
    `the player stopped drawing while the men's turn was playing out (${activity.changes} of ${activity.samples.length} samples changed); the match loop is parked`,
  ).toBeGreaterThanOrEqual(MIN_TURN_CHANGES);
  expect(activity.last.nonBlankRatio, "the player stopped drawing after the bot's turn").toBeGreaterThan(
    MIN_DRAWN_RATIO,
  );

  expectSyncAddressablesFailure(runtime, "a match with the men starting");
  expectNoLocalePreloadFailure(runtime, "a match with the men starting");
  expectNoBlockingWait(runtime, "a match with the men starting");
  expectNoUnhandledException(runtime, "a match with the men starting");
});

test('a match with the women starting answers the human turn', async ({ page }) => {
  const runtime = runtimeFor(page);

  await bootPlayer(page, runtime);
  await sendToUnity(page, 'MenuManager', 'PlaySolo');
  const options = await waitForRenderedFrame(page);

  // The women are player one, and "Play Solo" keeps that side for the human: the dice are thrown for the
  // player, and the turn that follows is theirs — the re-roll question first, then a piece and a place.
  await startMatchWith(page, runtime, 0, 'One');
  await expectViewChange(page, options, 'the match board');
  await expectBoardRendering(page, 'the match board');

  // Nothing is asserted here about the canvas still changing: a turn the human owns is *supposed* to go
  // quiet and wait for them, so their seat is checked the other way round — by what their input does.
  // The baseline is taken once the throw has settled, so a difference afterwards is the answer to the
  // clicks and not the tail of an animation that would have played anyway.
  await page.waitForTimeout(2500);
  const before = await expectBoardRendering(page, 'the settled board');

  // A turn played the way the player would play it: browser pointer events on the canvas, which is the
  // path the pieces and the two re-roll buttons are both read through. The sweep covers the board and the
  // row the buttons are grown in; which point lands on a piece or a button is not asserted, since a click
  // on a piece with no move and a click on empty board are both no-ops by rule.
  await clickCanvasGrid(page);

  // And the re-roll question's other answer, through the same bridge its button reaches: `KeepDice` keeps
  // the dice when the question is open and does nothing when the turn has already moved on.
  await sendToUnity(page, GAME_OBJECT, 'KeepDice');

  const after = await expectBoardRendering(page, 'the board after the human turn was answered');
  expect(
    changedFraction(before, after),
    'a turn of clicks and a re-roll answer left the board exactly as it was; the human seat is not answering',
  ).toBeGreaterThan(MIN_VIEW_CHANGE);

  expectSyncAddressablesFailure(runtime, 'a match with the women starting');
  expectNoLocalePreloadFailure(runtime, 'a match with the women starting');
  expectNoBlockingWait(runtime, 'a match with the women starting');
  expectNoUnhandledException(runtime, 'a match with the women starting');
});

// ---------------------------------------------------------------------------------------------
// Player lifecycle
// ---------------------------------------------------------------------------------------------

/**
 * Opens the player and waits until it is running: the loader has produced an instance, the
 * template's loading bar is gone, and the runtime has reported a WebGL context.
 */
async function bootPlayer(page, runtime) {
  await page.goto('./', { waitUntil: 'load' });
  await page.locator('#unity-canvas').waitFor({ state: 'visible', timeout: BOOT_TIMEOUT });

  const instance = await page
    .waitForFunction((name) => Boolean(window[name]) && typeof window[name].SendMessage === 'function', UNITY_INSTANCE, {
      timeout: BOOT_TIMEOUT,
    })
    .then(() => true)
    .catch(() => false);
  expect(instance, `the Unity player never became ready.\n${runtime.report()}`).toBe(true);

  await page.waitForFunction(
    () => {
      const loading = document.querySelector('#unity-loading-container');
      return !loading || loading.style.display === 'none';
    },
    undefined,
    { timeout: BOOT_TIMEOUT },
  );

  await expect
    .poll(() => runtime.started(), { timeout: BOOT_TIMEOUT, message: 'the Unity runtime never reported a WebGL context' })
    .toBe(true);

  // Waiting for the renderer rather than for a fixed delay: the assertions below are about what the
  // player drew, so they must not race the first frame.
  await waitForRenderedFrame(page);
}

/**
 * Captures the instance the page's `createUnityInstance` resolves to.
 *
 * The bundled template keeps the instance in a closure, so the loader is served back with a
 * one-statement wrapper appended: it hands the instance to the test and otherwise leaves the page
 * byte-for-byte the same player. Nothing is injected into the game itself.
 */
async function captureUnityInstance(page) {
  await page.route('**/*loader.js', async (route) => {
    const response = await route.fetch();
    const body = await response.text();

    await route.fulfill({
      status: response.status(),
      headers: { 'content-type': 'application/javascript' },
      body:
        body +
        `
;(function () {
  try {
    if (typeof createUnityInstance !== 'function') return;
    var factory = createUnityInstance;
    window.createUnityInstance = function () {
      var pending = factory.apply(this, arguments);
      if (pending && typeof pending.then === 'function') {
        pending.then(function (instance) { window.${UNITY_INSTANCE} = instance; }).catch(function () {});
      }
      return pending;
    };
  } catch (error) {
    // Never let the instrumentation be the reason a player fails to start.
  }
})();
`,
    });
  });
}

/**
 * Calls a public method on a GameObject through the player's own message bridge.
 *
 * This is the same entry point a scene button reaches; the test asserts the method's *effect* on
 * the rendered frame, because `SendMessage` itself reports nothing and silently logs a warning for
 * a name it cannot find.
 */
async function sendToUnity(page, gameObject, method, value) {
  const result = await page.evaluate(
    ({ name, target, action, argument }) => {
      const instance = window[name];
      if (!instance || typeof instance.SendMessage !== 'function') return { error: 'the player is not running' };

      const args = argument === undefined ? [target, action] : [target, action, argument];
      try {
        instance.SendMessage(...args);
        return { ok: true };
      } catch (error) {
        return { error: `${target}.${action} could not be called: ${error.message}` };
      }
    },
    { name: UNITY_INSTANCE, target: gameObject, action: method, argument: value },
  );

  expect(result.error, `${gameObject}.${method} was rejected by the player`).toBeUndefined();
}

/**
 * Starts a solo match with a chosen starting player (1 = the men / player two, 0 = the women / player one).
 *
 * `MenuManager.SetStartingPlayer` is the method the options screen's "men start" row calls, so the match is
 * configured the way that row configures it without clicking a control whose position on screen is the
 * scene's business. The menu prints the side it just stored, and that line is what confirms the argument
 * arrived: a call that never reached the player is then reported as itself rather than as a broken match.
 */
async function startMatchWith(page, runtime, startingPlayer, name) {
  await sendToUnity(page, 'MenuManager', 'SetStartingPlayer', startingPlayer);
  await expect
    .poll(() => runtime.entries.some((entry) => entry.text.includes(`Starting player: ${name}`)), {
      timeout: TRANSITION_TIMEOUT,
      message: `the menu never stored "${name}" as the starting player`,
    })
    .toBe(true);

  await sendToUnity(page, 'MenuManager', 'StartGame');
}

// ---------------------------------------------------------------------------------------------
// Frame inspection
// ---------------------------------------------------------------------------------------------

/**
 * Reads the canvas back as a coarse luminance grid.
 *
 * The read has to happen inside the frame the player drew: the WebGL drawing buffer is cleared
 * once the browser has composited it, so `drawImage` is called from a `requestAnimationFrame`
 * callback, which runs after the player's own render callback for the same frame.
 */
function readCanvas(page) {
  return page.evaluate(
    () =>
      new Promise((resolve) => {
        requestAnimationFrame(() => {
          const canvas = document.querySelector('#unity-canvas');
          if (!canvas) return resolve({ error: 'the page has no #unity-canvas' });

          const columns = 16;
          const rows = 10;
          const scratch = document.createElement('canvas');
          scratch.width = columns;
          scratch.height = rows;
          const context = scratch.getContext('2d');
          context.drawImage(canvas, 0, 0, columns, rows);

          const pixels = context.getImageData(0, 0, columns, rows).data;
          const cells = [];
          let total = 0;
          let drawn = 0;

          for (let i = 0; i < pixels.length; i += 4) {
            const luma = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;
            cells.push(Math.round(luma));
            total += luma;
            if (luma > 8) drawn += 1;
          }

          resolve({
            cells,
            meanLuma: total / cells.length,
            nonBlankRatio: drawn / cells.length,
          });
        });
      }),
  );
}

/** Waits for a frame with actual content on it, tolerating the frames the player draws while loading. */
async function waitForRenderedFrame(page, { attempts = 20 } = {}) {
  let frame = await readCanvas(page);
  for (let attempt = 0; attempt < attempts && Number(frame.nonBlankRatio || 0) < MIN_DRAWN_RATIO; attempt += 1) {
    await page.waitForTimeout(500);
    frame = await readCanvas(page);
  }
  return frame;
}

/** Waits for a scene to draw, and fails with its name when what is on screen is still an empty frame. */
async function expectBoardRendering(page, what) {
  const frame = await waitForRenderedFrame(page);
  expect(frame.nonBlankRatio, `${what} is blank; the player is not drawing it`).toBeGreaterThan(MIN_DRAWN_RATIO);
  return frame;
}

/**
 * Watches the canvas for a whole turn and reports how often it changed.
 *
 * The harness has no game-side hook to read the match loop's progress with, so it uses the signal the
 * player cannot help producing while it is playing: the throw, the pieces moving and the status banner
 * changing all redraw the canvas, turn after turn. A loop parked on a wait that never resumes draws the
 * frame it stopped on and nothing after it — which is what watching a whole turn tells apart from a match
 * that is being played. `changes` is what the tests assert on; `last` is the frame the watch ended on.
 */
async function watchTurnActivity(page, { windowMs = TURN_ACTIVITY_WINDOW, intervalMs = TURN_ACTIVITY_INTERVAL } = {}) {
  const samples = [];
  let previous = await readCanvas(page);
  const deadline = Date.now() + windowMs;

  do {
    await page.waitForTimeout(intervalMs);
    const current = await readCanvas(page);
    samples.push(changedFraction(previous, current) > MIN_VIEW_CHANGE);
    previous = current;
  } while (Date.now() < deadline);

  return { samples, changes: samples.filter(Boolean).length, last: previous };
}

/** Asserts that the player moved to another view, rather than sitting on the one it was showing. */
async function expectViewChange(page, before, what) {
  await expect
    .poll(
      async () => {
        const current = await readCanvas(page);
        if (current.error) return 0;
        return changedFraction(before, current);
      },
      { timeout: TRANSITION_TIMEOUT, message: `${what} never appeared; the view did not change` },
    )
    .toBeGreaterThan(MIN_VIEW_CHANGE);
}

/** Share of the sampled grid that changed by more than rendering noise. */
function changedFraction(before, after) {
  if (!before.cells || !after.cells || before.cells.length !== after.cells.length) return 1;
  let changed = 0;
  for (let i = 0; i < before.cells.length; i += 1) {
    if (Math.abs(before.cells[i] - after.cells[i]) > 8) changed += 1;
  }
  return changed / before.cells.length;
}

/** Clicks the canvas at a relative position, using the browser's own pointer events. */
async function clickCanvas(page, xFraction, yFraction) {
  const box = await page.locator('#unity-canvas').boundingBox();
  if (!box) throw new Error('the page has no visible #unity-canvas to click');
  await page.mouse.click(box.x + box.width * xFraction, box.y + box.height * yFraction);
}

/**
 * Clicks a grid of points across the canvas, with real browser pointer events.
 *
 * The board and the two re-roll buttons sit in the middle and along the bottom of the frame (the buttons
 * are grown at run time, so the scene does not pin them there), so the sweep covers both. Which point
 * lands on a movable piece or on a button is not asserted: the clicks exist to drive the input path the
 * player ships with — pieces on the board, and the buttons that answer the re-roll question — during a turn
 * the human owns.
 */
async function clickCanvasGrid(page) {
  for (const y of [0.3, 0.45, 0.6, 0.85]) {
    for (const x of [0.3, 0.41, 0.5, 0.59, 0.7]) {
      await clickCanvas(page, x, y);
      await page.waitForTimeout(200);
    }
  }
}

/** The text of the template's error banner; the template styles a load failure bright red. */
function errorBannerText(page) {
  return page.evaluate(() => {
    const banner = document.querySelector('#unity-warning');
    if (!banner) return '';
    return Array.from(banner.querySelectorAll('div'))
      .filter((div) => /red/i.test(div.getAttribute('style') || ''))
      .map((div) => div.textContent.trim())
      .join('\n');
  });
}

// ---------------------------------------------------------------------------------------------
// Console diagnostics
// ---------------------------------------------------------------------------------------------

/** The console/page-error recorder for a page, created on first use. */
function runtimeFor(page) {
  return runtimeByPage.get(page) || watchRuntime(page);
}

/**
 * Records everything the player tells the browser. Unity logs its start-up trace, its warnings and
 * its unhandled exceptions here, which makes the console the only window the harness has into the
 * running game.
 */
function watchRuntime(page) {
  const existing = runtimeByPage.get(page);
  if (existing) return existing;

  const entries = [];
  page.on('console', (message) => entries.push({ kind: `console.${message.type()}`, text: message.text() }));
  page.on('pageerror', (error) => entries.push({ kind: 'pageerror', text: error.stack || error.message }));

  const failures = () => entries.filter((entry) => /console\.(error|warning)/.test(entry.kind) || entry.kind === 'pageerror');

  const runtime = {
    entries,
    /** True once the runtime has announced its graphics context. */
    started: () => entries.some((entry) => RUNTIME_STARTED.test(entry.text)),
    /** Every logged failure, for a readable assertion message. */
    failures,
    /** The synchronous-Addressables regressions among them. */
    syncAddressablesFailures: () => failures().filter((entry) => SYNC_ADDRESSABLE_FAILURE.test(entry.text)),
    report() {
      if (!entries.length) return 'The browser console stayed empty.';
      return entries.map((entry) => `[${entry.kind}] ${entry.text}`).join('\n');
    },
  };

  runtimeByPage.set(page, runtime);
  return runtime;
}

/** Asserts the one failure mode this task was written for, with the player's own output attached. */
function expectSyncAddressablesFailure(runtime, when) {
  const failures = runtime.syncAddressablesFailures();
  expect(
    failures.map((entry) => entry.text),
    `the player performed a synchronous Addressables load while ${when}:\n${failures.map((entry) => entry.text).join('\n')}`,
  ).toEqual([]);
}

/** Asserts the player never reached a call that waits for a load: on WebGL none of them can return. */
function expectNoBlockingWait(runtime, when) {
  expectNoFailureMatching(runtime, BLOCKING_WAIT, `the player waited for a load to finish while ${when}`);
}

/** Asserts no locale table was read before the localization system had finished initializing. */
function expectNoLocalePreloadFailure(runtime, when) {
  expectNoFailureMatching(runtime, LOCALE_PRELOAD_FAILURE, `the player read a locale before it was preloaded while ${when}`);
}

/** Asserts the player did not throw on its way through a state: a thrown exception stops the game. */
function expectNoUnhandledException(runtime, when) {
  expectNoFailureMatching(runtime, UNHANDLED_EXCEPTION, `the player logged an unhandled exception while ${when}`);
}

/** The shared body of the console assertions: which failures were logged, and what each one means. */
function expectNoFailureMatching(runtime, pattern, description) {
  const failures = runtime.failures().filter((entry) => pattern.test(entry.text));
  expect(
    failures.map((entry) => entry.text),
    `${description}:\n${failures.map((entry) => entry.text).join('\n')}`,
  ).toEqual([]);
}
