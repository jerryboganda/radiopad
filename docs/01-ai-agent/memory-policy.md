# Memory Policy

**Status:** Current  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-04

Applies to long-lived AI agent memories (Copilot persistent memory, Claude `CLAUDE.md`, Cursor rules, OmO `/memories/`, etc.).

## Agents may remember

- Repo conventions, build commands, lint commands.
- Architectural decisions referenced in committed ADRs.
- Pitfalls that recur across sessions (e.g. PowerShell `${var}:` escape rule).
- Public-product information that is already in `docs/` or `README.md`.

## Agents must NOT remember

- Real patient data, names, MRNs, accession numbers from production tenants.
- Provider API keys or any secret-shaped strings.
- Customer tenant slugs, internal email addresses, or pricing negotiated under NDA.
- Unpatched vulnerability details — keep these in the security tracker only.
- Anything from `.env`, `appsettings.Production.json`, or files containing `<REDACTED_SECRET>`.

## Sensitive data restrictions

- An agent that observes data fitting the *must NOT remember* list must **redact** the value before writing to memory and **alert** the operator.
- Repository memory (`/memories/repo/`) is preferred for codebase facts; user memory is for cross-project preferences only.

## Update process

1. Notice a recurring fact across sessions.
2. Confirm it is non-sensitive.
3. Add a concise bullet (1 line) under the matching topic file.
4. Remove or correct outdated bullets in the same edit.
5. Reference the canonical doc URL or repo path so the memory remains verifiable.

## Cleanup

- Quarterly review of all entries.
- Delete outdated entries; supersede with the new fact.
- Never let memory become a parallel source of truth — `docs/` always wins.
