# QA Checklist

**Status:** Current  ·  **Owner:** QA  ·  **Last Updated:** 2026-05-16

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

## Per-desktop-release checklist

- [ ] Tauri updater public key is populated for the target channel or updater artifacts are disabled for an internal unsigned test build.
- [ ] Windows, macOS, and Linux bundles were produced or blockers are documented in the release notes.
- [ ] Signed artifacts pass platform verification (Authenticode on the Windows `.msi` — the only desktop artifact RadioPad ships).
- [ ] App launches and reaches backend `ready` state.
- [ ] Missing sidecar does not crash the app; desktop status banner appears.
- [ ] Sidecar exit triggers controlled restart or final failed state.
- [ ] Pairing flow stores the bearer through the Tauri keyring path.
- [ ] Secure clipboard clears after TTL and does not clear unrelated clipboard content.
- [ ] Offline draft save/read uses encrypted Tauri storage.
- [ ] Idle CPU/GPU remains effectively near zero after readiness.
- [ ] No PHI or secrets appear in desktop logs, installer notes, or workflow output.

## Per-incident checklist (SEV-1 / SEV-2)

- [ ] Incident commander assigned.
- [ ] Containment action recorded.
- [ ] Audit chain verified for affected tenants.
- [ ] Postmortem written within SLA.
- [ ] Action items tracked to closure.
- [ ] CHANGELOG note under `### Security` if relevant.
