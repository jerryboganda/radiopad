# MCP Servers

**Status:** Draft (none in production)  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

RadioPad does not currently ship an MCP (Model Context Protocol) server in production. This document lists the development-time MCP servers used by maintainers and the policies that govern them.

## Dev-time MCP servers in use

| Server | Purpose | Auth | Capabilities |
| --- | --- | --- | --- |
| Pylance MCP | Python tooling for ad-hoc data scripts | Local | Read-only file inspection. |
| GitHub MCP | Issue/PR management | PAT (per developer) | Issues, PRs, repo metadata. |
| Search/index MCPs | Local code search | Local | Read-only. |

Note: presence of an MCP server in a developer's editor configuration is **not** a production capability of RadioPad.

## Policy for adding a new MCP server (production)

1. **Justify** the capability in an ADR.
2. **Constrain** scopes — only the minimum read/write needed.
3. **Authenticate** with a per-tenant token; never share across tenants.
4. **Audit** every call with an `AuditAction` value (we will likely introduce `McpCall` when the first server ships).
5. **Sandbox** in dev/staging before any PHI-bearing tenant.

## Setup

- For dev, follow the editor's MCP guide.
- Tokens are stored in the OS keychain or a `.env.local` file ignored by git.
- No tokens may appear in shared docs.

## Out-of-scope (today)

- Customer-installed MCP servers.
- MCP servers that proxy clinical data — these need a full security review and BAA before consideration.
