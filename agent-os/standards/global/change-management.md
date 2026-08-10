# RadioPad Change Management

- Read `CLAUDE.md` first; read `frontend/CLAUDE.md` before changing frontend routes or nav.
- Update `PROGRESS.md` for completed product checklist work and update the canonical `docs/`
  page when behavior or an API contract changes.
- Files called out as requiring human review in `AGENTS.md` stay human-reviewed.
- Full builds, full test suites, lint/typecheck sweeps, packaging, and Docker builds run in
  GitHub Actions, not locally.
- Local validation is limited to a focused test, a quick app run, or another cheap check.
- Any change under `frontend/` or `desktop/` requires the `pnpm release:desktop` release
  ritual after commit and push; backend, CLI, docs, and standards-only changes do not.
