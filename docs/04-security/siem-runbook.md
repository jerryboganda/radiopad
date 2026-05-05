# SIEM runbook

**Status:** Living  **Owner:** Security / SRE  **Last Updated:** 2026-05-04

RadioPad's SIEM forwarder ships every audit-log row to one or more external
sinks (Splunk HEC, Microsoft Sentinel Log Analytics, Elasticsearch `_bulk`,
RFC-5424 Syslog UDP). The push is run by `SiemPushService` from a background
loop; the sinks themselves live in
`backend/RadioPad.Api/src/RadioPad.Application/Services/Siem/Sinks.cs`.

This document explains how to wire a sink to an external endpoint, how to
exercise the new live smoke tests (iter-33 INT-010), and the pass criteria
operators use during disaster-recovery rehearsals.

---

## 1. Endpoint configuration

Each sink reads its endpoint from environment variables. **Unset variables
disable the sink.** None of these variables may be checked into source
control; provision them through the operator's secret manager.

| Sink     | Required env vars                                                                                                  | Optional env vars                                                                  |
| -------- | ------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------- |
| Splunk   | `RADIOPAD_SIEM_SPLUNK_URL` (HEC base URL), `RADIOPAD_SIEM_SPLUNK_TOKEN`                                            | —                                                                                  |
| Sentinel | `RADIOPAD_SIEM_SENTINEL_WORKSPACE_ID`, `RADIOPAD_SIEM_SENTINEL_SHARED_KEY`                                         | `RADIOPAD_SIEM_SENTINEL_LOG_TYPE` (default `RadioPadAudit`)                        |
| Elastic  | `RADIOPAD_SIEM_ELASTIC_URL`                                                                                        | `RADIOPAD_SIEM_ELASTIC_INDEX` (default `radiopad-audit`), `_BEARER` _or_ `_BASIC`  |
| Syslog   | `RADIOPAD_SIEM_SYSLOG_HOST`                                                                                        | `RADIOPAD_SIEM_SYSLOG_PORT` (default `514`)                                        |

PHI minimisation is enforced at the contract layer: only `Id`, `TenantId`,
`UserId`, `ReportId`, action code/name, timestamp, and the audit-chain
`IntegrityHash` are emitted. The audit row's `DetailsJson` (which may carry
provider-routing context) is **never** shipped.

---

## 2. Live smoke tests (iter-33 INT-010)

Unit tests for the four sinks live in
`tests/RadioPad.Api.Tests/Integration/SiemSinkTests.cs` and run on every
build (mocked HTTP / UDP). Live smoke tests against real endpoints live in
`tests/RadioPad.Api.Tests/Iter33/SiemLiveSmokeTests.cs` and are gated by:

```
RADIOPAD_RUN_SIEM_LIVE=1
```

Without this gate, every test in the file is skipped (xUnit reports
`Skipped`, not `Failed`). When the gate is on, each test additionally checks
the per-sink env vars and silently no-ops if they're absent — operators can
opt in to a single sink without provisioning all four.

### Running locally

```powershell
$env:RADIOPAD_RUN_SIEM_LIVE = "1"
$env:RADIOPAD_SIEM_SPLUNK_URL = "https://localhost:8088"
$env:RADIOPAD_SIEM_SPLUNK_TOKEN = "<dev-token>"
dotnet test backend/RadioPad.Api/RadioPad.Api.sln `
  --filter "FullyQualifiedName~SiemLiveSmokeTests"
```

### Pass criteria

| Sink     | Pass criterion                                                                                  |
| -------- | ----------------------------------------------------------------------------------------------- |
| Splunk   | HEC returns 2xx; sink returns without throwing.                                                 |
| Sentinel | Data Collector API returns 2xx; sink returns without throwing.                                  |
| Elastic  | `_bulk` returns 2xx; sink returns without throwing.                                             |
| Syslog   | A non-empty UDP datagram lands on the in-test listener at `127.0.0.1:5514` within 2 s.          |

The synthetic event sent to every sink is **PHI-free**:

- `tenantId = 00000000-0000-0000-0000-00000000beef` (literal — not a real tenant).
- `actionName = "radiopad-iter33-smoke"`.
- `userId / reportId = null`.
- `integrityHash = "0" × 64` (synthetic; not a valid SHA-256).

### Splunk dev container

```powershell
docker run -d --name splunk-dev `
  -p 8000:8000 -p 8088:8088 `
  -e SPLUNK_START_ARGS=--accept-license `
  -e SPLUNK_PASSWORD=changeme `
  -e SPLUNK_HEC_TOKEN=local-dev-token `
  splunk/splunk:9.3
# wait ~60 s for first-run init, then:
$env:RADIOPAD_SIEM_SPLUNK_URL  = "https://localhost:8088"
$env:RADIOPAD_SIEM_SPLUNK_TOKEN = "local-dev-token"
$env:RADIOPAD_RUN_SIEM_LIVE    = "1"
```

The smoke test accepts the container's self-signed cert via
`DangerousAcceptAnyServerCertificateValidator`; this convenience is **only**
applied inside the smoke test file and never in production code.

### Nightly CI run (iter-35)

The workflow [.github/workflows/nightly-live-suites.yml](../../.github/workflows/nightly-live-suites.yml)
runs every day at 06:00 UTC (and on `workflow_dispatch`) and exercises both
the AWS KMS round-trip and the SIEM live smoke against real endpoints.

- Credentials come exclusively from `secrets.*` — never inline.
- A `precheck` job materialises secret-presence flags as job outputs;
  the `aws-kms-live` and `siem-live` jobs each gate on the matching flag.
  When the secret is absent the job is `skipped` (not failed) so forks
  and pre-secret environments stay green.
- The `report-summary` job always runs and emits a Markdown table to the
  GitHub Actions step summary (`$GITHUB_STEP_SUMMARY`) so the nightly
  run is auditable even when sub-jobs were skipped.
- Concurrency is set so a manually-triggered run cancels any in-flight
  scheduled run of the same workflow.

The required GitHub secrets are:

| Suite     | Secrets                                                                                                                                                                                                                                                                                            |
| --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AWS KMS   | `RADIOPAD_AWS_KMS_KEY_ARN`, `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_REGION`                                                                                                                                                                                                              |
| Splunk    | `RADIOPAD_SIEM_SPLUNK_URL`, `RADIOPAD_SIEM_SPLUNK_TOKEN`                                                                                                                                                                                                                                            |
| Sentinel  | `RADIOPAD_SIEM_SENTINEL_WORKSPACE_ID`, `RADIOPAD_SIEM_SENTINEL_SHARED_KEY`, `RADIOPAD_SIEM_SENTINEL_LOG_TYPE` (optional)                                                                                                                                                                             |
| Elastic   | `RADIOPAD_SIEM_ELASTIC_URL`, `RADIOPAD_SIEM_ELASTIC_INDEX` (optional), one of `RADIOPAD_SIEM_ELASTIC_BEARER` / `RADIOPAD_SIEM_ELASTIC_BASIC`                                                                                                                                                         |
| Syslog    | `RADIOPAD_SIEM_SYSLOG_HOST`, `RADIOPAD_SIEM_SYSLOG_PORT` (optional)                                                                                                                                                                                                                                 |

---

## 3. Operational notes

- The forwarder retries with exponential back-off via the underlying `HttpClient` policy chain; failures surface in `/admin/security` as the per-sink `LastError` string.
- The Syslog sink uses RFC-5424 framing with PRI = 134 (local0/info). Operators routing into legacy Cisco/RSA collectors that expect RFC-3164 should run a syslog gateway (rsyslog `mmnormalize`) in front.
- Rotating Splunk HEC tokens / Sentinel shared keys does NOT require a process restart — the sinks read env vars on every push.
- The SIEM forwarder is intentionally fire-and-forget at the audit-log layer: a SIEM outage must not block report sign-off.
