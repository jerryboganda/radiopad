# Task Template

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

Copy this template into a new issue or a chat prompt when handing work to an AI agent.

```md
# Task: <title>

## Objective
<One sentence stating the user-visible outcome.>

## Context
<Why this matters; link the PRD/roadmap/issue.>

## Files to inspect
- <repo-relative path>
- <repo-relative path>

## Files likely to change
- <repo-relative path>

## Constraints
- Strict tech stack: Next.js 16 / ASP.NET Core 8 / Tauri 2 / Capacitor 6 / .NET 8 CLI.
- Locked Open Design tokens & component classes only.
- Tenant isolation via TenantedController.ResolveContextAsync.
- Append-only audit log via IAuditLog.AppendAsync.
- PHI policy untouched.

## Acceptance criteria
- [ ] <criterion>
- [ ] <criterion>

## Required tests
- <test path or behaviour>

## Required docs updates
- [ ] docs/<area>/<doc>.md
- [ ] openapi/openapi.yaml (if API changed)
- [ ] CHANGELOG.md under [Unreleased]
- [ ] PROGRESS.md iteration entry

## Human approval needed?
- [ ] Yes — touches AiGateway / ReportValidator / migrations / signed rulebook / SECURITY.md.
- [ ] No — routine change.
```
