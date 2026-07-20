# Teaching File & Education Module (PRD §14.14, TF-001…TF-008)

A per-tenant library of de-identified teaching cases, authored one click at a
time from real reports.

API surface:
[docs/03-architecture/api-reference.md](../03-architecture/api-reference.md#teaching-file-prd-1414-tf-001tf-008).

## Why the de-identification design looks like this

A teaching file is the one place in RadioPad where clinical narrative is
*deliberately* shared beyond the person who wrote it. That makes it the highest-
consequence de-identification surface in the product: a leak here is not a stray
log line, it is a patient's history circulated to a whole department and possibly
onward.

So the module does not rely on a single control. It uses three, each of which
would have to fail independently:

1. **The schema cannot hold an identifier.** `TeachingCase` has no accession
   number, no patient reference, no MRN, no name, no date of birth. There is
   nowhere to put one. This is the control that survives a bug in the other two.
2. **Every text field is scrubbed on the way in** — including the fields the
   client supplies directly, not just the ones copied from a report. There is no
   DTO shaped like "trust me, this is already clean".
3. **The result is re-checked before it is saved.** `create-from-report` asserts
   the post-condition and returns `500 { kind: "deidentification_failed" }`
   rather than persisting a case it cannot prove is clean. Refusing to save is
   the correct failure mode; saving a "probably fine" case is not.

## The scrubber

`RadioPad.Application/Teaching/TeachingCaseDeidentifier`.

It is deliberately **separate** from `PhiRedactor` (which masks log lines),
because the two have opposite failure costs. A log line can be destroyed at no
cost, so `PhiRedactor` is maximally aggressive. A teaching case must stay
*clinically readable* — "45-year-old male, 3-day history of RLQ pain" and
"disc protrusion at L4-L5" are the teaching value, and a scrubber that eats them
has quietly defeated the purpose of the module while appearing to work.

`TeachingCaseDeidentifier` therefore layers:

| Stage | What it removes |
| --- | --- |
| 1. Literals | The accession number and patient reference of the source study, verbatim and case-insensitively. Longest first, so `ACC-2026-0031` goes whole rather than leaving `-0031`. |
| 2. Labelled identifiers | `MRN:`, `Accession Number:`, `DOB:`, `Patient Name:`, `SSN:`, `Study UID:` … through to the end of the field. |
| 3. Dates | Textual (`January 5, 2024`, `Mar 2023`) then numeric (`05/01/2024`, `2024-01-05`, `5.1.2024`). |
| 4. Honorific names | `Dr. Alan Grant`, `Prof Smith` (bounded to three trailing name words). |
| 5. Accession-shaped tokens | A 1–4 letter prefix followed by **four or more** digits. The four-digit floor is what keeps `L4-L5`, `T12`, and `C1` intact. |
| 6. `PhiRedactor` backstop | `Patient: Jane Doe`, SSNs, long digit runs. Running it last keeps the two scrubbers in sync — a pattern added there is enforced here automatically. |

Redacted spans become the visible marker `[de-identified]`. Visible on purpose:
a reader must be able to tell that something was removed rather than silently
reading a mutilated sentence.

`ContainsAny` is the post-condition helper the controller uses. It ignores
values shorter than four characters, because a three-letter "identifier" like
`CT` is far more likely to be an ordinary word.

### Known limits

- Free-text patient names with **no** label and **no** honorific
  ("saw Bob again today") are not detected. Structured fields and the labelled
  forms cover the realistic report shapes; this is a residual risk, not a solved
  problem.
- Ages ≥ 90 are not banded to "90 or older" (HIPAA Safe Harbor §164.514(b)(2)(i)(C)).
  Deliberately out of this slice — call it out before this module is used for
  any release of data outside the tenant.
- Pixel-level DICOM de-identification (TF-003) is not implemented; the module is
  text-only today.

## Visibility & permissions (TF-007)

| | |
| --- | --- |
| `Private` (default) | Author only. A non-author fetching it gets `404`, never `403` — the response must not confirm the id exists. |
| `Tenant` | Everyone in the tenant. |

Nothing in the model can widen a case beyond its owning tenant. The PRD's
"opt-in public sharing with attribution" half of TF-007 is **not** implemented.

Permissions: `teaching_cases.read` (browse) and `teaching_cases.manage`
(author / publish / delete). Ownership is checked **separately** from the
permission: holding `manage` lets you author your own cases, not edit or delete a
colleague's. Only a **library admin** — `ItAdmin`, `MedicalDirector`,
`ReportingAdmin` — may moderate a case they do not own.

Roles granted read + manage: Radiologist, Subspecialist, Resident, Fellow,
ReportingAdmin, MedicalDirector, ItAdmin. Read only: ComplianceReviewer
(oversight), Researcher (the de-identified corpus is exactly their remit),
Auditor.

## UI

- **`/teaching`** — browse and search (`frontend/app/(desktop)/teaching/page.tsx`).
  Filtering is server-side: the library grows without bound, and the visibility
  rule lives on the server, so a client-side filter would be both wrong and
  unsafe.
- **`/teaching/view?id=…`** — case detail. Query-param routing, matching
  `reportHref` / `rulebookHref`, so `output: 'export'` produces one page rather
  than one per case.
- **`SaveAsTeachingCaseButton`** (`frontend/components/teaching/`) — the
  TF-001 affordance. It states what will be stripped **before** the clinician
  commits, and the notice is not dismissible: consent to share is only
  meaningful if you know what you are sharing.

Desktop surface only. Cases are authored from reports, and reports live in the
reporting product.

## Tests

- `backend/.../tests/RadioPad.Api.Tests/Services/TeachingCaseDeidentifierTests.cs`
  — both halves of the contract: identifiers removed, clinical content preserved.
- `backend/.../tests/RadioPad.Api.Tests/Integration/TeachingCasesTests.cs`
  — PHI stripped from the **persisted row** (not just the response), search
  filters, private-case invisibility, cross-tenant isolation, delete permission.
- `frontend/__tests__/teachingLibrary.test.tsx` — browse filters reach the
  server; the save button discloses before it saves.

Run: `dotnet test --filter Teaching`, `pnpm vitest run __tests__/teachingLibrary.test.tsx`.
