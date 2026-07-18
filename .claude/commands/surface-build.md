---
description: Build one RadioPad frontend surface (desktop|web|mobile) via the route-group staging script.
argument-hint: "desktop|web|mobile"
allowed-tools: Bash(pnpm --filter @radiopad/frontend build:*)
---

Build the requested surface bundle. Requested surface: `$ARGUMENTS` (must be one of `desktop`, `web`, `mobile`).

If `$ARGUMENTS` is empty or not one of the three, list the options and stop.

Otherwise run: `pnpm --filter @radiopad/frontend build:$ARGUMENTS`

This invokes `frontend/scripts/build-surface.mjs`, which stages the non-target route groups out of `app/`, swaps `/` for a redirect on web/mobile, runs `next build`, and moves `out/` → `out-$ARGUMENTS`. CI only builds the default desktop surface, so this is how you catch a broken `web`/`mobile` surface (off-surface import, staging leftover, bad redirect stub) before release.

Report success or the first error from the `next build` output.
