# HANDOFF — Workflow streamlining (Iter-58 → Iter-62)

**Written:** 2026-11-21 · **Branch:** `main` · **State:** 6 commits local, **nothing pushed, nothing released**

Read this first, then `PROGRESS.md` Iter-58 … Iter-62 for the per-change detail.

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
errors, make sure everything suits the workflow and is easily understood.* That sweep was
**interrupted by the owner as it started — it has produced no results yet.** It is the next job (§5).

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
| Visual, light + dark, mock data | Done for: dashboard, sidebar, `/reports/new`, report editor, `/settings`, confirm dialog styling, worklist, reports list, templates, critical results, web Users, mobile Reporting + Companion |
| Real data, sign, export, generate, dictation, autosave against a real server | **Never exercised** |
| The other ~50 pages | **Not looked at** |

## 5. Next job: the full click-through (owner-approved scope)

Frontend only, fake data, **every page, every control**, both themes. Plan that was about to run:

1. **Load sweep** — load all 65 routes, record crashes, error text, console errors, blank pages.
2. **Make the mock realistic** where a page rejects its data. The mock returns `[]` for unknown
   endpoints, so messages like "Cannot read properties of undefined" are usually the mock's
   fault, not a product bug. Look up the real response type in `frontend/lib/api.ts` and add it to
   the mock before judging. A page that *white-screens* on odd data is a real robustness bug; a page
   that shows its `ErrorState` is behaving correctly.
3. **Click sweep** — on each page click every button, tab, `<summary>`, checkbox and change every
   select; record exceptions. Skip export/download/print/sign-out in the automated pass and test
   those by hand. Cancel any `dialog.rp-confirm` that opens.
4. **Walk the reporting flow by hand**: worklist → open case → edit → validate → acknowledge →
   sign & send → next case; new report → generate; settings.
5. Fix what is found, re-run the touched Vitest files, append an Iter-63 entry to `PROGRESS.md`, commit locally.

Routes (65):

```
/ /account/security /admin/billing /admin/dictation-settings /admin/feature-flags /admin/fhir-import
/admin/governance /admin/mcp /admin/model-eval /admin/pacs /admin/providers/oauth /admin/security
/admin/settings /admin/sso /admin/ubag /admin/usage /admin/users /admin/validation-packs /ai-assistant
/analytics /analytics/quality /audit /audit/verify /body-parts /companion /critical-results /dashboard
/design/motion /device-pairing /findings-library /governance /login /marketplace /modalities
/notifications /offline /pair /peer-review /prompts /protocols /providers /quality /register
/report-templates /report-templates/designer /reporting /reporting/dictate /reports /reports/compose
/reports/new /reports/view /rulebooks /rulebooks/editor /rulebooks/view /settings /settings/corrections
/settings/hotkeys /settings/models /settings/snippets /teaching /teaching/view /templates /terminology
/validation /worklist
```

`/reports/view`, `/rulebooks/view`, `/rulebooks/editor`, `/teaching/view`,
`/report-templates/designer`, `/admin/providers/oauth` take `?id=…`.

Tip for the load sweep: instead of 65 full navigations, load routes in hidden same-origin
`<iframe>`s from one page (5–6 at a time, ~20 s wait for webpack to compile) and read
`iframe.contentDocument.querySelector('main').innerText`. Keep each script run short — a single
long-running browser script is what the owner interrupted.

## 6. How to view the app on this machine

The dev machine has **no `dotnet`**, and `desktop/src-tauri/binaries/radiopad-api-*.exe` are 11-byte
placeholders, so the real backend cannot run locally.

1. Start the mock API (appendix A) on `127.0.0.1:7457`: `node <path>/mockapi.js`.
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

## Appendix A — throwaway mock API (keep out of the repo's shipped code)

```js
// Throwaway mock of the RadioPad API, only to look at screens without a backend.
const http = require('http');
const perms = ['reports.read', 'reports.draft', 'reports.edit', 'reports.validate', 'reports.sign', 'reports.export', 'rulebooks.read', 'templates.read', 'providers.read', 'audit.read', 'audit.verify', 'users.read', 'users.manage', 'billing.read', 'security.manage', 'tenant_settings.manage', 'validation_packs.read', 'mcp_tools.manage', 'prompt_overrides.manage', 'modalities.read', 'body_parts.read', 'critical_results.read', 'teaching_cases.read', 'peer_review.read'];
const now = Date.now();
const rep = (id, mod, part, status, mins, ind) => ({
  id, status, updatedAt: new Date(now - mins * 60000).toISOString(), createdAt: new Date(now - mins * 60000).toISOString(),
  indication: ind, technique: '', comparison: '', findings: id === 'r1' ? '8 mm solid nodule in the right upper lobe, unchanged.' : '', impression: '', recommendations: '',
  aiHighlightsJson: '{}', templateId: null, rulebookId: null, templatePinned: false, rulebookPinned: false,
  study: { accessionNumber: 'ACC-' + id.toUpperCase(), modality: mod, bodyPart: part, contrast: 'With', age: 58, ageUnit: 'Years', gender: 'Male', comparison: '' },
});
const reports = [rep('r1', 'CT', 'Chest', 'Draft', 3, 'Persistent cough. STAT.'), rep('r2', 'MR', 'Brain', 'Draft', 18, 'Headache'), rep('r3', 'XR', 'Chest', 'Validated', 50, 'Routine'), rep('r4', 'US', 'Abdomen', 'Exported', 300, 'Pain')];
const cat = (codes) => codes.map((c, i) => ({ id: 'c' + i, code: c, name: c, active: true, sortOrder: i }));
function body(url) {
  const p = url.split('?')[0].replace(/\/$/, '');
  if (p === '/api/tenant/me') return { tenant: { slug: 'dev', displayName: 'Dev Hospital' }, user: { email: 'radiologist@radiopad.local', role: 5, roleName: 'MedicalDirector', permissions: perms } };
  if (p === '/api/reports/stats') return { total: 4, draft: 2, validated: 1, acknowledged: 0, exported: 1, trend: { total: [1, 2, 3, 4], validated: [0, 1, 1, 1], acknowledged: [0, 0, 0, 0], exported: [0, 0, 1, 1] } };
  if (p === '/api/reports') return reports; // paged calls read the array + X-Total-Count header
  const m = p.match(/^\/api\/reports\/(r\d)$/);
  if (m) return reports.find((r) => r.id === m[1]);
  if (p === '/api/modalities') return cat(['CT', 'MR', 'XR', 'US']);
  if (p === '/api/body-parts') return cat(['Chest', 'Brain', 'Abdomen']);
  if (p === '/api/providers') return [{ id: 'p1', name: 'On-device formatter', adapter: 'llama-cpp', model: 'x', compliance: 4, enabled: true, priority: 1 }];
  if (/validate|validation/.test(p)) return { blockerPresent: false, findings: [] };
  if (/\/(stats|summary|status|settings|me|count|health|ready)$/.test(p)) return {};
  return [];
}
http.createServer((req, res) => {
  res.setHeader('Access-Control-Allow-Origin', req.headers.origin || '*');
  res.setHeader('Access-Control-Allow-Credentials', 'true');
  res.setHeader('Access-Control-Allow-Headers', req.headers['access-control-request-headers'] || '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET,POST,PUT,PATCH,DELETE,OPTIONS');
  if (req.method === 'OPTIONS') return res.writeHead(204).end();
  res.setHeader('Content-Type', 'application/json');
  res.end(JSON.stringify(body(req.url)));
}).listen(7457, '127.0.0.1', () => console.log('mock api on 7457'));
```

Known gaps in the mock: `/api/users/roles` (Users page shows "Cannot read properties of undefined
(reading 'length')" — it expects `{ roles: [...] }`), and every endpoint not listed above.
