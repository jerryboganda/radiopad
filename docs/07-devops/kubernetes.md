# Kubernetes

**Status:** Planned (Phase 2)  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-04

## When K8s lands

The hosted SKU. On-prem stays on Docker Compose unless the customer requests otherwise.

## Resources (proposed)

```
deploy/helm/radiopad/
├── Chart.yaml
├── values.yaml
└── templates/
    ├── deployment-api.yaml
    ├── service-api.yaml
    ├── hpa-api.yaml
    ├── ingress.yaml
    ├── configmap.yaml
    ├── secret.yaml          # references only; values injected
    ├── job-migrate.yaml
    └── networkpolicy.yaml
```

## Deployment

- API as a `Deployment` with ≥ 2 replicas.
- HorizontalPodAutoscaler on CPU + custom RPS metric (Phase 3).
- Rolling update strategy with `maxUnavailable: 0`.

## Probes

- **Liveness:** `GET /api/health` → 200.
- **Readiness:** `GET /api/health/ready` → 200 (DB-aware).
- **Startup:** generous; avoids killing pods during EF Core warm-up.

## Migrations

- Pre-deploy `Job` runs `dotnet ef database update`.
- Job uses a Helm hook (`pre-install`, `pre-upgrade`).

## Secrets

- Mounted from cloud secret manager via External Secrets Operator (planned) or sealed secrets.

## Network policy

- Default deny; allow:
  - Ingress from the cluster ingress controller only.
  - Egress to Postgres, AI provider allowlist, IdP.

## Logging / metrics

- stdout/stderr → log aggregator.
- `/metrics` scraped by Prometheus (Phase 2).

## Drain & shutdown

- ASP.NET Core gracefully drains on `SIGTERM`.
- Helm hook for `preStop` adds a 5-second sleep so the load balancer drains first.
