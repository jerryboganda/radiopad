# Versioning Policy

RadioPad follows [Semantic Versioning 2.0.0](https://semver.org/).

## Public surface

The "public API" for SemVer purposes is:

1. HTTP API documented in [openapi/openapi.yaml](openapi/openapi.yaml) and [docs/03-architecture/api-reference.md](docs/03-architecture/api-reference.md).
2. CLI commands and flags in [docs/08-user-docs/cli-guide.md](docs/08-user-docs/cli-guide.md).
3. Rulebook YAML schema and report-template JSON schema.
4. FHIR `DiagnosticReport` export contract.
5. Audit event schema and SHA-256 chain definition.

Internal types, EF migrations, and design tokens are **not** part of the public surface but are versioned in lockstep.

## Bump rules

| Change | Bump |
| --- | --- |
| Backwards-incompatible HTTP/CLI/rulebook/template change | **MAJOR** |
| New endpoint, command, rulebook, template, optional field | **MINOR** |
| Bug fix, doc, internal refactor, additive optional response field | **PATCH** |
| Pre-1.0 (`0.x.y`) | breaking changes allowed in MINOR; document under `### Changed` |

## Deprecation

1. Mark the deprecated surface in code (`[Obsolete]` / `@deprecated`) and in `docs/08-user-docs/deprecation-policy.md`.
2. Emit a deprecation warning in API responses (`Warning` header) or CLI stderr.
3. Keep the surface available for ≥ 2 minor releases (≥ 6 months at our cadence) before removal.
4. Removal lands in a MAJOR bump with a migration note in [CHANGELOG.md](CHANGELOG.md).

## Release branches

- `main` is always releasable.
- Tags use the form `vMAJOR.MINOR.PATCH` (e.g. `v0.1.0`).
- LTS branches `release/MAJOR.x` are cut on demand and receive security patches for 12 months after the next MAJOR.

## Breaking change definition

Any change that requires a caller to modify their integration is breaking, including:

- Removing or renaming an HTTP route or response field.
- Changing the type or shape of an existing response field.
- Tightening validation of a previously accepted request.
- Removing or renaming a CLI command, flag, or output column.
- Changing the rulebook/template schema or the audit chain hash function.
