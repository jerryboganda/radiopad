# ADR-0001: Use Next.js + ASP.NET Core + Tauri + Capacitor + .NET CLI

- **Status:** Accepted (Iteration 1, 2026-05-02)
- **Decision-makers:** Founding engineering team
- **Supersedes:** Legacy Open Design (Node daemon + React/Vite) reference architecture

## Context

RadioPad is the AI-assisted radiology reporting platform that replaces the legacy Open Design playground. It must:

- Run on Windows / macOS / Linux desktops with native packaging.
- Ship a mobile companion for follow-up triage.
- Be operable from a CLI for automation, CI, and headless deployments.
- Run inside hospital networks with strict tenant isolation, audit, and PHI controls.
- Be straightforward for clinical engineering teams to deploy on Postgres + reverse proxy.

## Decision

The strict tech stack is:

| Layer | Technology |
| --- | --- |
| Web   | Next.js 16 (App Router, TypeScript, React 18) — static export so the same bundle is served by the API, Tauri, and Capacitor. |
| Backend | ASP.NET Core 8 + EF Core (SQLite dev / PostgreSQL prod). |
| Desktop | Tauri 2 (Rust shell over the static frontend, OS-native packaging). |
| Mobile  | Capacitor 6 (wraps the same `frontend/out/`). |
| CLI     | .NET 8 global tool (`radiopad`). |

No other frameworks or ORMs are permitted without an ADR superseding this one.

## Consequences

- One language per layer, no JavaScript on the backend, no .NET in the browser.
- Frontend code is shared 1:1 between web / desktop / mobile because we publish a static export.
- All persistence + auth + audit logic lives in ASP.NET Core — there is no second backend (`daemon/` is legacy).
- Every contributor needs both the .NET 8 SDK and pnpm; CI installs both.
- Tauri / Capacitor are thin shells: features that need OS APIs go through Tauri commands (e.g. `secure_copy`).
