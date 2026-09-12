import "@testing-library/jest-dom/vitest";
import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";

// vitest.config.mts doesn't set test.globals, so `afterEach` isn't a global -
// @testing-library/react's own auto-cleanup side effect silently no-ops without it,
// meaning every component test file was leaking its rendered DOM into the next test.
// Only surfaced now because two different tests happened to render the same text ("—").
afterEach(() => {
  cleanup();
});
