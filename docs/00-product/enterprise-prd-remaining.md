# Enterprise PRD Remaining Work

**Status:** Current  ·  **Owner:** Product + Engineering  ·  **Last Updated:** 2026-05-05

This list tracks remaining work after the Enterprise PRD close-out passes. It intentionally separates code gaps from work that cannot be completed without operator credentials, signed vendor contracts, deployment telemetry, or clinical governance sign-off.

## Repo-Actionable Gaps

| Area | Remaining work | Notes |
| --- | --- | --- |
| Clinical rulebooks | Implement or explicitly retire domain-specific resolver ids that are declared in YAML but not yet resolved by `ReportValidator`. | Core generic resolvers ship, including `level_consistency`; remaining examples include TI-RADS, LI-RADS, BI-RADS, Lung-RADS, PI-RADS, organ-coverage, contrast-phase, and joint-specific completeness rules. Unknown rule ids are now no-ops rather than false-positive findings, so golden cases must explicitly cover any resolver that is expected to work. |
| Golden-case breadth | Add conflict and clean fixtures for every approved domain-specific resolver once implemented. | Golden cases are strict: unexpected rule ids fail the case, not only missing expected ids. |
| Migration hygiene | Keep future EF migrations additive and discoverable, with a clean-database smoke test in CI. | The duplicate operations in `SecurityHardening` were removed; future generated migrations should avoid consolidating already-applied schema from older discoverable migrations. |
| Secret handling tests | Add focused integration/unit tests for inline provider secret rejection and env-only PACS secret resolution. | Runtime code now rejects provider inline refs at save time and resolves unsupported refs to no secret. |
| Plugin sandbox parity | Replace the documented macOS no-op plugin sandbox placeholder with a real `sandbox-exec` profile or a notarized helper process. | Windows and Linux wrappers exist; macOS still tags launches as `sandbox=noop` so operators can see the gap. |
| Export UX | Consider a visible “acknowledge before export” disabled state or tooltip on export buttons. | Backend enforces the final-export gate; text preview remains draft-safe for Copy to RIS preview. |

## Externally Gated Launch Work

| Area | Remaining work | External dependency |
| --- | --- | --- |
| OAuth provider token vault | Validate refresh-token storage/rotation with real IdP and provider sandboxes. | Vendor OAuth apps, refresh-token scopes, security review. |
| Availability SLO | Prove 99.9% web/API availability in production. | Operator deployment, observability backend, traffic history. |
| Desktop signing and notarization | Produce signed Windows/macOS/Linux installer artifacts. | Authenticode cert, Apple Developer ID, Linux package signing keys. |
| Mobile store release | Complete Capacitor store packaging, signing, and review. | Apple/Google developer accounts, release credentials, store review. |
| PACS/RIS production pilots | End-to-end validate Sectra, Visage, Carestream, Orthanc bridge, HL7 sendback, and DICOM SR flows. | Hospital/vendor test systems, VPN/network allowlists, test orders. |
| Live provider conformance | Run live smoke tests for cloud AI adapters, KMS providers, billing, webhooks, SIEM sinks, and push notifications. | Operator secrets and paid/live sandbox accounts. |
| Compliance evidence | Finalize HIPAA/BAA, SOC 2 operational controls, disclosure workflow, and clinical governance sign-off. | Legal/security/clinical review outside the repository. |
