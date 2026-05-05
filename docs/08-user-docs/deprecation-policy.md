# Deprecation Policy

**Status:** Current  ·  **Owner:** Engineering + Product  ·  **Last Updated:** 2026-05-04  ·  **Source of Truth:** [VERSIONING.md](../../VERSIONING.md)

## Principles

- We deprecate before we remove.
- We tell customers, write it down, and respect the runway.
- We never silently change behaviour.

## Process

1. **Announce** — CHANGELOG entry under `### Deprecated` in the release that introduces the deprecation. Include the replacement and the planned removal version.
2. **Surface** — emit a `Deprecation` HTTP header on affected endpoints; CLI prints a one-line warning to stderr.
3. **Document** — update the relevant doc page with a "Deprecated since" note.
4. **Hold** — keep the deprecated surface working for **at least 2 minor versions**.
5. **Remove** — only in a MAJOR version (or after the hold window if there is no MAJOR yet). CHANGELOG entry under `### Removed`.

## What can be deprecated

- HTTP API endpoints / fields / enum values.
- CLI subcommands, flags, output formats.
- Rulebook YAML schema fields.
- Template JSON fields.
- FHIR mapping conventions.
- Audit log payload keys (extra-careful — many integrators rely on stability).

## What cannot be silently changed

- Audit chain hash formula.
- PHI policy semantics.
- Tenant isolation contract.
- `kind` enum values (additions OK; renames require deprecation).

## Communications

- Release notes.
- Customer email for hosted SKU; included in upgrade guide for on-prem.
- Status page entry for surfaces broadly used.

## Exceptions

- Security: a vulnerable surface may be removed faster than the deprecation window. CHANGELOG `### Security` entry with rationale; customers notified directly.
