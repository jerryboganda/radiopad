# iter-32 Agent F — MCP / tool registry hardening (handoff)

## Status: complete. Build green, 11/11 new tests pass, 6/6 iter-31 MCP regression tests still pass.

## Audit-action ints reserved
- `McpToolRegistered = 31`
- `McpToolBlocked = 32`
(slots 29, 30 held by Agent E for SCIM).

## Schema deltas
- `McpTool`: + Version, ScopeString, ManifestJson, ManifestSha256, ManifestSig, Status (Submitted=0/Approved=1/Blocked=2), IsBuiltIn.
- `McpToolCall`: + ToolName, ScopeString, LatencyMs.
- `TenantSettings`: + AllowDangerousMcp (bool, default false).

## Endpoints (all under /api/mcp/tools, RBAC noted)
- GET / (any tenant user)
- GET /{id} (any)
- POST / (ReportingAdmin/MedicalDirector/ItAdmin) — audits McpToolRegistered
- POST /{id}/approve (MedicalDirector/ItAdmin) — audits McpToolApproved
- POST /{id}/block (MedicalDirector/ItAdmin) — audits McpToolBlocked
- POST /{id}/revoke (back-compat alias)
- DELETE /{id} (MedicalDirector/ItAdmin)
- POST /{id}/invoke — scope-policy + status-gate + invocation-service
- POST /{id}/test — sandboxed test (5 s wall, 256 MiB soft cap, BelowNormal priority)

## Default-deny scope policy
- shell:/fs:/net: tokens require BOTH env `RADIOPAD_MCP_ALLOW_DANGEROUS=1` AND `TenantSettings.AllowDangerousMcp=true`.
- Refused 403 body: `{ kind: "mcp_scope_blocked", reason: "mcp_scope", offendingTokens: [...] }`.
- Refusal audits `AuditAction.PolicyViolation` with `reason="mcp_scope"`.

## Files touched
See PROGRESS.md "Iteration 32 — MCP / tool registry hardening (Agent F)" entry.

## P1 follow-ups
1. Update `RadioPadDbContextModelSnapshot.cs` to match the new entity columns before cutting a release.
2. Rotate `mcp-connectors/_signing/release.sec` placeholder seed to an HSM key per the new security-architecture.md "MCP signing" section.
3. Replace in-process sandbox with process-isolated WASM (Wasmtime) — surface stays the same.
4. CLI `mcp serve` registry-unreachable behaviour is default-allow today; make per-tenant configurable in iter-33 (MCP-008).
5. Sibling agent's `Iter32NetworkDefenseTests.cs` references missing `IpAllowlistMiddleware.ResolveRemoteIp` — needs repair by IP-allowlist owner.
