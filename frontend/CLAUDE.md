# frontend/ — surface model (RADIOPAD_SURFACE)

> Loaded when working under `frontend/`. The rule this detail serves lives in the root
> [CLAUDE.md](../CLAUDE.md); the mechanics live here.

RadioPad is **desktop-first, surface-specialised**. The single `frontend/` codebase builds into three scoped bundles selected by the `RADIOPAD_SURFACE` build flag:

- **desktop** = the entire reporting product (worklist, editor, dictation, library authoring, personal settings, **companion host**). Clinical roles.
- **web** = master-admin / platform operations ONLY (`admin/*`, users, billing, SSO, providers, governance, usage). NO reporting. Clinical-only users get a "download the desktop app" interstitial ([WebAdminGate](components/shell/WebAdminGate.tsx)).
- **mobile** = a dictation **companion** that pairs to a live desktop session (pairing + voice dictation + remote only). NO standalone reporting.

How it works: routes live in App Router **route groups** `app/(desktop|web|mobile|shared)/`. [scripts/build-surface.mjs](scripts/build-surface.mjs) (`pnpm --filter @radiopad/frontend build:{desktop,web,mobile}`) sets the flag, stages non-target groups OUT of `app/` (and swaps the root `/` for a redirect on web/mobile), runs `next build`, and moves `out/` → `out-<surface>`. So each shell **physically** ships only its routes. [lib/surface.ts](lib/surface.ts) exposes `SURFACE`/`isWebSurface`/`surfaceAllows`; nav is surface-tagged in [nav.config.tsx](components/shell/nav.config.tsx). Tauri consumes `out-desktop` (`build:desktop`), Capacitor `out-mobile` (`build:mobile`), web deploy serves `out-web`. Plain `next dev` = full desktop app (all groups present).

The **companion** relay is a cloud subsystem (`/ws/companion` + `/api/companion/*`, [lib/companion.ts](lib/companion.ts), [CompanionHostPanel](components/companion/CompanionHostPanel.tsx)) — desktop advertises a code, phone pairs and streams dictation into the desktop's focused section via `getLastFocusedSectionEditor().insertAtCursor`.
