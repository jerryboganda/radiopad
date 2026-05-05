# Design Review Checklist

**Status:** Current  ·  **Owner:** Product + Design  ·  **Last Updated:** 2026-05-04

Run this checklist before writing code.

## Product clarity

- [ ] User-visible outcome is described in one sentence.
- [ ] Persona(s) impacted are listed.
- [ ] Linked to a PRD/roadmap entry.

## UX clarity

- [ ] All affected surfaces use only the locked Open Design tokens & component classes.
- [ ] Empty / loading / error states are specified.
- [ ] Validation severities follow the locked palette (Blocker→red, Warning→amber, Info→blue).
- [ ] AI-generated text uses `.ai-mark` until acknowledged.
- [ ] Exactly one `.primary` button per surface.

## Architecture clarity

- [ ] Layer boundaries respected (Domain → Application → Validation → Infrastructure → Api).
- [ ] No new framework or ORM introduced.
- [ ] Tenant isolation handled by `ResolveContextAsync`.

## API clarity

- [ ] Verb / path / request / response / errors are documented.
- [ ] Pagination, rate limit, auth headers, audit action are specified.
- [ ] OpenAPI update planned.

## Data clarity

- [ ] New columns / indexes / constraints listed.
- [ ] Migration is forward-compatible.
- [ ] No breaking change to in-flight reports.

## Security clarity

- [ ] PHI policy boundary unchanged or reviewed.
- [ ] Audit events emitted with stable enums.
- [ ] No secrets in code or fixtures.
- [ ] Threat-model delta noted if new attack surface added.

## Testability

- [ ] Unit + integration tests planned.
- [ ] Rulebook golden cases planned (if clinical).
- [ ] CI updates planned (if new test suite).
