import { defineConfig, devices } from '@playwright/test';

/**
 * Functional end-to-end reporting flow (e2e-flow/), run by
 * .github/workflows/e2e-reporting-flow.yml against the real API on a throwaway
 * database. Kept apart from the layout audit (playwright.config.ts / e2e/) so
 * neither suite runs the other's specs.
 */
export default defineConfig({
  testDir: './e2e-flow',
  retries: 0,
  workers: 1,
  reporter: process.env.CI ? [['github'], ['list'], ['html', { open: 'never', outputFolder: 'playwright-flow-report' }]] : [['list']],
  // First visits compile each route in `next dev`.
  timeout: 240_000,
  expect: { timeout: 15_000 },
  use: {
    baseURL: process.env.RADIOPAD_WEB_BASE ?? 'http://127.0.0.1:3000',
    acceptDownloads: true,
    screenshot: 'on',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
