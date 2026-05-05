# CLI Design

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04  ·  **Source of Truth:** [../08-user-docs/cli-guide.md](../08-user-docs/cli-guide.md)

## Tool

`radiopad` — .NET 8 global tool built with `System.CommandLine`. Source: `cli/RadioPad.Cli/Program.cs`.

## Commands

```
radiopad
├── login                       # write tenant + user headers to local config
├── daemon
│   └── status                  # check API health + readiness
├── rulebook
│   ├── validate <yaml>         # local YAML schema check
│   ├── test <id>               # run golden cases under rulebooks/_tests/<id>/
│   ├── approve <id> <version>  # POST /api/rulebooks/{id}/approve
│   └── deprecate <id> <version>
├── report
│   ├── list                    # paginated list
│   ├── get <id>
│   ├── validate <id>
│   └── export <id> --format text|fhir
├── generate                    # ask AI for impression/recommendation/technique
├── audit
│   ├── export                  # stream JSON-Lines for the tenant
│   └── verify                  # recompute SHA-256 chain locally
└── provider
    ├── list                    # GET /api/providers
    └── test --id <guid>        # round-trip a smoke-test report
```

## Flags / config

- Global: `--tenant <slug>`, `--user <email>`, `--api <baseUrl>`, `--json`.
- Config file: `~/.config/radiopad/config.json` (Windows: `%AppData%\radiopad\config.json`). Stores tenant/user/api defaults; never stores secrets.

## Auth

- v0.1: writes the tenant + user headers from config or flags.
- Phase 3: `radiopad login` performs OIDC PKCE; refresh token lives in the OS keychain.

## Output formats

- Default: human-friendly tables.
- `--json`: stable JSON for scripting; documented schema per command (Phase 2).
- Errors print to stderr with the request id from the API.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | Generic error (network, parsing) |
| 2 | Validation failure (YAML, golden case) |
| 3 | Audit chain mismatch |
| 4 | Provider blocked by PHI policy |
| 5 | Auth failure |

## Update policy

- New subcommands land under an existing parent and are documented in [../08-user-docs/cli-guide.md](../08-user-docs/cli-guide.md) in the same PR.
- Removing a subcommand requires a deprecation cycle (≥ 2 minor releases).
