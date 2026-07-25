import { defineConfig, devices } from '@playwright/test';

/**
 * Layout-regression audit (see e2e/layout-audit.spec.ts).
 *
 * Deliberately narrow in scope: this is not a functional end-to-end suite, it
 * renders every route and asserts the page is not visually broken in ways a
 * build cannot detect. It exists because a whole session's worth of UI shipped
 * "compiling cleanly" with amputated table columns and one-word-per-line labels.
 *
 * Expects a dev server on 3000 and the API on 7457, both started by the
 * workflow. `RADIOPAD_REQUIRE_AUTH` must stay unset so AuthGate lets the audit
 * through to the real pages instead of bouncing every route to /login.
 */
export default defineConfig({
  testDir: './e2e',
  // Layout is deterministic; a retry would only mask a real flake.
  retries: 0,
  // These runs are pure rendering — parallelism is safe and keeps CI short.
  workers: process.env.CI ? 2 : undefined,
  reporter: process.env.CI ? [['github'], ['list']] : [['list']],
  timeout: 60_000,
  use: {
    baseURL: process.env.RADIOPAD_WEB_BASE ?? 'http://127.0.0.1:3000',
    // The audit reads geometry, so a screenshot is the useful failure artefact.
    screenshot: 'only-on-failure',
    trace: 'off',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
});
