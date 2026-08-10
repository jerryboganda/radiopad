# RadioPad Frontend Surfaces

- Routes live in `frontend/app/(desktop|web|mobile|shared)/`; the build flag stages only
  the selected surface into the static export.
- Desktop is the full reporting product, web is master-admin/platform operations, and
  mobile is the companion plus the intentional standalone reporting flow documented in
  `frontend/CLAUDE.md`.
- Tag navigation entries with the correct surface and preserve the `WebAdminGate` and
  companion relay boundaries.
- Read `frontend/CLAUDE.md` before adding or moving a route, page, or nav entry.
- Keep UI compatible with Tauri and Capacitor static exports; do not assume browser-only
  server APIs in shared code.
