# Security Policy

RadioPad processes clinical text and may touch PHI. Security findings are handled with priority.

## Supported versions

| Version | Supported |
| --- | --- |
| `0.x` (pre-GA) | ✅ — patched on `main` |
| Pre-fork Open Design playground | ❌ — superseded |

Once `1.0.0` ships, the latest minor on the latest major and the previous major will receive security fixes for 12 months (see [VERSIONING.md](VERSIONING.md)).

## Reporting a vulnerability

**Do not open a public issue.** Email **security@radiopad.example** (placeholder) with:

- A description and impact assessment.
- Reproduction steps or a proof-of-concept.
- Affected versions / commits.
- Your preferred disclosure timeline.

We acknowledge within **3 business days**, triage within **7 days**, and aim to ship a fix within **30 days** for High/Critical, **90 days** for Medium, best-effort for Low.

## Disclosure process

1. Reporter notifies us privately.
2. We confirm, assign a severity (CVSSv3.1), and create a private fix branch.
3. Fix released; advisory published with credit (opt-in).
4. CVE requested for CVSS ≥ 7.0.

## Out of scope

- Self-inflicted misconfiguration of self-hosted deployments.
- Theoretical attacks against `127.0.0.1`-only dev mode.
- Social-engineering of staff.

## Hard rules contributors must respect

- Never commit secrets — see [docs/04-security/secrets-management.md](docs/04-security/secrets-management.md).
- Never weaken the PHI policy in `AiGateway` — see [docs/04-security/security-architecture.md](docs/04-security/security-architecture.md).
- Audit log is append-only — see [docs/03-architecture/adr/ADR-0003-audit-chain.md](docs/03-architecture/adr/ADR-0003-audit-chain.md).
- Do not include real patient data in any fixture, screenshot, or test.
