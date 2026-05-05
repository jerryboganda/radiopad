# NIST SSDF Mapping

**Status:** Draft  ·  **Owner:** Security  ·  **Last Updated:** 2026-05-04

> Mapping our practices to [NIST SP 800-218 SSDF v1.1](https://csrc.nist.gov/pubs/sp/800/218/final). Items are tagged `Implemented / Partial / Missing` with evidence.

## PO — Prepare the Organization

| Practice | Status | Evidence |
| --- | --- | --- |
| PO.1 Define security requirements | Implemented | [security-architecture.md](security-architecture.md), this file. |
| PO.2 Implement roles & responsibilities | Partial | [../01-ai-agent/human-review-policy.md](../01-ai-agent/human-review-policy.md); formal RACI pending. |
| PO.3 Implement supporting toolchains | Partial | CI builds + tests; SCA via `pnpm audit` and `dotnet list package`. |
| PO.4 Define security checks | Implemented | [owasp-asvs-checklist.md](owasp-asvs-checklist.md), CI gates. |
| PO.5 Implement & maintain secure environments | Partial | 127.0.0.1 binding default; container hardening pending. |

## PS — Protect the Software

| Practice | Status | Evidence |
| --- | --- | --- |
| PS.1 Protect all forms of code | Partial | Branch protection on `main`; signed commits not yet required. |
| PS.2 Provide a mechanism to verify integrity | Partial | Container digests; SBOM (planned). |
| PS.3 Archive & protect each release | Missing | Release artefact retention policy pending. |

## PW — Produce Well-Secured Software

| Practice | Status | Evidence |
| --- | --- | --- |
| PW.1 Design software to meet requirements | Implemented | ADRs, architecture docs. |
| PW.2 Review the software design | Implemented | [../01-ai-agent/design-review-checklist.md](../01-ai-agent/design-review-checklist.md). |
| PW.4 Reuse existing well-secured software | Implemented | Microsoft.* libraries; vetted YamlDotNet. |
| PW.5 Create source code | Implemented | [secure-coding.md](secure-coding.md). |
| PW.6 Configure compilation, build, package | Implemented | `Directory.Build.props`; CI workflows. |
| PW.7 Review/analyze human-readable code | Implemented | PR review + `human-review-required` label. |
| PW.8 Test executable code | Implemented | xUnit + integration + golden suites. |
| PW.9 Configure software defaults | Implemented | Backend binds 127.0.0.1; PHI policy default deny. |

## RV — Respond to Vulnerabilities

| Practice | Status | Evidence |
| --- | --- | --- |
| RV.1 Identify & confirm vulnerabilities | Partial | Disclosure email + advisory process. |
| RV.2 Assess, prioritise, and remediate | Partial | Severity SLAs in [SECURITY.md](../../SECURITY.md). |
| RV.3 Analyze vulnerabilities to identify root causes | Partial | Postmortem template; SBOM ingestion pending. |

## Gaps to close before SOC 2 Type I

- Formal vulnerability management policy.
- Required signed commits + tag signing.
- Centralised SCA dashboard.
- Documented release artefact retention.
- Automated SBOM generation per release.
