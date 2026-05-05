# Infrastructure Architecture

**Status:** Draft  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-04

## Components (hosted SKU target)

- **Edge / TLS:** managed CDN or load balancer with TLS 1.2+ and HSTS.
- **API tier:** stateless containers behind the load balancer, autoscaled by CPU + RPS.
- **Database:** managed PostgreSQL with automatic backups + point-in-time recovery.
- **Object storage (Phase 2):** customer-supplied S3-compatible bucket per tenant.
- **Secret manager:** cloud-native (AWS Secrets Manager / GCP Secret Manager / Azure Key Vault).
- **Observability stack (planned):** Prometheus + Grafana + Loki / customer SIEM.

## Topology

```
Internet
  │
  ▼
[ Load balancer + WAF ]
  │
  ▼
[ API replicas ] ── [ Postgres primary ] ── [ Postgres replicas ]
        │
        └── [ Object storage (Phase 2) ]
        └── [ AI providers (HTTPS egress, allowlisted) ]
```

## Networking

- API has explicit egress allowlist for AI providers + IdP + status endpoints.
- Internal traffic over private network only.
- Backend never accepts requests directly from the internet — always through the load balancer.

## Single-region (today) → multi-region (Phase 3)

- Today: single region, multi-AZ.
- Phase 3: secondary region with async replication; DNS-based failover.

## Capacity planning

- See [capacity-planning.md](capacity-planning.md).

## Tooling

- **Provisioning:** Terraform (planned) under `deploy/terraform/`.
- **Container orchestration:** Docker Compose v0.x; Helm/Kubernetes Phase 2.
- **CI/CD:** GitHub Actions; eventually a release workflow that talks to the cluster.

## Security posture

- WAF rules for common patterns (planned).
- Backend binds 127.0.0.1 by default; container exposes the port only inside the cluster.
- Secrets injected as env vars from the secret manager at startup.
