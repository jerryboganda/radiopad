# RadioPad PACS Plugin SDK

**Status:** Active  **Owner:** Desktop / PACS  **Last Updated:** 2026-05-04

Vendor-side adapters that bridge a workstation PACS (Sectra, AGFA, Visage, Merge, Hyland) into RadioPad ship as **signed manifests** loaded by the desktop shell. Plugins do not run arbitrary code — they describe capabilities (window detection, accession grab, paste-back) that the desktop's PACS bridge exposes through documented Tauri commands.

## Trust model

Every manifest is verified before load:

1. **SHA-256** of the manifest bytes is constant-time compared against `manifest.sha256`.
2. **Ed25519** detached signature (`manifest.sig.b64`) is verified against `RADIOPAD_PLUGIN_PUBKEY` (PEM `SubjectPublicKeyInfo` or 32-byte hex).

Release builds **refuse** unsigned plugins. See [desktop/PLUGIN_TRUST.md](../PLUGIN_TRUST.md) for key generation and rotation. The verifier is the same one used for desktop sandbox plugins (iter-30) — see [`src-tauri/src/sandbox.rs`](../src-tauri/src/sandbox.rs) and the new [`src-tauri/src/pacs_plugins.rs`](../src-tauri/src/pacs_plugins.rs).

## Install location

- Windows: `%APPDATA%\RadioPad\plugins\<plugin-id>\`
- macOS: `~/Library/Application Support/RadioPad/plugins/<plugin-id>/`
- Linux: `~/.local/share/RadioPad/plugins/<plugin-id>/`

Each plugin folder contains:

```
<plugin-id>/
├── manifest.json       — schema-validated manifest (see manifest.schema.json)
├── manifest.sig.b64    — Ed25519 detached signature over manifest.json bytes (base64)
└── README.md           — vendor-supplied operator notes
```

## Manifest format

See [`manifest.schema.json`](./manifest.schema.json) for the JSON schema. Required fields:

- `id` — kebab-case identifier (`sectra-pacs`, `agfa-impax`, …).
- `name` — human-readable name shown on `/admin/pacs`.
- `vendor` — vendor name.
- `version` — semver string.
- `sha256` — lowercase hex digest of the manifest body **excluding the `sha256` field itself** (canonical JSON, sorted keys, no whitespace).
- `capabilities` — array of declared capabilities. Allowed values: `window-detect`, `accession-grab`, `paste-back`, `hanging-protocol`, `study-launch`.
- `executable` — optional native helper (path relative to the plugin folder). MUST also be hashed and signed if present (`executable_sha256`, `executable_sig_b64`).

## CLI

```powershell
# List installed plugins (verifying each manifest).
radiopad pacs plugins list

# Verify a single manifest file.
radiopad pacs plugins verify .\sectra-plugin\manifest.json

# Enable / disable (writes the `enabled: true|false` field).
radiopad pacs plugins enable  sectra-pacs
radiopad pacs plugins disable sectra-pacs
```

## Example

[`example-sectra-plugin/`](./example-sectra-plugin) is a stub showing the manifest layout and capability declarations. It is **not** signed by RadioPad — vendors substitute their own signature.

## Vendor onboarding

1. Generate an Ed25519 keypair (see [PLUGIN_TRUST.md](../PLUGIN_TRUST.md)).
2. Distribute your public key to RadioPad operators out-of-band; they install it as `RADIOPAD_PLUGIN_PUBKEY`.
3. Build your manifest, hash + sign it, drop it in the operator's plugins folder.
4. The desktop shell verifies on next launch and lights up `/admin/pacs` with your plugin.

## Locks

- Plugins are signed manifests — they MUST NOT execute unsanctioned native code. Capabilities map to documented Tauri commands only.
- No PHI may appear in any manifest. Vendor identifiers and capability lists only.
- Verification failures are audited as `PluginRejected` (desktop → backend). Successful verifications carry the plugin id, vendor, and SHA-256 prefix.
