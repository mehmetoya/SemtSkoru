import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  retries: 0,
  use: {
    baseURL: "http://localhost:3000",
    trace: "on-first-retry",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    {
      // Real backend, real Postgres (docker-compose) -- this is an end-to-end test of the
      // actual system, not a mocked one. Requires `docker compose up` to already be running.
      command: "dotnet run --project src/SemtSkoru.Api --urls http://localhost:5169",
      cwd: "../backend",
      url: "http://localhost:5169/api/neighborhoods",
      reuseExistingServer: true,
      timeout: 60_000,
    },
    {
      command: "npm run dev",
      url: "http://localhost:3000",
      reuseExistingServer: true,
      timeout: 60_000,
    },
  ],
});
