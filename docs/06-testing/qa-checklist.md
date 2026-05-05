# QA Checklist

**Status:** Current  ·  **Owner:** QA  ·  **Last Updated:** 2026-05-04

## Per-PR checklist

- [ ] Unit & integration tests added for the change.
- [ ] `dotnet test` passes locally.
- [ ] `pnpm typecheck` passes locally.
- [ ] No new lint warnings.
- [ ] `PROGRESS.md` updated if a roadmap item moved.
- [ ] `CHANGELOG.md` `[Unreleased]` section updated.
- [ ] No PHI or secrets in fixtures, logs, or screenshots.
- [ ] UI uses only locked tokens & components ([../02-design/design.md](../02-design/design.md)).
- [ ] `.ai-mark` retained on AI text until acknowledge.
- [ ] If touching a human-review file ([../01-ai-agent/human-review-policy.md](../01-ai-agent/human-review-policy.md)), the `human-review-required` label is set.

## Per-release checklist

- [ ] Full test suite passes (`dotnet test` + frontend).
- [ ] Rulebook golden suites pass.
- [ ] Prompt eval safety set 100%; quality bars met.
- [ ] Pen-test outstanding items resolved (no Critical / High open).
- [ ] CHANGELOG `[Unreleased]` rolled into the new version section.
- [ ] Tag created and signed.
- [ ] Release notes link to documentation.
- [ ] Migration plan documented if the release adds DB migrations.
- [ ] Customer comms drafted for breaking changes.

## Per-incident checklist (SEV-1 / SEV-2)

- [ ] Incident commander assigned.
- [ ] Containment action recorded.
- [ ] Audit chain verified for affected tenants.
- [ ] Postmortem written within SLA.
- [ ] Action items tracked to closure.
- [ ] CHANGELOG note under `### Security` if relevant.
