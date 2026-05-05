# Containers

**Status:** Current  ·  **Owner:** Ops + Engineering  ·  **Last Updated:** 2026-05-04

## Images

| Image | Source | Base | Notes |
| --- | --- | --- | --- |
| `radiopad/api:<tag>` | `deploy/Dockerfile.api` | `mcr.microsoft.com/dotnet/aspnet:8.0` | Multi-stage; minimal runtime image. |
| Frontend (static) | `pnpm build` | n/a (served by reverse proxy) | Container image not required. |
| `radiopad/cli:<tag>` (planned) | `deploy/Dockerfile.cli` (planned) | `mcr.microsoft.com/dotnet/runtime:8.0` | Operator container. |

## Build

```powershell
docker build -t radiopad/api:dev -f deploy/Dockerfile.api .
```

## Hardening

- Runs as a non-root user.
- Read-only root filesystem (planned).
- No package installs at runtime.
- Health check: `curl -f http://localhost:7457/api/health || exit 1`.
- `STOPSIGNAL SIGTERM`; ASP.NET Core handles graceful shutdown.

## Tagging

- Released images: `vX.Y.Z` and `vX.Y` rolling.
- Dev images: `dev-<git-sha-short>`.
- `latest` is **not** used for production deployments.

## Scanning

- Trivy (planned) in CI on every image build.
- Critical vulnerabilities block the release; high are tracked per [vulnerability management](../04-security/vulnerability-management.md).

## Configuration

- Env-driven; never bake secrets into images.
- Settings file: `appsettings.{Environment}.json` shipped in the image (no secrets).

## Logging

- stdout/stderr only; let the orchestrator collect.
- JSON renderer planned (Phase 2).

## Image inventory in SBOM

- See [../04-security/sbom.md](../04-security/sbom.md). Each release attaches a CycloneDX SBOM.
