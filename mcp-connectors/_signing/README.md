# RadioPad MCP connector signing

This directory holds the **placeholder** Ed25519 release signing key used to
verify the JSON manifests under `mcp-connectors/`. It is a development
placeholder — production deployments rotate to an HSM-backed key as
described in [docs/04-security/security-architecture.md](../../docs/04-security/security-architecture.md#mcp-signing).

Files:

- `release.pub` — base64-encoded 32-byte Ed25519 public key.
- `release.sec` — base64-encoded 32-byte Ed25519 seed (private key). **Dev
  placeholder only — never use in production. Rotate before shipping.**

Each connector ships with a sibling `<name>.sig` file containing the
detached Ed25519 signature (base64) over the canonical JSON bytes of
`<name>.json`. The backend `McpManifestVerifier` re-computes the signature
on registration; any mismatch flips the tool to `Status = Blocked` and
audits `McpToolBlocked` with `reason = "bad_signature"`.

To re-sign after editing a manifest, run:

```powershell
dotnet run --project cli/RadioPad.Cli -- mcp sign mcp-connectors/<name>.json
```
