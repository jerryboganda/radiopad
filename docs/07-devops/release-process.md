# Release Process

**Status:** Current  ·  **Owner:** Engineering + Ops  ·  **Last Updated:** 2026-05-16

## Cadence

- Minor releases roughly every 4–6 weeks.
- Patch releases as needed (security, correctness).
- Major releases tied to significant changes; coordinated with customers.

## Steps

1. **Cut** — confirm `main` is green; freeze for the cycle.
2. **Bump** — version in `Directory.Build.props`, `package.json`, `cli/.../*.csproj`, `desktop/src-tauri/tauri.conf.json`.
3. **CHANGELOG** — promote `[Unreleased]` to `[X.Y.Z] — YYYY-MM-DD`.
4. **Verify** — full test suite, rulebook goldens, prompt evals (safety 100%).
5. **Tag** — `git tag -s vX.Y.Z`; push.
6. **Build** — release workflow builds and pushes container images, NuGet tools, and desktop installers. Desktop production builds must inject a non-empty Tauri updater public key and operator-supplied signing identities; unsigned internal desktop builds must disable updater artifacts.
7. **Notes** — release notes summarise changes, link CHANGELOG, list breaking changes.
8. **Communicate** — email customers (hosted), advisory on the status page; on-prem customers receive the upgrade runbook.
9. **Deploy staging** — verify with smoke + audit verify.
10. **Deploy prod** — staged rollout if hosted; on-prem ships images.
11. **Monitor** — 24-hour watch with on-call awareness.

## Roles

- **Release Manager** — drives the checklist.
- **QA Lead** — signs off on the test suite.
- **Security Reviewer** — signs off if the release touches PHI policy, audit, or auth.
- **Clinical Reviewer** — signs off if the release changes rulebooks `status: approved`.

## Rollback readiness

- Previous tag image kept in registry for ≥ 90 days.
- Migration plan documented in the release notes.
- Rollback drill must have run within the last quarter.

## Pre-release sign-off checklist

- [ ] All tests green.
- [ ] Safety eval set 100%.
- [ ] No outstanding Critical / High vulnerabilities.
- [ ] CHANGELOG updated.
- [ ] PROGRESS.md current.
- [ ] Release notes drafted.
- [ ] Migration plan documented (if applicable).
- [ ] On-call schedule confirmed for the release window.
- [ ] Desktop updater key, sidecar smoke, and installer-signing status confirmed when a desktop artifact is included.
