# Design Document: Fix Mobile Bottom Navigation Overlap & Move Check for Updates to Topbar

## Problem Statement
In the mobile companion app, the bottom navigation bar (`<nav>`) is `fixed` at the bottom of the screen with `z-index: 40`. The content of the mobile page (`.rp-comp-body`) lacked bottom padding, causing elements at the bottom—such as the `<MobileUpdateCheck />` button—to be obscured behind the bottom navigation bar. Additionally, the user requested moving the "Check for updates" functionality to the topbar (`CompanionTopbar`).

## Proposed Solution

1. **Move `MobileUpdateCheck` into `CompanionTopbar`**:
   - Render `<MobileUpdateCheck />` inside `CompanionTopbar` right next to `<ThemeToggle />`.
   - Update `MobileUpdateCheck` styling to fit cleanly inside topbar actions (flex row with `ThemeToggle`).

2. **Add Bottom Padding to Mobile Layout**:
   - Ensure `.rp-comp-body` and mobile content containers have sufficient bottom padding (e.g. `pb-24` / `100px`) so scrollable content is fully visible above the fixed bottom navigation bar.

## Verification
- Verify layout visually and structurally in Next.js mobile build.
- Confirm `<MobileUpdateCheck />` is visible and accessible in `CompanionTopbar`.
- Confirm bottom content is no longer overlapped by the fixed bottom navigation bar.
