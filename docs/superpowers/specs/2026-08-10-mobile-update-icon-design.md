# Design Document: Sleek Icon-Only Mobile Update Check in Topbar

## Goal
Redesign `<MobileUpdateCheck />` from a bulky text button into a compact, sleek icon button in the header (`CompanionTopbar`) that fits harmoniously next to `<ThemeToggle />`.

## UI Details
1. **Icon Button**:
   - 36px x 36px icon button styled identically to standard header controls (`icon-btn` / `ghost` / `subtle` style).
   - Display `RefreshCw` icon when idle/checking.
   - If an update is available, display a green update badge or switch icon to `DownloadCloud`.
2. **Interaction & Popover**:
   - Tapping the icon button triggers the update check.
   - If an update is available (or when checked), show a clean floating popover/toast below the icon with:
     - Version info (`v0.1.x available`).
     - "Download & install" primary CTA button.
     - Auto-dismiss / tap outside to close.

## Verification Plan
- Verify frontend compiles (`pnpm --filter frontend run typecheck`).
- Verify visual layout of `CompanionTopbar` keeps title and icons cleanly aligned without text wrapping or layout distortion.
