**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# Screenshot Index

No screenshots were captured.

## Blocker

This CLI environment did not provide a browser automation, rendered preview, or screenshot capture tool. Frontend validation/dev scripts were also blocked before execution by pnpm's ignored-builds policy for `esbuild` and `sharp`.

## Screenshot Coverage Table

| Route | Viewport | Screenshot Path | Notes |
|---|---:|---|---|
| `/` | 320, 390, 768, 1280 | Not captured | Source-level responsive risk only. |
| `/login` | 320, 390, 1280 | Not captured | Source-level shell/copy audit only. |
| `/reports/view?id=...` | 320, 768, 1024, 1440 | Not captured | Source-level editor/toolbar/layout audit only. |
| `/validation` | 320, 768, 1280 | Not captured | Source-level table audit only. |
| `/audit` | 320, 1280 | Not captured | Source-level table audit only. |
| `/analytics` | 390, 1280, 1920 | Not captured | Source-level active-state/copy audit only. |
| `/rulebooks` | 320, 768, 1280 | Not captured | Source-level responsive grid audit only. |
| `/rulebooks/editor` | 320, 768, 1280 | Not captured | Source-level split layout audit only. |
| `/templates` | 320, 768, 1280 | Not captured | Source-level table/modal audit only. |
| `/prompts` | 320, 1280 | Not captured | Source-level design-system/tab audit only. |
| `/providers` | 320, 768, 1280 | Not captured | Source-level table/modal audit only. |
| `/mobile/dictate` | 320, 390 | Not captured | Source-level mobile/native audit only. |
| `/mobile/reports/edit` | 320, 390 | Not captured | Source-level action-row audit only. |
| `/mobile/reports/sign` | 320, 390 | Not captured | Source-level action-row/export audit only. |
| `/admin/*` | 320, 768, 1280 | Not captured | Source-level admin table/form audit only. |
