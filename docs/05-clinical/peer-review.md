# Peer Review & Quality (PRD §14.13, PR-001…PR-010)

RADPEER-aligned second reads of signed reports. This is a **quality benchmark,
not a clinical decision system**: a peer review never changes a report, never
signs or unsigns one, and never blocks a workflow. It records what a second
radiologist would have said, so the practice can see its own inter-reader
agreement over time.

## Concepts

### Score — RADPEER 1…4

The numeric score is what the backend stores and benchmarks on; the UI always
shows the plain-English label beside it (`frontend/lib/peerReview.ts` is the one
place both are defined).

| Value | Enum | Label shown to the reviewer |
| --- | --- | --- |
| 0 | `NotScored` | *(sentinel — the review is still open; never displayed)* |
| 1 | `Concur` | I agree with the original read |
| 2 | `DiscrepancyUnlikelySignificant` | Minor difference, unlikely to matter |
| 3 | `DiscrepancyShouldBeMadeMostOfTheTime` | Should have been caught most of the time |
| 4 | `DiscrepancyShouldBeMadeAlmostEveryTime` | Should have been caught almost every time |

`NotScored` exists so an `Assigned` row is never mistaken for a recorded
"concur" in the concordance maths.

### Complexity — the RADPEER difficulty modifier

`Routine` / `Complex` (RADPEER "a"/"b"). Recorded with the score so a
discrepancy on a genuinely hard study is not benchmarked against a routine one.

### Discrepancy category — structured rationale (PR-003, PR-009)

`None` · `Perceptual` (there but not seen) · `Interpretive` (seen, characterised
differently) · `Communication` (right finding, report did not convey it) ·
`Technique` (acquisition limited the read).

**Consistency rule, enforced server-side and mirrored in the form:** a score of
1 must carry `None`; a score of 2–4 must carry a real category. The client
disables Submit rather than round-tripping to a 400, but the backend is the
authority.

### Review type (PR-001)

`Random` (the sampling sweep) · `Targeted` (deliberate selection: STAT,
high-risk, complaint follow-up) · `Consensus` (second read sought before close) ·
`Addendum` (triggered by an addendum to a signed report).

### Status

`Assigned` → `InProgress` (reviewer opened it) → `Completed` (score submitted) →
optionally `Disputed` (the original author contests it; a director adjudicates
out of band — disputing does not retract the score).

## The two invariants

### 1. No self-review

A radiologist can never be assigned a report they authored **or signed in any
capacity** (primary, co-signer, addendum). Enforced in `PeerReviewController`
for both single assignment (`400 { kind: "peer_review_self" }`) and the random
sampler, which filters the reviewer pool per candidate report and skips a report
outright when nobody is eligible (reported as `skippedNoEligibleReviewer`).

The "reader under review" is the report's **primary signer** when there is one,
falling back to `Report.CreatedByUserId`.

### 2. Blinding (PR-002)

`PeerReview.Blinded` defaults to true. While a blinded review is still open
**and the caller is the reviewer**, the API projection omits
`originalAuthorUserId` and `originalAuthorName` **entirely** — they are not
blanked, nulled, or replaced with a resolvable placeholder — and sets
`authorHidden: true`. Blinding lifts the moment the reviewer submits.

Blinding protects the reviewer's judgement from the author's identity. It does
**not** hide the author from themselves, nor from a programme administrator who
has to read the concordance dashboard — both see the row unblinded.

The column is always populated in the database: PR-009 analytics cannot compute
per-reader concordance without it.

## RBAC

Three permissions (`RolePermissionMap` / `PermissionCatalog`):

| Key | Grants | Held by |
| --- | --- | --- |
| `peer_review.read` | My queue, feedback on my own reports, reviews on a report I am party to | Radiologist, Subspecialist, Fellow, Resident, MedicalDirector, ReportingAdmin, ComplianceReviewer, ItAdmin, Auditor |
| `peer_review.submit` | Score an assigned review; dispute a score of my own report | Radiologist, Subspecialist, Fellow, MedicalDirector |
| `peer_review.manage` | Assign, random-sample, read the concordance dashboard | MedicalDirector, ReportingAdmin |

Rationale for the sharp edges:

- **Residents hold Read but not Submit** (PR-008): a resident reads the
  attending feedback recorded against their own drafts, but does not peer-review
  a colleague. Fellows *do* hold Submit — a senior trainee may score a resident
  draft in educational mode.
- **ReportingAdmin holds Manage but not Submit**: they run the programme
  (roster, cadence, dashboard); scoring is a clinical judgement reserved for
  attendings.
- **Radiologists never hold Manage**: the dashboard is the one view that names
  readers next to their discrepancy rate.

## Sampling (PR-001)

`POST /api/peer-reviews/sample` selects from reports **signed inside the window**
(default: last 30 days) that are not already in the peer-review queue, shuffles
them, and round-robins across the eligible reviewer pool.

- Size: explicit `count`, else `ratePercent` of the eligible pool, else the
  default **5 %** (ACR-style). Hard-capped at 200 per sweep so a mis-typed rate
  cannot flood the queue.
- Reviewer pool: explicit `reviewerUserIds`, else every active tenant user whose
  role holds `peer_review.submit`.

## Audit

Every state change appends to the append-only log via `IAuditLog.AppendAsync`:

| Action | Written when | Details recorded |
| --- | --- | --- |
| `PeerReviewAssigned` (80) | Single assignment or each row of a sampling sweep | review id, reviewer id, review type, blinded flag |
| `PeerReviewSubmitted` (81) | Score submitted | review id, score, complexity, discrepancy category, review type |
| `PeerReviewDisputed` (82) | Author disputes a completed score | review id, score |

The reviewer's free-text rationale is **never** written to the audit log — it may
quote clinical narrative. It lives in `PeerReview.Comments` inside the tenant.

## Data

`PeerReview` (`Domain/Entities/Entities.cs`), `DbSet<PeerReview> PeerReviews`,
migration `20260720150000_AddPeerReview`. Indexes:

- `(TenantId, ReviewerUserId, Status)` — the reviewer's queue.
- `(TenantId, ReportId)` — the per-report panel and the sampler's
  already-queued guard.

Every query in the controller filters on the resolved tenant; there is no
cross-tenant read path.

## UI

- `/peer-review` (desktop surface, nav key `nav.peerReview`) — queue, scoring
  form, completed history, PR-008 feedback on my own reports, and the PR-005
  concordance dashboard gated on `peer_review.manage` via `usePermissions()`.
- `/quality` — compact summary panel (open count, concordance rate,
  discrepancies) linking through. This replaced the former "coming soon"
  placeholder.

Both render inside `<AppShell>` with `<Container>` + `<PageHeader>`, use
`Skeleton`/`EmptyState`/`ErrorState` for the three data states, and take all
colour from documented tokens and `.badge` tone classes.

## Not yet built

PR-004 (discrepancies feeding rulebook improvement suggestions), PR-006
(automatic second-read routing for STAT/high-risk findings — the `Consensus`
review type exists but nothing triggers it automatically), and PR-010
(export-only mode for external peer-review services) are unimplemented.
