# Logging

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Format

- `Microsoft.Extensions.Logging` with `AddSimpleConsole`.
- Structured fields where the .NET API supports them (`logger.LogInformation("Validated {ReportId} ({Findings} findings)", id, count);`).
- Phase 2: switch to `Serilog` + JSON renderer for line-protocol-friendly output.

## Levels

| Level | Use |
| --- | --- |
| `Trace` | Local debugging only; off by default. |
| `Debug` | Verbose handler entry/exit; off in prod. |
| `Information` | Per-request line, lifecycle events. |
| `Warning` | 4xx errors, retried calls, degraded modes. |
| `Error` | 5xx errors, integration failures. |
| `Critical` | Process-impacting failures (DB outage, audit chain mismatch). |

## Correlation

- `RequestCorrelationMiddleware` reads or generates `X-Request-Id`.
- The id is added to the log scope and surfaced in error responses.

## Redaction rules

- Never log:
  - PHI (patient name, MRN, accession number content beyond a hashed prefix).
  - Secrets (provider API keys, JWT bodies).
  - Full report sections.
- Replace with placeholders:
  - `{patient}` → `<redacted>`.
  - Provider keys never reach logs because the secret resolver returns the resolved value to the adapter only.

## Retention

- Self-hosted: customer policy (recommended ≥ 30 days).
- Hosted (Phase 2): 30 days hot, 1 year cold, then deleted.

## What we log per request

- `method`, `path`, `status`, `latencyMs`, `tenantId`, `userId`, `requestId`.
- For AI calls: `provider`, `phiClass`, `tokensIn`, `tokensOut`, `latencyMs`, **never** prompt or completion text.

## Observability roadmap

- Phase 2: OpenTelemetry traces (W3C trace context + `traceparent`).
- Phase 2: per-tenant metrics (Prometheus).
- Phase 3: log aggregation (Loki / Elastic / customer-supplied SIEM).
