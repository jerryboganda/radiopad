# Desktop App UX

**Status:** Current  ·  **Owner:** Design  ·  **Last Updated:** 2026-05-04

The desktop app is a Tauri 2 shell wrapping the Next.js static export.

## Installation

- Windows: signed `.msi` installer (planned). Dev build: `cargo tauri build` produces an unsigned bundle.
- macOS: notarised `.dmg` (planned).
- Linux: `.AppImage` and `.deb`.

## Auto-update

- Tauri updater pointed at a signed manifest hosted on the release CDN (planned).
- Update channel: `stable` by default, `beta` opt-in.
- Update applied on next launch with a "What's new" banner.

## System tray / menu

- v0.1 has a single window — no tray icon. The Tauri capability set in `desktop/src-tauri/capabilities/default.json` is intentionally minimal.
- Phase 2: tray icon with quick-actions (open last report, Run audit verify).

## Global shortcuts

- `Ctrl+Shift+R` (Windows/Linux) / `⌘⇧R` (macOS): bring RadioPad to the front. Wired in `desktop/src-tauri/src/main.rs`.

## Offline behaviour

- The shell loads cached static assets but the API is not cached. Without network connectivity, the dashboard shows a `.banner.warn` "Offline — some actions disabled".
- Drafts in flight are not auto-saved offline in v0.x; on reconnect, the pending PATCH retries once.

## OS permissions

- Network access only.
- Clipboard write via `secure_copy` (clears after a 30 s TTL) — see [../03-architecture/desktop-architecture.md](../03-architecture/desktop-architecture.md).
- No camera, no microphone, no contacts.

## Local file access

- File save dialog for FHIR export (`text/plain` and `application/fhir+json`).
- Audit export writes to a user-chosen folder.
- No silent file writes.

## Differences from web

- Global shortcut.
- Clipboard TTL wipe.
- A future "Local-first cache" mode (Phase 4) for fully on-prem reading rooms.
