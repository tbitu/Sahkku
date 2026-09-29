/**
 * Vite configuration for the 2D web client.
 *
 * The client is deliberately dependency-light: the only source of game rules is the engine-agnostic
 * TypeScript core in `src/rules/`, which Vite bundles straight from source (no 3D runtime, no
 * WebAssembly, no proprietary asset pipeline). `index.html` is the single entry document and
 * `src/main.ts` the single script entry.
 *
 * The test runner keeps its own `vitest.config.ts`, which Vitest prefers over this file, so the
 * Node-only rules/agents/CLI suites keep running without a DOM.
 */

import { defineConfig } from "vite";

export default defineConfig({
  // Static, single-page bundle: everything the browser needs ends up in `dist/`.
  build: {
    outDir: "dist",
    emptyOutDir: true,
    target: "es2022",
    sourcemap: true,
  },
  server: {
    port: 5173,
    open: false,
  },
  preview: {
    port: 4173,
  },
});
