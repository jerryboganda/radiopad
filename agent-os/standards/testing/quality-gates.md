# RadioPad Testing and Validation

- Every behavior change in Domain, Application, or Validation ships a matching test.
- Integration tests use in-memory SQLite with the documented synthetic tenant and user
  fixtures; never add real patient data or secrets.
- AI gateway tests cover disabled/blocked provider rejection, audit recording, PHI
  computation, and allowed routing under the current operator policy.
- Validate each rulebook YAML and every matching golden-case suite in CI.
- Run only the smallest relevant local test or one Vitest file; CI owns full builds,
  suites, typechecks, lint, packaging, and coverage.
