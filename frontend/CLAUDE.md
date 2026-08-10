# frontend/ — surface model (RADIOPAD_SURFACE)

> Loaded when working under `frontend/`. The rule this detail serves lives in the root
> [CLAUDE.md](../CLAUDE.md); the mechanics live here.

RadioPad is **desktop-first, surface-specialised**. The single `frontend/` codebase builds into three scoped bundles selected by the `RADIOPAD_SURFACE` build flag:

- **desktop** = the entire reporting product (worklist, editor, dictation, library authoring, personal settings, **companion host**). Clinical roles.
- **web** = master-admin / platform operations ONLY (`admin/*`, users, billing, SSO, providers, governance, usage). NO reporting. Clinical-only users get a "download the desktop app" interstitial ([WebAdminGate](components/shell/WebAdminGate.tsx)).
- **mobile** = pairing + voice dictation **companion** to a live desktop session, **plus** a standalone **Reporting** tab (2026-08 — see below). NO admin/platform surface.

How it works: routes live in App Router **route groups** `app/(desktop|web|mobile|shared)/`. [scripts/build-surface.mjs](scripts/build-surface.mjs) (`pnpm --filter @radiopad/frontend build:{desktop,web,mobile}`) sets the flag, stages non-target groups OUT of `app/` (and swaps the root `/` for a redirect on web/mobile), runs `next build`, and moves `out/` → `out-<surface>`. So each shell **physically** ships only its routes. [lib/surface.ts](lib/surface.ts) exposes `SURFACE`/`isWebSurface`/`surfaceAllows`; nav is surface-tagged in [nav.config.tsx](components/shell/nav.config.tsx). Tauri consumes `out-desktop` (`build:desktop`), Capacitor `out-mobile` (`build:mobile`), web deploy serves `out-web`. Plain `next dev` = full desktop app (all groups present).

The **companion** relay is a cloud subsystem (`/ws/companion` + `/api/companion/*`, [lib/companion.ts](lib/companion.ts), [CompanionHostPanel](components/companion/CompanionHostPanel.tsx)) — desktop advertises a code, phone pairs and streams dictation into the desktop's focused section via `getLastFocusedSectionEditor().insertAtCursor`.

**The old "mobile has NO standalone reporting" rule is revoked (2026-08).** It was a
misreading of an unfinished feature as an accident. Mobile's `app/(mobile)/reporting/`
route is real, intentional, and required: a radiologist logs into the mobile app with
real credentials — **no desktop pairing** — creates a report (name/age/gender/modality/
region of scan), and records **one or more** audio dictation takes against it. Each take
uploads immediately and is pushed live (existing SignalR) to the matching desktop report's
**"Positive Findings & Mobile Dictations"** tab
([PositiveFindingsTab.tsx](app/(desktop)/reports/[id]/components/PositiveFindingsTab.tsx)),
where a dedicated audio card ([DictationAudioCard.tsx](app/(desktop)/reports/[id]/components/DictationAudioCard.tsx))
lets the radiologist pick a transcription engine (default **medASR-6gram**, the local
on-device model; a dropdown lists any other enabled provider) and, one click, transcribe
the take into the editable Positive Findings text — reviewed and appended manually (never
auto-inserted) per the AI-text-marked-until-reviewed rule. Purpose: a senior consultant
dictates findings on the go; a junior resident/PGR finishes the written report from that
dictation. This route stays behind the real `AuthGate` (unlike `/companion`, which is
intentionally public for QR pairing) and is built only with RC design tokens/`.rp-*`
classes — no hardcoded colors.
