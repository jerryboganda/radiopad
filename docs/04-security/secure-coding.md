# Secure Coding

**Status:** Current  ·  **Owner:** Security + Engineering  ·  **Last Updated:** 2026-05-04

## Input validation

- Validate at the system boundary only (controllers / DTO bind). No defensive copy-validation deep in services.
- Use `[Required]` and `[StringLength]` on DTO properties; reject malformed input with 422.
- Whitelist enums; reject unknown values rather than coercing.
- Trim and normalise text inputs but **do not** strip clinical content (e.g. "5 mm" must survive).

## Output encoding

- JSON output via `System.Text.Json` with default escaping.
- HTML rendering uses React's default escaping; no `dangerouslySetInnerHTML` for any radiology content.
- FHIR text export uses string-builder concatenation with explicit encoding for special characters.

## Auth checks

- Every controller method calls `ResolveContextAsync` first; tests assert this.
- Tenant id is **never** read from the request body — only from headers / claims.

## Dependency safety

- `dotnet list package --vulnerable --include-transitive` runs in CI weekly.
- `pnpm audit --prod` runs on every PR.
- New dependency requires a license + maintenance note in the PR.

## Injection prevention

- EF Core parameterises queries automatically; never concatenate SQL.
- `EF.Functions.Like` is the only "raw"-shape we use; the pattern is built from a sanitized `q` parameter.
- YAML parsing uses `YamlDotNet` with safe defaults; we do not enable arbitrary tag resolution.

## SSRF prevention

- AI providers are configured with **explicit URLs** in the provider catalog ([provider-catalog.md](../03-architecture/provider-catalog.md)).
- The Ollama adapter is restricted to loopback (`127.0.0.1`).
- No outbound `fetch` or `HttpClient.GetAsync(userInput)` anywhere in the codebase.

## File upload safety

- v0.x has no upload surface. When attachments land (Phase 2), they are scanned with ClamAV and stored in object storage with tenant-prefixed keys (see [../03-architecture/file-storage.md](../03-architecture/file-storage.md)).

## Error message safety

- Never include stack traces, secrets, or PHI in error responses.
- Always include the request id for support.
- Provider-policy block returns a stable `kind: "provider_policy"` so the frontend can render the locked banner.

## Cryptography

- Only `System.Security.Cryptography` (`SHA256`) for the audit chain.
- No bespoke crypto.
- TLS termination is the responsibility of the reverse proxy.
