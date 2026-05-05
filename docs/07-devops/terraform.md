# Terraform

**Status:** Planned (skeleton)  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-04

> Infrastructure-as-code for the hosted SKU. v0.x ships with Docker Compose; Terraform lands when we operate the hosted environment.

## Layout (proposed)

```
deploy/terraform/
├── modules/
│   ├── network/
│   ├── database/
│   ├── api/
│   ├── observability/
│   └── security/
└── environments/
    ├── staging/
    │   ├── main.tf
    │   ├── variables.tf
    │   └── terraform.tfvars
    └── prod/
        ├── main.tf
        ├── variables.tf
        └── terraform.tfvars
```

## Conventions

- One module per concern; modules pin provider versions.
- State stored in a remote backend (S3 + DynamoDB lock or equivalent).
- Workspaces per environment (`staging`, `prod`).
- Secrets are **not** stored in Terraform state; they live in the secret manager and are referenced by name.

## Key resources

- VPC + subnets + NAT.
- Managed Postgres with automated backups + PITR.
- API service (ECS / GKE / AKS) — stateless containers, autoscaled.
- Load balancer + TLS certificate.
- Secret manager entries (without values).
- Observability stack (managed Prometheus / Grafana or customer-supplied).

## Drift detection

- Nightly `terraform plan -detailed-exitcode` in CI.
- Drift triggers a notification; deliberate manual changes must be merged into Terraform before drift is acceptable.

## Apply policy

- Plan posted to PR; reviewer approval required before apply.
- Production applies require Engineering Lead + Ops sign-off.
- Destructive changes (delete resource, replace database) require an ADR.

## Gaps before this is real

- Choose primary cloud (AWS / GCP / Azure).
- Decide on managed vs self-hosted Postgres.
- Decide on managed vs self-hosted observability stack.
- Define cost budgets per environment.
