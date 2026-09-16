import { fileURLToPath } from "node:url";
import { configDefaults, defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tsconfigPaths from "vite-tsconfig-paths";

export default defineConfig({
  plugins: [tsconfigPaths(), react()],
  resolve: {
    alias: {
      // Outside of Next's own build, `next-intl/config` has no bundler magic pointing it
      // at our i18n/request.ts (that's what next-intl/plugin's webpack/turbopack alias
      // does for `next build`/`next dev` - see next.config.ts). Vite/Vitest needs the same
      // alias made explicit, or every getTranslations()/getRequestConfig() call throws
      // "Couldn't find next-intl config file" in tests.
      "next-intl/config": fileURLToPath(new URL("./i18n/request.ts", import.meta.url)),
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./vitest.setup.ts"],
    // e2e/ holds Playwright specs (`npm run test:e2e`), a different test runner entirely.
    exclude: [...configDefaults.exclude, "e2e/**"],
    // next-intl ships ESM-only and expects to be processed by the bundler rather than
    // required as-is - per next-intl's own testing docs
    // (https://next-intl.dev/docs/environments/testing), inlining it avoids Vitest's
    // default dependency externalization tripping over that.
    server: { deps: { inline: ["next-intl", "use-intl"] } },
  },
});
