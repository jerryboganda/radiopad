**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# UI/UX Fix Backlog

| Backlog ID | Task | Severity | Files/Components | Acceptance Criteria |
|---|---|---|---|---|
| UIUX-BL-001 | Add canonical responsive table/card primitive. | HIGH | `frontend/components/ui`, table pages, `radiopad.css` | No horizontal page overflow at 320-768; tables have scroll/card behavior and accessible headers. |
| UIUX-BL-002 | Implement accessible mobile drawer behavior. | HIGH | `AppShell`, `Sidebar`, `ShellContext`, `shell.css` | Closed drawer is inert; open drawer traps focus, closes on Escape, returns focus. |
| UIUX-BL-003 | Add shared accessible Dialog component. | HIGH | Providers, templates, report confirmations | Dialog has role/name, focus trap, initial focus, Escape/backdrop policy, focus return. |
| UIUX-BL-004 | Audit and fix form labels. | HIGH | Login, report editor, admin/forms | Every input/select/textarea has programmatic label. |
| UIUX-BL-005 | Rework report editor action hierarchy. | HIGH | `ReportClient.tsx` | One primary next action, grouped secondary actions, export menu with disabled reasons. |
| UIUX-BL-006 | Fix report editor tablet/mobile layout. | HIGH | `radiopad.css`, `ReportClient.tsx` | Clinical context, editor, validation, and actions remain discoverable at 768/1024. |
| UIUX-BL-007 | Make rulebook editor responsive. | HIGH | `globals.css`, `RulebookEditorClient.tsx` | `.split` stacks below tablet widths without overflow. |
| UIUX-BL-008 | Make templates modal responsive and accessible. | HIGH | `templates/page.tsx` | Section rows stack on mobile; dirty-close confirmation exists; dialog is accessible. |
| UIUX-BL-009 | Rebuild Prompt Studio on canonical page primitives. | HIGH | `prompts/page.tsx` | No undefined/generic panel classes; no browser `prompt()`; tabs are accessible. |
| UIUX-BL-010 | Standardize data page states. | HIGH | All API-backed pages | Loading uses Skeleton, empty uses EmptyState, error uses ErrorState/onRetry. |
| UIUX-BL-011 | Add skip navigation. | MEDIUM | `AppShell`, `shell.css` | Keyboard users can skip to `<main id="main-content">`. |
| UIUX-BL-012 | Add live regions for dynamic statuses. | MEDIUM | Report editor, settings, validation, exports | Save/loading/success/error states are announced appropriately. |
| UIUX-BL-013 | Verify and fix contrast. | MEDIUM | `globals.css`, design docs | Primary controls and faint text pass WCAG contrast for normal text. |
| UIUX-BL-014 | Add anchor-compatible button styling. | HIGH | `globals.css`, components using `primary-ghost` links | Link CTAs visually match locked button variants and remain accessible. |
| UIUX-BL-015 | Centralize status labels. | MEDIUM | `frontend/lib`, status pages | Report/rulebook/template/validation labels use one glossary. |
| UIUX-BL-016 | Fix undefined green token. | MEDIUM | `radiopad.css` | `.banner.ok` uses documented semantic token. |
| UIUX-BL-017 | Clean production copy. | HIGH | Login, dashboard, validation, analytics, report editor | No internal commands/PRD/header details in user-facing copy. |
| UIUX-BL-018 | Add native shell recovery UX. | HIGH | `DesktopStatusBanner`, `ShellBridge`, native config | Backend failure/biometric/offline states include clear recovery actions. |
| UIUX-BL-019 | Define mobile backend URL strategy. | HIGH | `api.ts`, mobile config/docs | Mobile dev/staging/prod/offline backend states are explicit and testable. |
| UIUX-BL-020 | Secure mobile offline drafts. | HIGH | `offlineDrafts.ts`, mobile shell | PHI-bearing drafts are encrypted or gated by documented platform controls. |
| UIUX-BL-021 | Add mobile permission fallback UX. | MEDIUM | Dictation, push, biometric | Denied/restricted/unavailable states have clear Settings/retry/manual fallback. |
| UIUX-BL-022 | Add screenshot/accessibility regression workflow. | MEDIUM | Test tooling/CI | Key routes have viewport screenshots and axe checks. |
