# 11 — Screenshot Index

**Status:** Not Captured (Blocked)  
**Owner:** Audit  
**Last Updated:** 2026-05-16

---

## Executive Summary

This document is a **placeholder and capture protocol** for visual regression tracking of the RadioPad web client. Screenshots have **not been captured** in this audit session because:

1. The ASP.NET Core 8 backend (`127.0.0.1:7457`) is not running in the audit environment.
2. The frontend is a static export (`next.config.ts` sets `output: 'export'`) — no route handlers, no middleware at runtime.
3. **All 33 data-driven routes call the `api` client** which requires the backend to be live. Without it, every data request fails and renders `<ErrorState>` or perpetual `<Skeleton>`.

**Outcome:** Screenshots would have minimal signal for visual QA. Instead, this document outlines the **capture protocol** for a follow-up session with both frontend and backend running.

---

## Status: Blocked

### Blocker Details

| Dependency | Status | Impact |
|---|---|---|
| Backend (`backend/RadioPad.Api`) | ❌ Not running | All data-driven pages fail to load. |
| Frontend dev server (`pnpm dev`) | ✅ Capable | Can start, but will show error states only. |
| Static export artifact | ✅ Available | `next.config.ts` exports to disk; no visual feedback without browser. |
| Seed data (dev tenant) | ⚠️ Assumed | `radiologist@it-radiologist@radiopad.local` tenant created by migrations, but unverified without backend. |

### Data-Driven Pages Affected

**33 of 38 routes** depend on backend API calls:

- `frontend/app/reports/**` (6 routes) — fetch reports, rulebook findings, prior studies
- `frontend/app/rulebooks/**` (5 routes) — fetch rulebook metadata, YAML definitions
- `frontend/app/templates/**` (2 routes) — fetch template library
- `frontend/app/providers/**` (2 routes) — fetch AI provider configurations
- `frontend/app/admin/**` (8 routes) — fetch users, audit logs, etc.
- `frontend/app/workspace/**` (4 routes) — fetch workspace settings, invitations
- `frontend/app/validation/**` (1 route) — fetch validation rules
- `frontend/app/analytics/**` (3 routes) — fetch analytics / quality metrics
- `frontend/app/settings/**` (2 routes) — fetch user settings

Without backend, all render `<ErrorState onRetry>` with the message "Something went wrong. Try again."

### Public / Static Routes (Capturable)

**5 routes do not require backend data:**
- `frontend/app/page.tsx` (landing / dashboard stub — but shows welcome message with no data)
- `frontend/app/auth/login/page.tsx` (login form — external auth flow; would show form only)
- `frontend/app/auth/logout/page.tsx` (logout confirmation)
- `frontend/app/onboarding/**` (2 routes) — registration flow (would show forms only)

**Visual signal:** Limited — auth pages outside `<AppShell>` use minimal styling; no real data context.

---

## Capture Protocol (For Follow-Up)

### Phase 1: Environment Setup

#### 1a. Start the Backend

```powershell
cd backend/RadioPad.Api
dotnet restore
dotnet build
dotnet run --project src/RadioPad.Api
# Expected output: "Listening on http://127.0.0.1:7457"
# Database: SQLite at `backend/RadioPad.Api/radiopad.db`
```

**Expected behavior:**
- Migrations run automatically on startup (EF Core).
- Seed tenant created: `slug = 'it'`, name = `'Integration Test'`.
- Seed radiologist user created: `email = 'it-radiologist@radiopad.local'`, password = (hardcoded in seed, check code).
- Health check available: `GET http://127.0.0.1:7457/api/health` → `{ "status": "ok" }`.

#### 1b. Start the Frontend Dev Server

In a separate terminal:

```powershell
cd frontend
pnpm install  # if not already done
pnpm dev
# Expected output: "ready - started server on http://localhost:3000"
```

**Dev server behavior:**
- Hot reload enabled.
- API calls proxied to `http://127.0.0.1:7457` (configured in `next.config.ts`).
- `pnpm build` workaround: use `.\node_modules\.bin\next build` directly if needed.

#### 1c. Seed Test Data (Radiologist Tenant)

The backend seed creates a default tenant. To create test data (reports, rulebooks, etc.):

**Option A (Manual):** Navigate to `http://localhost:3000`, authenticate as `it-radiologist@radiopad.local`, and create sample reports/rulebooks via UI.

**Option B (CLI):** Use the RadioPad CLI (if available):
```powershell
cd cli/RadioPad.Cli
dotnet run -- seed --tenant=it --data-set=sample
```
(Check `cli/RadioPad.Cli/Program.cs` for seed command availability.)

**Option C (SQL):** Insert fixture data directly into the SQLite database at `backend/RadioPad.Api/radiopad.db` (requires schema knowledge).

### Phase 2: Screenshot Capture

#### 2a. Browser & Device Emulation

Use **Chrome DevTools device emulation** to capture at three canonical viewports:

| Viewport | Width | Height | Device | Rationale |
|---|---|---|---|---|
| Mobile | 390 px | 844 px | iPhone 14 | Most common mobile screen; narrow CSS breakpoint test. |
| Tablet | 768 px | 1024 px | iPad (landscape) | Medium breakpoint; touch targets visible. |
| Desktop | 1440 px | 900 px | Laptop / monitor | Primary dev target; sidebar visible. |

**Instructions:**
1. Open Chrome DevTools: `F12` or `Right-click → Inspect`.
2. Toggle device emulation: `Ctrl+Shift+M` (or DevTools icon).
3. Select device from preset dropdown, or set custom dimensions.
4. Take screenshot: `Ctrl+Shift+P` → "Capture screenshot" or use Playwright (below).

#### 2b. Screenshot Tool (Recommended: Playwright)

For batch capture and consistency, use Playwright:

```bash
npm install --save-dev @playwright/test @axe-core/playwright
```

Create `frontend/tests/screenshots.spec.ts`:

```typescript
import { test, expect } from '@playwright/test';

const ROUTES = [
  { path: '/', name: 'dashboard' },
  { path: '/reports', name: 'reports-list' },
  { path: '/reports/1', name: 'reports-detail' },
  // ... 35 more routes
];

const VIEWPORTS = [
  { width: 390, height: 844, name: 'mobile' },
  { width: 768, height: 1024, name: 'tablet' },
  { width: 1440, height: 900, name: 'desktop' },
];

test.describe('Screenshots', () => {
  for (const route of ROUTES) {
    for (const viewport of VIEWPORTS) {
      test(`capture ${route.name} at ${viewport.name}`, async ({ page }) => {
        await page.setViewportSize(viewport);
        await page.goto(`http://localhost:3000${route.path}`);
        await page.waitForLoadState('networkidle'); // Wait for API calls
        await expect(page).toHaveScreenshot(
          `${route.name}/${viewport.name}.png`,
          { maxDiffPixels: 100 } // Allow small pixel variations
        );
      });
    }
  }
});
```

Run:
```bash
npx playwright test frontend/tests/screenshots.spec.ts --update
```

This creates baseline PNGs in `frontend/tests/__screenshots__/`. On future runs, Playwright compares and flags visual changes.

#### 2c. Capture States for Each Route

For each route, capture **four states**:

| State | Trigger | Example |
|---|---|---|
| **Loading** | Page load, before API response | `<Skeleton>` visible, placeholders. |
| **Populated** | API returns data | Full page with real/seed data. |
| **Empty** | API returns empty list (0 items) | `<EmptyState icon="folder" title="No reports">` |
| **Error** | API returns 5xx or network fails | `<ErrorState title="Something went wrong" onRetry>` |

**How to trigger each:**
- **Loading:** DevTools throttle → Slow 3G; take screenshot mid-load.
- **Populated:** Normal network speed; seed test data before capture.
- **Empty:** Delete/clear all test data via API or UI, then reload.
- **Error:** Stop backend, reload page, screenshot error state.

### Phase 3: Batch Capture Checklist

Complete this checklist during capture session:

```markdown
## 38-Route Batch Capture Checklist

### Auth & Public Routes (5)
- [ ] `/` (dashboard) — 3 viewports × 1 state = 3 PNGs
- [ ] `/auth/login` — 3 viewports × 1 state = 3 PNGs
- [ ] `/auth/logout` — 3 viewports × 1 state = 3 PNGs
- [ ] `/onboarding/invite` — 3 viewports × 1 state = 3 PNGs
- [ ] `/onboarding/setup` — 3 viewports × 1 state = 3 PNGs

### Reports Routes (6)
- [ ] `/reports` — 3 viewports × 4 states = 12 PNGs
- [ ] `/reports/[id]` — 3 viewports × 1 state = 3 PNGs (detail always populated)
- [ ] `/reports/[id]/edit` — 3 viewports × 1 state = 3 PNGs
- [ ] `/reports/[id]/preview` — 3 viewports × 1 state = 3 PNGs
- [ ] `/reports/[id]/signature` — 3 viewports × 1 state = 3 PNGs
- [ ] `/reports/draft` — 3 viewports × 2 states (empty / populated) = 6 PNGs

### Rulebooks Routes (5)
- [ ] `/rulebooks` — 3 viewports × 4 states = 12 PNGs
- [ ] `/rulebooks/[id]` — 3 viewports × 1 state = 3 PNGs
- [ ] `/rulebooks/editor` — 3 viewports × 1 state = 3 PNGs
- [ ] `/rulebooks/editor/[id]` — 3 viewports × 1 state = 3 PNGs
- [ ] `/rulebooks/history` — 3 viewports × 2 states = 6 PNGs

### Templates Routes (2)
- [ ] `/templates` — 3 viewports × 4 states = 12 PNGs
- [ ] `/templates/[id]` — 3 viewports × 1 state = 3 PNGs

### Providers Routes (2)
- [ ] `/providers` — 3 viewports × 4 states = 12 PNGs
- [ ] `/providers/[id]` — 3 viewports × 1 state = 3 PNGs

### Admin Routes (7)
- [ ] `/admin/users` — 3 viewports × 4 states = 12 PNGs
- [ ] `/admin/users/[id]` — 3 viewports × 1 state = 3 PNGs
- [ ] `/admin/audit` — 3 viewports × 4 states = 12 PNGs
- [ ] `/admin/settings` — 3 viewports × 1 state = 3 PNGs
- [ ] `/admin/providers/[id]/oauth` — 3 viewports × 1 state = 3 PNGs
- [ ] `/admin/billing` — 3 viewports × 2 states = 6 PNGs
- [ ] `/admin/security` — 3 viewports × 1 state = 3 PNGs

### Workspace Routes (4)
- [ ] `/workspace/members` — 3 viewports × 2 states = 6 PNGs
- [ ] `/workspace/settings` — 3 viewports × 1 state = 3 PNGs
- [ ] `/workspace/invitations` — 3 viewports × 2 states = 6 PNGs
- [ ] `/workspace/billing` — 3 viewports × 1 state = 3 PNGs

### Settings Routes (3)
- [ ] `/settings/profile` — 3 viewports × 1 state = 3 PNGs
- [ ] `/settings/password` — 3 viewports × 1 state = 3 PNGs
- [ ] `/settings/preferences` — 3 viewports × 1 state = 3 PNGs

### Validation Routes (1)
- [ ] `/validation` — 3 viewports × 1 state = 3 PNGs

### Analytics Routes (3)
- [ ] `/analytics/dashboard` — 3 viewports × 1 state = 3 PNGs
- [ ] `/analytics/quality` — 3 viewports × 1 state = 3 PNGs
- [ ] `/analytics/trends` — 3 viewports × 1 state = 3 PNGs

**Total:** ~240 PNGs (38 routes × 3 viewports × avg 2.1 states)
```

---

## Capture Inventory Template

After completing Phase 2–3, populate this table. Copy-paste and fill in:

| # | Route | URL | Viewports | States | Auth | Sample Data | Captured | Notes |
|---|---|---|---|---|---|---|---|---|
| 1 | Dashboard | `/` | mobile, tablet, desktop | — | No | None | ❌ | No backend needed. |
| 2 | Login | `/auth/login` | mobile, tablet, desktop | — | No | None | ❌ | Public form. |
| 3 | Logout | `/auth/logout` | desktop | — | No | None | ❌ | Confirmation only. |
| 4 | Onboarding (Invite) | `/onboarding/invite` | mobile, tablet, desktop | — | No | None | ❌ | Registration flow. |
| 5 | Onboarding (Setup) | `/onboarding/setup` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Tenant configuration. |
| 6 | Reports List | `/reports` | mobile, tablet, desktop | loading, populated, empty, error | Yes | Seed data | ❌ | Data-driven; 4 states needed. |
| 7 | Report Detail | `/reports/[id]` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Populated only. |
| 8 | Report Edit | `/reports/[id]/edit` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Editor interface. |
| 9 | Report Preview | `/reports/[id]/preview` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Read-only preview. |
| 10 | Report Signature | `/reports/[id]/signature` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Signing UI. |
| 11 | Draft Reports | `/reports/draft` | mobile, tablet, desktop | populated, empty | Yes | Seed data | ❌ | In-progress reports. |
| 12 | Rulebooks List | `/rulebooks` | mobile, tablet, desktop | loading, populated, empty, error | Yes | Seed data | ❌ | Data-driven. |
| 13 | Rulebook Detail | `/rulebooks/[id]` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Read-only YAML view. |
| 14 | Rulebook Editor | `/rulebooks/editor` | desktop | — | Yes | None | ❌ | YAML editing; desktop-only. |
| 15 | Rulebook Editor Detail | `/rulebooks/editor/[id]` | desktop | — | Yes | Seed data | ❌ | Editing existing rulebook. |
| 16 | Rulebook History | `/rulebooks/history` | mobile, tablet, desktop | populated, empty | Yes | Seed data | ❌ | Version history. |
| 17 | Templates List | `/templates` | mobile, tablet, desktop | loading, populated, empty, error | Yes | Seed data | ❌ | Data-driven. |
| 18 | Template Detail | `/templates/[id]` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Template view. |
| 19 | Providers List | `/providers` | mobile, tablet, desktop | loading, populated, empty, error | Yes | Seed data | ❌ | AI provider configs. |
| 20 | Provider Detail | `/providers/[id]` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Provider settings. |
| 21 | Admin Users | `/admin/users` | mobile, tablet, desktop | loading, populated, empty, error | Yes | Seed data | ❌ | User management. |
| 22 | Admin User Detail | `/admin/users/[id]` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | User edit. |
| 23 | Admin Audit Log | `/admin/audit` | mobile, tablet, desktop | loading, populated, empty, error | Yes | Seed data | ❌ | Audit trail. |
| 24 | Admin Settings | `/admin/settings` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | System config. |
| 25 | Admin Provider OAuth | `/admin/providers/[id]/oauth` | desktop | — | Yes | None | ❌ | OAuth flow callback. |
| 26 | Admin Billing | `/admin/billing` | mobile, tablet, desktop | normal, suspended | Yes | Seed data | ❌ | Billing status. |
| 27 | Admin Security | `/admin/security` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Security settings. |
| 28 | Workspace Members | `/workspace/members` | mobile, tablet, desktop | populated, empty | Yes | Seed data | ❌ | Team members. |
| 29 | Workspace Settings | `/workspace/settings` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Workspace config. |
| 30 | Workspace Invitations | `/workspace/invitations` | mobile, tablet, desktop | populated, empty | Yes | Seed data | ❌ | Pending invites. |
| 31 | Workspace Billing | `/workspace/billing` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Billing management. |
| 32 | Settings Profile | `/settings/profile` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | User profile edit. |
| 33 | Settings Password | `/settings/password` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Password change. |
| 34 | Settings Preferences | `/settings/preferences` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | User preferences. |
| 35 | Validation | `/validation` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Validation rules view. |
| 36 | Analytics Dashboard | `/analytics/dashboard` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | KPIs & trends. |
| 37 | Analytics Quality | `/analytics/quality` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | QA metrics. |
| 38 | Analytics Trends | `/analytics/trends` | mobile, tablet, desktop | — | Yes | Seed data | ❌ | Historical trends. |

---

## File Organization

Save captured PNGs to:

```
docs/ui-ux-audit/screenshots/
├── auth/
│   ├── login/
│   │   ├── mobile.png
│   │   ├── tablet.png
│   │   └── desktop.png
│   ├── logout/
│   │   ├── mobile.png
│   │   ├── tablet.png
│   │   └── desktop.png
│   └── …
├── reports/
│   ├── list/
│   │   ├── mobile_loading.png
│   │   ├── mobile_populated.png
│   │   ├── mobile_empty.png
│   │   ├── mobile_error.png
│   │   ├── tablet_*.png
│   │   ├── desktop_*.png
│   │   └── …
│   ├── detail/
│   │   ├── mobile.png
│   │   ├── tablet.png
│   │   └── desktop.png
│   └── …
├── rulebooks/
├── templates/
├── providers/
├── admin/
├── workspace/
├── settings/
├── validation/
└── analytics/
```

If using Playwright, this structure is generated automatically under `frontend/tests/__screenshots__/` and can be copied to `docs/ui-ux-audit/screenshots/` after capture.

---

## Visual Regression Testing Strategy (Proposed)

Once baseline screenshots exist, implement **visual regression detection** to flag unintended CSS changes:

### Option A: Playwright (Low Cost)

```bash
npx playwright test --update  # Baseline
npx playwright test            # Compare; fail on pixel changes > threshold
```

**Pros:** Built into test suite; no external service.  
**Cons:** Requires manual threshold tuning; false positives on minor rendering changes.

### Option B: Percy.io (Recommended)

```bash
npm install --save-dev @percy/cli @percy/playwright
```

Update `screenshots.spec.ts`:

```typescript
import { percySnapshot } from '@percy/playwright';

test('screenshots', async ({ page }) => {
  await page.goto('http://localhost:3000/reports');
  await percySnapshot(page, 'reports-list-desktop');
});
```

```bash
PERCY_TOKEN=<token> npx percy exec -- npx playwright test
```

**Pros:** Cloud-based; automatic smart diffing; browser history; team collaboration.  
**Cons:** Requires paid Percy account (~$99/month).

### Option C: Chromatic (Next.js + Storybook)

If Storybook is added (see `09-frontend-structure-audit.md`), Chromatic automatically captures stories and diffs on every build.

```bash
npm install --save-dev chromatic
npx chromatic --project-token=<token> --build-script-name=build
```

**Recommended:** Start with Playwright, upgrade to Percy once baseline is stable.

---

## Success Criteria

✅ **Capture complete when:**
1. All 38 routes captured at 3 viewports.
2. Data-driven routes captured in 4 states (loading, populated, empty, error).
3. 240+ PNGs stored in `docs/ui-ux-audit/screenshots/` with clear naming.
4. Inventory table (above) updated with ✅ marks.
5. Visual regression CI integrated (Playwright or Percy).
6. Baseline committed to repo (`git add docs/ui-ux-audit/screenshots/`).

---

## References

- **Capture protocol:** This document (§ Capture Protocol).
- **Backend setup:** `backend/RadioPad.Api/README.md` (or equivalent).
- **Frontend dev:** `frontend/README.md` + `CONTRIBUTING.md`.
- **Playwright guide:** https://playwright.dev/docs/api/class-browsercontext#browser-context-screenshot
- **Percy docs:** https://docs.percy.io/docs/getting-started
- **Chromatic docs:** https://www.chromatic.com/docs/

---

**End of placeholder. Awaiting follow-up session with backend + frontend running.**
