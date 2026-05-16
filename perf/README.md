# RadioPad performance smoke tests (k6)

PRD §21 / PERF-004. These scripts exercise the four hottest backend
endpoints and assert their P95/P99 latency SLOs:

| Script | Endpoint | SLO |
| --- | --- | --- |
| `scripts/ai-draft.js` | `POST /api/reports/{id}/ai` | P95 < 10s |
| `scripts/impression.js` | `POST /api/reports/{id}/ai` (impression) | P95 < 5s |
| `scripts/validate.js` | `POST /api/reports/{id}/validate` | P95 < 3s |
| `scripts/audit-write.js` | create + `GET /api/audit/search` | P99 < 500ms |

## Run locally

Start the backend with the mock AI provider so tests don't hit external
endpoints:

```powershell
$env:RADIOPAD_AI_PROVIDER = 'Mock'
dotnet run --project backend/RadioPad.Api/src/RadioPad.Api
```

Then in another shell:

```powershell
k6 run --vus 10 --duration 60s perf/k6/scripts/ai-draft.js
k6 run --vus 10 --duration 60s perf/k6/scripts/impression.js
k6 run --vus 10 --duration 60s perf/k6/scripts/validate.js
k6 run --vus 10 --duration 60s perf/k6/scripts/audit-write.js
```

Override the target via env vars:

| Var | Default | Purpose |
| --- | --- | --- |
| `RADIOPAD_BASE_URL` | `http://127.0.0.1:7457` | Backend base URL |
| `RADIOPAD_TENANT` | `it` | Tenant slug |
| `RADIOPAD_USER` | `it-radiologist@radiopad.local` | User email |
| `K6_VUS` | `10` | VUs per scenario |
| `K6_DURATION` | `60s` | Scenario duration |

## Run against staging

Use a service-account user with read+write on a non-PHI tenant. **Never**
load real patient data — k6 writes synthetic accession numbers prefixed
with `K6-`, `K6IMP-`, `K6VAL-`, `K6AUD-` which can be filtered out of
analytics.

```bash
RADIOPAD_BASE_URL=https://staging.radiopad.com \
RADIOPAD_TENANT=staging \
RADIOPAD_USER=perf@radiopad.local \
k6 run --vus 5 --duration 30s perf/k6/scripts/validate.js
```

## CI

`.github/workflows/perf-smoke.yml` runs all four scripts at low VUs
(3 VUs / 30s) on `workflow_dispatch`. The workflow fails when any
threshold is violated.

## Safety

- No PHI in any synthetic payload.
- No bearer tokens are written to k6 summary output.
- Mock provider only — never a real LLM in CI.
