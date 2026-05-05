# Post-Market Surveillance Plan

**Status:** Draft  ·  **Owner:** Regulatory + Quality  ·  **Last Updated:** 2026-05-04  ·  **Iteration:** 31

This plan describes how RadioPad monitors quality, safety, and performance after release. It is the v0.x non-SaMD equivalent of the surveillance file required for class-IIa-and-above devices and follows the spirit of MDR Article 83–86 / FDA 21 CFR 820 §820.198.

The companion documents are [iso-14971-risk-register.md](iso-14971-risk-register.md), [vendor-risk-register.md](vendor-risk-register.md), [clinical-evaluation-plan.md](clinical-evaluation-plan.md), and [iec-62304-sdlc.md](iec-62304-sdlc.md).

## 1. Inputs (what we monitor)

| Input | Source | Cadence |
| --- | --- | --- |
| Validation pass rate per rulebook | `AuditEvents` filter `ReportValidated` | Weekly per tenant |
| Hallucination flags per 100 reports | `AuditEvents` filter `ValidationFinding` rule `unsupported_claim` | Weekly per tenant |
| PHI-policy block count | `AuditEvents` filter `ProviderBlocked` | Daily per tenant |
| Provider error rate | `byProvider` aggregation in `/api/usage/summary` | Daily |
| Edit distance after AI generation | `ReportSection.aiVsFinalDistance` + `ai-edit-distance` audit event | Weekly per tenant |
| Customer-reported incidents | Support inbox + GitHub issues (`severity:safety`) | As filed |
| CVE / vulnerability reports | `npm audit` + `dotnet list package --vulnerable` + GHSA feed | Weekly |
| Provider data-residency / compliance changes | Vendor risk register reviews | Quarterly |
| Golden-case regression failures | CI `Run golden cases` step | Per pull request |

## 2. KPIs and thresholds

| KPI | Definition | Threshold | Action when breached |
| --- | --- | --- | --- |
| Validation pass rate | reports with zero blockers / total reports | < 95 % weekly | Open `severity:safety` issue; rulebook owner reviews regressions. |
| Hallucination rate | unsupported-claim flags per 100 reports | > 5 / 100 | Pause affected rulebook to sandbox; rerun golden cases. |
| PHI block growth | week-over-week increase in `ProviderBlocked` | > 50 % | Confirm provider compliance class is correct; alert tenant admin. |
| Provider error rate | non-2xx responses / total provider calls | > 2 % daily | Toggle to fallback provider with equal or higher compliance class (PROV-004). |
| Edit distance (median) | radiologist edit characters / generated characters | > 35 % weekly | Tune prompt blocks; consider regression-testing the rulebook. |
| Critical CVE in dependency | `Critical` or `High` advisory | any | Patch in next minor (`0.x.y`) regardless of release cadence. |
| Provider compliance change | downgrade in `ProviderComplianceClass` for a configured provider | any | Auto-block PHI to the provider; notify tenant admin within 24 h. |
| Golden-case regression in CI | failing golden case on a previously approved rulebook | any | Block merge until rulebook owner triages. |

## 3. Incident response

1. **Triage** — within 4 business hours of report. Assign severity (Critical / High / Medium / Low) using the ISO 14971 risk matrix in [iso-14971-risk-register.md](iso-14971-risk-register.md).
2. **Containment** — for Critical/High, immediately:
   - revoke the offending rulebook (set status `deprecated`),
   - or block the offending provider (set `ProviderComplianceClass = Blocked`),
   - or pull the affected release.
3. **Investigation** — root-cause analysis using the immutable audit chain. Reproducer added to `rulebooks/_tests/<id>/` or backend integration tests.
4. **Notification** — per BAA §6 and EU AI Act + GDPR profile §2.5 (72-hour clock).
5. **Closure** — fix shipped under semver patch; risk register entry updated; CHANGELOG entry filed under `[Security]` / `[Fixed]`.

## 4. Reporting cadence

| Audience | Cadence | Vehicle |
| --- | --- | --- |
| Customer admins | Monthly | Tenant analytics dashboard (`/admin/usage`, `/admin/feature-flags`) + email summary |
| Internal quality review | Quarterly | `docs/_reports/pms-Qx-yyyy.md` |
| Regulatory file | Annually (or upon material change) | Refresh of [traceability-matrix.md](traceability-matrix.md) + [clinical-evaluation-plan.md](clinical-evaluation-plan.md) |

## 5. Plan review

This plan is reviewed:

- whenever the SaMD posture in [samd-classification.md](samd-classification.md) changes;
- whenever a new modality / subspecialty rulebook is shipped;
- annually, regardless of changes.

Reviewers: Regulatory lead, Quality lead, Engineering lead, Medical Director (clinical advisor).
