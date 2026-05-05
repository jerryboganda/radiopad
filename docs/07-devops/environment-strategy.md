# Environment Strategy

**Status:** Current (basic)  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-04

| Environment | Purpose | Hostname | DB | Secrets | Data |
| --- | --- | --- | --- | --- | --- |
| **local** | Developer workstation | `127.0.0.1:7457` / `localhost:3000` | SQLite `radiopad.dev.db` | `.env` | Synthetic |
| **ci** | CI test runs | per-runner | In-memory SQLite | GitHub Actions secrets | Synthetic |
| **staging** (planned) | Pre-prod verification | `staging.radiopad.example` | Postgres | Cloud secret manager | Synthetic only — never customer data |
| **prod** (planned hosted) | Customer traffic | `app.radiopad.example` (per-region) | Postgres + replicas | Cloud secret manager | Real PHI under BAA |
| **on-prem** | Customer-hosted deploy | per-customer | Postgres / customer choice | Customer secret manager | Real PHI |

## Promotion path

`local → CI → staging → prod`. On-prem is shipped as tagged container images and migration scripts; the customer drives promotion.

## Configuration sources

- Env vars (preferred). See [.env.example](../../.env.example).
- `appsettings.{Environment}.json` for non-secret config.
- Migrations from `dotnet ef migrations` ship inside the API image.

## Backups by environment

| Environment | Backup |
| --- | --- |
| local | None — disposable. |
| ci | None — ephemeral. |
| staging | Daily; 7-day retention; verified weekly. |
| prod | Continuous WAL + daily base; 35-day retention; geo-redundant. |
| on-prem | Customer responsibility; we ship a runbook. |

## Network defaults

- Backend binds `127.0.0.1:7457` by default (`RADIOPAD_BIND` overrides).
- TLS termination at the reverse proxy; backend speaks plain HTTP inside the trusted network only.
