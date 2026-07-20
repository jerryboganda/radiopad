# Critical results — closed-loop communication (PRD §14.15, CR-001…CR-010)

PowerScribe-style tracking of critical findings from "logged" through
"communicated to the ordering clinician" to "acknowledged" and "closed".

The point of this module is the **closed loop**, not the notification. RadioPad
does not page anyone, does not send the message, and does not acknowledge on a
clinician's behalf. It records what a human did, when, and to whom — so that the
loop is provable months later.

## Criticality classes and deadlines

The criticality class is the only input to the deadline. `dueAt` is stamped at
creation as `createdAt + window` and never recomputed, so shortening a class
later cannot retroactively make an old result "late".

| Class | Meaning | Window | UI badge tone |
| --- | --- | ---: | --- |
| `Red` | Immediate / life-threatening | 15 min | red (`danger`) |
| `Orange` | Urgent | 1 h | amber (`warn`) |
| `Yellow` | Actionable | 24 h | blue (`info`) |

Tones follow the documented severity map (Blocker→red, Warning→amber,
Info→blue) and are never hue-only: every badge carries its label text.

The window lives in one place — `CriticalResult.DeadlineFor(Criticality)` in
`RadioPad.Domain/Entities/Entities.cs`. Change it there and the API, the queue,
and the sweep all follow.

## Lifecycle

```
Open ──communicate──► Communicated ──acknowledge──► Acknowledged ──close──► Closed
 │                          │                              │                  ▲
 └──escalate───► Escalated ─┴──────────────────────────────┴──────────────────┘
      (manual, or overdue sweep)
```

- **Open** — logged, nobody told yet. This is what the deadline is counting down.
- **Communicated** — a human recorded who they told and how
  (`Phone` / `SecureMessage` / `InPerson` / `Other`).
- **Acknowledged** — the receiver's read-back was captured. Rejected with
  `409 not_communicated` if no communication was recorded first: an
  acknowledgement with no call is not a closed loop.
- **Escalated** — the loop was still open when the deadline lapsed. Set manually,
  or by the overdue sweep. It is a flag for a human, never a resolution.
- **Closed** — terminal.

### "Overdue" means

Loop still open (`Open` or `Escalated`) **and** `dueAt` has passed. A result that
was communicated late is no longer chased by the queue — it has already been
recorded, and the lateness lives in the audit trail. The server computes
`isOverdue` so the queue, the panel, and the API filter can never disagree.

## Overdue sweep

`RadioPad.Api/Services/CriticalResultEscalationService.cs` — a `BackgroundService`
on a 60 s cadence (same shape as `AnomalyDetector`). It flips `Open` results past
`dueAt` to `Escalated` and audits with `reason: "overdue_sweep"`. It deliberately
does **not** touch results that were already communicated. `ScanOnceAsync` is
public so tests drive a pass deterministically instead of waiting on the timer.

## Permissions

| Permission | Held by | Covers |
| --- | --- | --- |
| `critical_results.read` | Radiologist, Resident, Fellow, Subspecialist, MedicalDirector, ReportingAdmin, ComplianceReviewer, ItAdmin, Auditor | The radiologist queue and the compliance list. |
| `critical_results.manage` | Radiologist, Resident, Fellow, Subspecialist, MedicalDirector | Log, communicate, acknowledge, escalate, close. |

Communicating a critical finding is a clinical act, so the oversight roles
(`ReportingAdmin`, `ComplianceReviewer`, `ItAdmin`, `Auditor`) get read only.
Trainees (`Resident`, `Fellow`) **do** hold manage — calling a critical result
through is core on-call duty, and it is a communication record, not a report
signature. This is independent of `reports.sign`.

## Safety boundaries honoured

- **Nothing is automatic.** Every transition is an explicit clinician action. The
  sweep only raises a flag; it never communicates, acknowledges, or closes.
- **Append-only audit.** Each transition writes one `AuditEvent` via
  `IAuditLog.AppendAsync` — `CriticalResultCreated` / `Communicated` /
  `Acknowledged` / `Escalated` / `Closed`.
- **No narrative in the audit log.** Details carry ids, criticality, method,
  recipient label, and timings. `findingSummary` is never written to audit.
- **Tenant isolation.** Every query filters on the resolved tenant. A
  cross-tenant id returns `404`, never `403` — a 403 would confirm the row exists.

## Where it surfaces

| Surface | Path |
| --- | --- |
| Report editor panel | `frontend/components/critical/CriticalResultPanel.tsx` |
| Radiologist queue (desktop) | `frontend/app/(desktop)/critical-results/page.tsx` |
| API client | `api.criticalResults.*` in `frontend/lib/api.ts` |
| Controller | `backend/RadioPad.Api/src/RadioPad.Api/Controllers/CriticalResultsController.cs` |

API contract: [docs/03-architecture/api-reference.md](../03-architecture/api-reference.md#critical-results-prd-1415-cr-001cr-010).

## Tests

- Backend: `backend/RadioPad.Api/tests/RadioPad.Api.Tests/Integration/CriticalResultsTests.cs`
  — full loop, deadline derivation per class, tenant isolation, overdue sweep
  (including that it leaves communicated results alone).
- Frontend: `frontend/__tests__/criticalResultPanel.test.tsx` — badge tone
  mapping, countdown, explicit create/communicate/acknowledge calls, and that a
  read-only user sees no mutating controls.
