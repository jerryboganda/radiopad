# HANDOFF — Workflow streamlining (Iter-58 → Iter-65)

**Written:** 2026-11-21, **updated** 2026-11-22 · **Branch:** `main` · **State:** commits local, **nothing pushed, nothing released**

Read this first, then `PROGRESS.md` Iter-58 … Iter-65 for the per-change detail.

## 1. What the owner asked for

"Streamline the whole workflow, make it very easy to use, optimise UI/UX, leave nothing behind."
Decisions the owner made by popup (do not re-ask):

| Question | Answer |
| --- | --- |
| Starting a report | One screen, dictate-first (no wizard) |
| Navigation | Consolidate hard — short sidebar |
| Order of work | Desktop reporting flow → desktop nav/home/settings → web admin → mobile |
| Workspace layout | **Option A — three-pane cockpit** (canvas: https://claude.ai/artifact/GewBeASRRzgsEAVMZfLHFj) |
| Backend testing | **Frontend only, fake data** (owner declined installing .NET and declined a draft PR for CI) |
| Click-through depth | **Every page, every control** |

The owner's last instruction: *click through every option one by one, keep spotting and fixing
errors, make sure everything suits the workflow and is easily understood.* **Done in Iter-63**
(2026-11-22) — see §5 for what it found and what is still open.

## 2. Unpushed commits (oldest first)

```
8fd3dd7 feat(frontend): one-screen report intake, queue-first editor rail, short sidebar
2b5ff92 feat(frontend): timed autosave, next case, settings hub, first-run checklists, admin nav
bb33fdc feat(frontend): in-app confirm dialog, dictation language setting, theme token fixes
9cb89df feat(frontend): skip-to-content link in the app shell
397875c feat: invite email on user creation; intake and editor polish after visual check
c0f0f33 feat(frontend): worklist defaults to open cases, one-row filters, numbered checklist
```

Working tree is clean except `frontend/next-env.d.ts`, which `next dev` rewrites — always
`git checkout -- frontend/next-env.d.ts` before committing.

**Do not push without the owner saying so.** They were asked five times and answered "keep
building" each time. A push to `main` redeploys the live web admin (`hostinger-deploy.yml`) and the
API (`web-deploy-images.yml`); the desktop release needs `pnpm release:desktop` afterwards (DESK-001).

## 3. What is built

| Area | Change | Key files |
| --- | --- | --- |
| Report start | 4-step wizard → one screen; AI model is a remembered default beside a sticky Generate row | `frontend/app/(desktop)/reports/new/NewReportWizard.tsx`, `.rp-wizard-*` in `radiopad.css` |
| Editor | Case queue leads the left rail; study/template/rulebook card folded in `<details>`; lifecycle steps under the "Report Composer" heading; autosave every 20 s (never overwrites typing); "Next case" after Sign & Send | `components/reports/StudyContextPanel.tsx`, `reports/[id]/ReportClient.tsx`, `reportShared.tsx` (`ReportLifecycle`), `SignAndSendButton.tsx` |
| Sidebar | 5 fixed items + folded **Library / Quality / Settings** groups (native `<details>`); Composer removed from nav | `components/shell/nav.config.tsx`, `Sidebar.tsx`, `shell.css` |
| Worklist | Default filter "Open cases" (hides Exported); filters in one row; New report button | `app/(desktop)/worklist/page.tsx`, `.rp-filter-bar` |
| Settings | `/settings` lists every personal settings screen; dictation language select | `app/(desktop)/settings/page.tsx`, `lib/dictation/speech.ts` |
| Onboarding | `GettingStarted` checklist on dashboard and web `/admin/users` | `components/ui/GettingStarted.tsx` |
| Confirmations | All 15 `confirm()` calls → `await confirmDialog()` (native `<dialog>`, falls back to `window.confirm` in jsdom) | `lib/confirmDialog.ts`, `.rp-confirm` |
| Web admin nav | SSO, MCP tools, validation packs now in the sidebar | `nav.config.tsx`, 6 locale files |
| Accessibility | Skip-to-content link | `components/shell/AppShell.tsx` |
| Theme | Hardcoded colours → tokens on editor badge, mobile update check, companion sandbox | — |
| Backend | `UsersController.Create` sends a best-effort invite email (never the password); returns `inviteSent` | `backend/.../Controllers/Iter31Controllers.cs` |

## 4. Verification status — be honest about this

| Check | Status |
| --- | --- |
| Targeted Vitest files for every changed area | Pass locally |
| Full frontend suite, typecheck (`tsc`), lint | **Not run** — CI only (project rule) |
| Backend compile + tests (invite email) | **Not run — cannot run here**, see §6 |
| Visual, light + dark, mock data | All 65 routes screenshotted in both themes (Iter-63); editor re-checked at 1280 / 1366 / 1920 px |
| Every in-page control clicked, mock data | All 65 routes (Iter-63); export/download/print/sign-out skipped |
| Reporting flow, mock data | Worklist → validate → acknowledge → sign as primary, and Send → Next case, walked |
| Real data, sign, export, generate, dictation, autosave against a real server | **Never exercised** |

## 5. The click-through (Iter-63, done)

All 65 routes loaded in both themes and every in-page control clicked by `docs/dev-tools/route-sweep.js`
(headless Playwright, separate from the owner's browser pane). The mock was then filled in from
`frontend/lib/api.ts` until the remaining crashes were real. The reporting flow was walked end to end:
worklist → open case → edit → validate → acknowledge (in-app confirm) → review & sign → sign as primary →
co-signer / addendum offered. Fixes are listed in `PROGRESS.md` Iter-63: Escape on every modal, "No access"
vs "Sign in required", the editor fitting 1280–1440 px laptops, and id-less dead ends.

Follow-up checks (Iter-64): Send → Next case, every export format, sign-out and the mobile companion at
375 px were exercised, which turned up and fixed sign-off acting on stale server text, a stale report
staying on screen after "Next case", and "Next case" ignoring STAT. See `PROGRESS.md` Iter-64.

RIS copy and windows below 1180 px were checked in Iter-65 (clipboard never cleared on Windows; report
pushed below the fold at 1024 px; both fixed).

Still open:

- **Real backend**: everything above ran against mock data, never the real API.
- **Rust change uncompiled**: `secure_copy` clean-up in `desktop/src-tauri/src/main.rs` (Iter-65); CI builds it.

## 6. How to view the app on this machine

The dev machine has **no `dotnet`**, and `desktop/src-tauri/binaries/radiopad-api-*.exe` are 11-byte
placeholders, so the real backend cannot run locally.

1. Start the mock API on `127.0.0.1:7457`: `node docs/dev-tools/mock-api.js` (stateful: validate,
   acknowledge and sign change the report; it logs unmodelled endpoints to `%TEMP%`).
2. Start the frontend with the preview tool, config **`frontend-dev`** in the *outer*
   `D:\Projects\RadioPad\.claude\launch.json`. It must use `next dev --webpack` — Turbopack panics
   ("Next.js package not found") when launched from the outer folder and the page reload-loops on
   "Loading…".
3. `next dev` proxies `/api/*` to port 7457. No login is needed (`RADIOPAD_REQUIRE_AUTH` unset).
4. Flip theme: `document.documentElement.setAttribute('data-theme','dark')`.
5. When done: stop the preview server, kill the node process on 7457, restore `frontend/next-env.d.ts`.

At handoff time both servers may still be running from the interrupted session (ports 3000 and 7457).

## 7. Known open items

- **Email invite wording** — sent on the first real user creation after deploy; owner has not
  reviewed the text (`TrySendInviteAsync`).
- **Backend change is uncompiled.** If CI fails on `Iter31Controllers.cs`, that is the first place to look.
- **"Next case" picker** (`SignAndSendButton.nextCase`) has no unit test.
- **Dictation language** applies to the browser speech engine only; on-device engines are unchanged.
- **Critical results** was kept top-level in the sidebar although the mockup folded it away — owner has not objected or confirmed.
- **`/governance`** (web) is an unlinked audit summary, distinct from `/admin/governance`; left alone.
- **Stale docs** not touched: `docs/02-design/information-architecture.md`, `ux-strategy.md`,
  `docs/_reports/open-questions.md`, `docs/ui-ux-audit/*` (dated 2025-01), `PRD.md` MVP checklist.
  They contradict PRD v3.0 and the shipped app; trust PRD v3.0 + `PROGRESS.md`.

## 8. Rules that bit during this work

- Heavy work (full builds, full suites, typecheck/lint sweeps) runs on GitHub Actions only — one
  targeted Vitest file at a time locally.
- Both themes are mandatory for every UI change; tokens only, no hardcoded colours.
- Routes live in surface route groups (`app/(desktop|web|mobile|shared)/`); `crossSurfaceLinks.test.ts`
  fails if a page links across surfaces.
- `sidebar.test.tsx` pins every nav route to its group — update it with any nav change.
- Shell heredocs mangle backslashes in generated C#/JS; write patch scripts with the file-writing
  tool and use raw strings.
- The owner wants questions as popups, design options as artifacts before a build, and every change
  recorded in `PROGRESS.md`.

## Appendix — dev tools

`docs/dev-tools/mock-api.js` (mock API) and `docs/dev-tools/route-sweep.js` (route sweep) live in the repo
so they survive temp-folder clean-ups. Neither is imported by the app nor built into any surface.
Run the sweep from PowerShell, since Git Bash rewrites `/route` arguments into Windows paths.
