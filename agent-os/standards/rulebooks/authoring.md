# RadioPad Rulebook Authoring

- Keep `rulebook_id` stable and `snake_case`.
- Increment the semantic version for every published rulebook change.
- Preserve the lifecycle `Draft -> InReview -> Approved -> Deprecated`.
- Every approved rulebook has at least one passing golden case under
  `rulebooks/_tests/<rulebook_id>/`.
- Golden fixtures contain synthetic `report` and `expectFlagged` data only; never use PHI.
- Clinical rule changes require human review of `ReportValidator` behavior and its matching
  tests.
