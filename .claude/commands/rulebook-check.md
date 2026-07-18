---
description: Validate rulebooks (and run their golden cases) with the RadioPad CLI.
argument-hint: "[path/to/rulebook.yaml]"
allowed-tools: Bash(dotnet run --project cli/RadioPad.Cli:*)
---

Validate clinical rulebooks using the CLI (mirrors the `cli` CI job locally).

- **With a file argument**:
  `dotnet run --project cli/RadioPad.Cli -- rulebook validate $ARGUMENTS`
  Then, if golden fixtures exist under `rulebooks/_tests/<id>/` (where `<id>` is the rulebook's basename), also run:
  `dotnet run --project cli/RadioPad.Cli -- rulebook test $ARGUMENTS --cases rulebooks/_tests/<id>`
- **With no argument**: loop over every `rulebooks/*.yaml` and validate each, exactly as `ci.yml` does, then run all golden cases under `rulebooks/_tests/*`.

Report a per-file pass/fail summary and stop-detail on the first failure.
