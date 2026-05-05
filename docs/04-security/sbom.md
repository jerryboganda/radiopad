# Software Bill of Materials (SBOM)

**Status:** Draft (manual)  ·  **Owner:** Security  ·  **Last Updated:** 2026-05-04

## Policy

- Every release generates a CycloneDX SBOM (planned: automated via `cyclonedx-dotnet` and `cyclonedx-npm`).
- SBOMs are stored as release assets next to the artefact.
- Customers may request the SBOM under their support contract.

## Inventory snapshot (manual, until automation lands)

### Backend (`backend/RadioPad.Api/`)

Top-level packages (see `*.csproj` for the canonical list):

- Microsoft.AspNetCore.* (8.0.x) — MIT
- Microsoft.EntityFrameworkCore (8.0.10) — MIT
- Microsoft.EntityFrameworkCore.Sqlite (8.0.10) — MIT
- Npgsql.EntityFrameworkCore.PostgreSQL (8.0.8) — PostgreSQL
- YamlDotNet (15.1.6) — MIT
- Swashbuckle.AspNetCore (6.7.0) — MIT
- Microsoft.AspNetCore.Mvc.Testing (8.0.10) — MIT
- xUnit (latest) — Apache 2.0

### Frontend (`frontend/`)

Top-level packages (see `package.json` for the canonical list):

- next (16.x) — MIT
- react / react-dom (18.x) — MIT
- typescript (5.x) — Apache 2.0

### Desktop (`desktop/src-tauri/`)

- tauri (2.x) — Apache 2.0 OR MIT
- tokio — MIT

### Mobile (`mobile/`)

- @capacitor/core (6.x) — MIT

### CLI (`cli/RadioPad.Cli/`)

- System.CommandLine — MIT

## Generation (planned)

```bash
# .NET
dotnet tool install -g CycloneDX
dotnet CycloneDX backend/RadioPad.Api/src/RadioPad.Api -o sbom.dotnet.json

# Node
pnpm dlx @cyclonedx/cyclonedx-npm --output-file sbom.npm.json

# Merge
cyclonedx-cli merge --input-files sbom.dotnet.json sbom.npm.json --output-file sbom.json
```

## License compatibility

- Apache 2.0 / MIT / BSD are accepted.
- GPL / AGPL components require an explicit ADR before adoption.
- Commercial-only components are forbidden in the open-source core.
