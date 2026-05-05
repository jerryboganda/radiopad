# Example: Sectra workstation bridge plugin

**This is a stub for vendor onboarding.** It is intentionally unsigned. Replace `manifest.sig.b64` with a real Ed25519 detached signature over the bytes of `manifest.json` to ship.

## Files

- `manifest.json` — declared capabilities and identity metadata.
- `manifest.sig.b64` — base64 Ed25519 detached signature (placeholder).

## Capabilities

| Capability | Mapping in RadioPad desktop |
| --- | --- |
| `window-detect` | Detect when the Sectra IDS7 viewer is foregrounded. |
| `accession-grab` | Read the active accession from Sectra's title bar / scripting API. |
| `paste-back` | Paste a finalised report body back into the radiologist's Sectra session. |

## Build / sign

```powershell
# 1. Compute the canonical SHA-256 of manifest.json (sha256 field zeroed).
radiopad pacs plugins verify .\manifest.json --print-canonical-hash

# 2. Sign the canonical bytes with your Ed25519 private key.
openssl pkeyutl -sign -inkey vendor.key -rawin -in manifest.canonical.json -out manifest.sig
base64 -w0 manifest.sig > manifest.sig.b64
```
