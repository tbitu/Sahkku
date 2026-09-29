import { defineConfig } from "vitest/config";

/**
 * The rules core is engine-agnostic and dependency-free, so the suite runs in plain Node with no
 * DOM shim: the same modules run in the browser, in a web worker and in the headless CLI.
 */
export default defineConfig({
  test: {
    include: ["tests/**/*.test.ts"],
    environment: "node",
    globals: false,
    reporters: ["default"],
  },
});
