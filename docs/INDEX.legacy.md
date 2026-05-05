# RadioPad documentation

This documentation tree is organised by audience and lifecycle phase. Treat the
top-level directories as canonical — when adding new docs, slot them into the
matching folder rather than creating loose files.

| # | Folder | Owner | Contents |
| - | ------ | ----- | -------- |
| 00 | [`00-product/`](00-product/) | Product | Vision, PRD, personas, user stories |
| 02 | [`02-design/`](02-design/) | Design | **Locked** UI/UX system spec — `design.md` |
| 03 | [`03-architecture/`](03-architecture/) | Engineering | System architecture, data model, AI gateway, audit |
| 04 | [`04-security/`](04-security/) | Security | Threat model, PHI policy, controls mapping (HIPAA/GDPR) |
| 06 | [`06-testing/`](06-testing/) | QA | Test strategy, golden cases, regression matrix |
| 07 | [`07-devops/`](07-devops/) | Platform | Local dev setup, build/release, CI/CD |
| 08 | [`08-user-docs/`](08-user-docs/) | Docs | CLI guide, desktop guide, admin guide |

## Cross-cutting rules

- **UI/UX is locked.** Any visual change must update `02-design/design.md` AND
  `frontend/app/globals.css` / `frontend/app/radiopad.css` in the same change.
  See `AGENTS.md §0` for the non-negotiable rules.
- **PRD is the source of intent.** `RadioPad — Enterprise PRD _ Project Requirement Detail Document.md`
  at the repo root is the authoritative requirements document; sub-docs link
  back to its sections.
- **Audit log is append-only.** Documentation describing audit behaviour must
  match the integrity-chain implementation in `RadioPad.Infrastructure`.
