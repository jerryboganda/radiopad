# Threat Model

**Status:** Draft  ·  **Owner:** Security  ·  **Last Updated:** 2026-05-04

> STRIDE-style analysis of RadioPad. Covers v0.x; revisit at every MAJOR release.

## Assets

| Asset | Sensitivity |
| --- | --- |
| Patient health information (PHI) embedded in `Indication` / `Findings` / `Impression` | **Restricted** |
| Audit log (append-only, SHA-256 chain) | **Confidential — integrity-critical** |
| Provider API keys (env-var only, never in DB) | **Restricted** |
| Tenant configuration (rulebooks, templates, providers) | **Confidential** |
| Source code & rulebook YAML | **Internal** |

## Actors

- Authenticated radiologist (intended).
- Authenticated admin (intended).
- Operator with shell access to the host (semi-trusted).
- Anonymous internet attacker (untrusted).
- Compromised AI provider (untrusted boundary).
- Insider with DB read access (semi-trusted).

## Trust boundaries

1. **Browser ↔ API.** TLS-protected; auth via tenant headers (v0.1) / JWT (Phase 3).
2. **API ↔ DB.** Same network; credentials via env var; least-privilege DB role.
3. **API ↔ AI provider.** Hard boundary; PHI only crosses if compliance class permits.
4. **Tenant A ↔ Tenant B.** Logical boundary in shared DB; enforced by `ResolveContextAsync`.

## STRIDE walkthrough

| Threat | Surface | Mitigation | Residual risk |
| --- | --- | --- | --- |
| **Spoofing** — forge tenant header | API (v0.1) | Upstream identity gateway in production; Phase 3 OIDC closes it | Medium until Phase 3 |
| **Tampering** — modify audit row | DB | SHA-256 chain; `audit verify`; DB user lacks UPDATE/DELETE on `AuditEvents` (planned) | Low if hardened |
| **Repudiation** — radiologist denies signing | API + DB | `ReportAcknowledged` audit with user id + integrity chain | Low |
| **Information disclosure** — PHI to unsafe provider | AI gateway | `EnforcePhiPolicy`; `ProviderBlocked` audit; integration test | Very low |
| **Information disclosure** — log leakage | Logs | Redaction rules; never log bodies | Low |
| **Information disclosure** — cross-tenant read | API | `ResolveContextAsync` + 404 on mismatch | Low |
| **Denial of service** — AI flooding | API | `[EnableRateLimiting("ai")]` 60/min/tenant | Medium without per-IP cap |
| **Elevation of privilege** — escape to tenant admin | API | RBAC (Phase 3); v0.x assumes trusted operator | High in v0.1 hosted, low on-prem |
| **Prompt injection** — malicious finding text manipulates AI provider | AI gateway | System prompt hardening; refusal patterns; safety evals | Medium |

## Mitigations summary

- Strong PHI gate.
- Append-only audit chain with offline verifier.
- Tenant isolation enforced in code and tested.
- Backend binds 127.0.0.1 by default.
- Locked dependency set; weekly SCA review.

## Desktop update signing (DESK-001)

The Tauri desktop shell ships with an embedded **ed25519** public key and the
auto-updater verifies every manifest signature against it before any bytes are
written to disk. See [`desktop/UPDATE_SIGNING.md`](../../desktop/UPDATE_SIGNING.md).

## Plugin trust + capability sandbox (MCP-007, iter-33)

PACS / MCP plugins are an explicit code-execution boundary inside the
radiologist's session. The hardening pipeline is:

1. **Manifest signing chain.** Every plugin ships `manifest.json` plus a
   detached ed25519 `manifest.json.sig` over the canonical-JSON
   serialisation of `manifest.json`. The verifier
   (`PluginManifestSignatureVerifier`) accepts the signature only when it
   validates against an **active** row in the per-tenant
   `TrustedPluginPublisher` table (revoked rows ignored, deny-by-default
   when no key is configured).
2. **Capability allow-list.** The manifest declares a `capabilities[]`
   array; after a successful signature check the host registers the
   `(pluginId, capability)` tuples in `InMemoryMcpCapabilityRegistry`.
   Any tool call whose capability is not registered is refused with
   `PluginPolicyException(reason="capability_not_registered")`.
3. **Per-OS sandbox.** Plugin executables launch only through
   `IPluginSandbox` — `WindowsAppContainerSandbox` (AppContainer SID),
   `LinuxNamespaceSandbox` (`unshare --net --pid --user --map-root-user`),
   or the documented `MacOsNoopSandbox` placeholder. Cross-OS
   instantiation throws `PlatformNotSupportedException` so a misconfigured
   runtime cannot fall through to an unsandboxed launch.
4. **Audit.** Every block path appends one
   `AuditAction.ProviderBlocked{kind=plugin_policy,pluginId,reason}` row
   before `PluginPolicyException` is rethrown — the append-only SHA-256
   chain covers these events.

| Threat | Mitigation | Residual risk |
| --- | --- | --- |
| Plugin binary tampered after signing | Canonical-JSON ed25519 signature; verifier blocks + audits | Low |
| Capability creep at runtime | Deny-by-default `(pluginId, capability)` registry; capability list inside the signed manifest | Low |
| Stolen publisher key | Append-only `RevokedAt`; verifier ignores revoked rows | Low after revocation |
| Plugin filesystem / network escape | AppContainer (Windows) / namespaces (Linux) | Medium on macOS until `sandbox-exec` profile lands |

Full operator runbook: [`desktop/PLUGIN_TRUST.md`](../../desktop/PLUGIN_TRUST.md).

| Threat | Surface | Mitigation | Residual risk |
| --- | --- | --- | --- |
| **Tampering** — attacker substitutes a malicious bundle on the CDN | Update channel | ed25519 signature over the bundle digest; verification done by the client before unpack | Low |
| **Spoofing** — attacker stands up a rogue update endpoint | Network | The endpoint URL is fixed at build time per channel; signature still required, so a rogue host alone cannot push code | Low |
| **Replay** — attacker re-serves an older signed bundle to downgrade past a security fix | Update channel | (a) Updater rejects manifests whose `version` is `≤` the installed version; (b) the manifest digest is bound into the signed payload, so swapping a different signed file at the same URL fails verification; (c) `min_previous_version` field gates large jumps | Low |
| **Key compromise** — signing key leaks | CI / secret store | Key only ever lives in AWS KMS-backed SSM; CI access via GitHub OIDC → decrypt-only IAM role; rotation procedure documented (dual-sign for one minor, retire on the next) | Low |
| **Insider build-time tampering** | CI runner | All signed builds happen in the public `desktop-release.yml` workflow; the workflow file is required-review; signing happens only on `desktop-v*` tags | Medium |

## Open follow-ups


- DB role with no UPDATE/DELETE on `AuditEvents`.
- Per-IP and per-user rate limiting.
- Phase 3 OIDC + RBAC.
- Webhook signature scheme for Phase 2 events.
