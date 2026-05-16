---
applyTo: "**"
---

# Documentation instructions

- Update the canonical document in [docs/](../../docs/) in the same PR as a behaviour change. Stale docs are treated as bugs.
- Living documents carry the header `**Status:** ...  **Owner:** ...  **Last Updated:** YYYY-MM-DD`. Bump `Last Updated` whenever you edit.
- Architecture decisions are recorded as ADRs under `docs/03-architecture/adr/`. Use the existing template; never edit a merged ADR — supersede it.
- HTTP API changes update [docs/03-architecture/api-reference.md](../../docs/03-architecture/api-reference.md) **and** [openapi/openapi.yaml](../../openapi/openapi.yaml).
- CLI changes update [docs/08-user-docs/cli-guide.md](../../docs/08-user-docs/cli-guide.md).
- New rulebooks add a row to [docs/05-clinical/rulebook-authoring.md](../../docs/05-clinical/rulebook-authoring.md) plus golden cases.
- New design tokens / component classes update both `frontend/app/globals.css` and [docs/02-design/design.md](../../docs/02-design/design.md).
- The Ralph-loop log lives in [PROGRESS.md](../../PROGRESS.md) — append a new iteration block, never rewrite history.
- `CHANGELOG.md` follows Keep a Changelog; entries land under `[Unreleased]` until a tag is cut.
- No secrets, PHI, or real patient data in any document, screenshot, or fixture.
