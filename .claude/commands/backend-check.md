---
description: Build the backend solution and run tests (optionally filtered to one test for speed).
argument-hint: "[TestNameFilter]"
allowed-tools: Bash(dotnet build:*), Bash(dotnet test:*)
---

Verify the RadioPad backend locally.

- **With an argument** (fast, preferred for a quick check): run only the matching test —
  `dotnet test backend/RadioPad.Api/RadioPad.Api.sln --filter $ARGUMENTS`
- **With no argument**: build then run the full suite —
  `dotnet build backend/RadioPad.Api/RadioPad.Api.sln -c Debug`
  then `dotnet test backend/RadioPad.Api/RadioPad.Api.sln --no-build`

Report build status and test pass/fail counts. On failure, show the failing test name(s) and the assertion excerpt. The full suite is the CI gate (`ci.yml` → backend job); prefer the `--filter` form locally to honour the no-long-builds workflow.
