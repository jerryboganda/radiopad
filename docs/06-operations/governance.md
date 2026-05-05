**Status:** Active  **Owner:** Governance  **Last Updated:** 2026-05-05

# Governance & model-evaluation surfaces

Iter-36 ships two Enterprise-GA admin dashboards in the Next.js frontend.
Both are **read aggregations** over endpoints that already existed before
iter-36 — the iteration introduced no new backend endpoints.

| Page | Path | Allowed roles |
| --- | --- | --- |
| Governance | `/admin/governance` | Medical Director, Compliance Reviewer, IT Admin |
| Model evaluation | `/admin/model-eval` | Medical Director, Compliance Reviewer (read), Medical Director only for *Promote rulebook* |

Role gating is enforced on the server (controllers + `[Authorize]`-style
filters); the UI only hides the affordances the user cannot exercise.
The shared helpers live in [frontend/lib/roles.ts](../../frontend/lib/roles.ts).

## `/admin/governance` — six-panel dashboard

| # | Panel | Source endpoint(s) | Notes |
| --- | --- | --- | --- |
| 1 | Model inventory | `GET /api/providers` + on-demand `POST /api/providers/{id}/health` | Compliance class, endpoint host, retention label, last health probe. Class colour: 0 Blocked → red, 1 Sandbox → amber, 3/4 PHI/Local → green. |
| 2 | Prompt + rulebook versions | `GET /api/rulebooks`, `GET /api/prompts/overrides` | Rulebook status (Draft/Approved/Deprecated) + prompt-override approval state. |
| 3 | AI usage (last 30 days) | `GET /api/usage/summary` | Cost (USD), request count, average latency, per-provider breakdown. |
| 4 | PHI routing | `GET /api/usage/analytics` + `GET /api/audit?take=500` | `governance.phiPolicyBlocks` from analytics, cross-checked against `ProviderBlocked` (action 5) audit hits. |
| 5 | Validation results | `GET /api/audit?take=500` filtered by `ValidationPackRun` (action 44) | Aggregates `passed/failed` from `detailsJson` and lists the last 20 runs. |
| 6 | Drift alerts | `GET /api/audit?take=500` filtered by `SystemAlert` (40) and `AnomalyDetected` (25) | Last 50 alerts with severity tone. |

The panels are rendered inside `.rp-panel` blocks using only the locked
Open Design tokens / classes. The dashboard does not write data — every
mutation belongs to its originating page (`/providers`, `/rulebooks`,
`/prompts`, `/audit`).

## `/admin/model-eval` — evaluation harness

The harness lets a reviewer run the same prompt across one or more
**sandbox-class** providers and compare per-provider latency and output
length, side-by-side with a golden-case validation run.

Form selectors:

- **Rulebook** — `GET /api/rulebooks`.
- **Golden-case set** — `GET /api/validation-packs?rulebookId=…`.
- **Sample report** — `GET /api/reports?take=25`.
- **Mode** — fixed enum: `impression`, `cleanup`, `draft`, `concise`, `formal`.
- **Providers** — only providers with `compliance == 1` (Sandbox) are
  listed; the backend rejects anything else with HTTP 400 `providers_not_sandbox`.

On *Run evaluation*:

1. If a pack is selected → `POST /api/validation-packs/{id}/run` and the
   pass/fail counts render in the *Golden-case validation* panel.
2. `POST /api/ai/sandbox/compare` with `{ reportId, mode, providerIds }`
   and the per-provider runs render in the *Per-provider comparison* table.

Promotion: *Promote rulebook* calls `POST /api/rulebooks/{id}/approve`
(the existing rulebook approval flow, which writes a
`RulebookApproved` audit event). The button is hidden for any role
other than Medical Director and replaced by an informational banner.

## Audit & PHI guarantees (unchanged)

These pages do not weaken any of the existing safety boundaries:

- The append-only audit chain (SHA-256 over `id|tenantId|action|details|prevHash`)
  is read, never mutated.
- The PHI policy lives in `AiGateway.EnforcePhiPolicy`. `model-eval`
  cannot bypass it — sandbox-compare is restricted to providers whose
  compliance class is `Sandbox`.
- Tenant isolation is enforced server-side via
  `TenantedController.ResolveContextAsync`; both pages render only
  data scoped to the caller's tenant.

## Tests

- [frontend/\_\_tests\_\_/admin/governanceDashboard.test.tsx](../../frontend/__tests__/admin/governanceDashboard.test.tsx) — verifies all six panels render for a Medical Director, and the forbidden banner for a Radiologist.
- [frontend/\_\_tests\_\_/admin/modelEval.test.tsx](../../frontend/__tests__/admin/modelEval.test.tsx) — verifies form payload, results table, role gating on *Promote rulebook*.
