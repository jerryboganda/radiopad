# RadioPad desktop — plugin trust model

**Status:** Active  **Owner:** Desktop / Security  **Last Updated:** 2026-05-05

The RadioPad desktop shell (Tauri 2) optionally loads local plugins and AI
model files. Because those artifacts execute or influence model behaviour
inside the radiologist's workstation, every artifact is verified before
load by [`src-tauri/src/sandbox.rs`](src-tauri/src/sandbox.rs).

## Verification pipeline

For each artifact (`<file>`) the desktop:

1. Computes `sha256(<file>)` and constant-time compares it to the expected
   digest passed in by the caller (e.g. embedded in a manifest).
2. If `RADIOPAD_PLUGIN_PUBKEY` is set, requires a detached Ed25519
   signature over the artifact bytes and verifies it against that key.
3. If the env var is **not** set:
   - Debug builds log a warning and allow the load (developer convenience).
   - Release builds **refuse** unsigned plugins.

The Tauri command `verify_plugin(path, expected_sha256, expected_signature)`
exposes the same check to the frontend.

## `RADIOPAD_PLUGIN_PUBKEY` format

Either:

- A 32-byte Ed25519 public key as 64 hex characters, or
- A PEM-encoded `SubjectPublicKeyInfo` (`-----BEGIN PUBLIC KEY-----`).

## Generating a key pair

```bash
# Private key (keep offline, never commit)
openssl genpkey -algorithm ED25519 -out radiopad-plugin.key

# Public key (PEM, distribute via MDM / config management)
openssl pkey -in radiopad-plugin.key -pubout -out radiopad-plugin.pub.pem

# Sign an artifact
openssl pkeyutl -sign -inkey radiopad-plugin.key \
  -rawin -in plugin.bin -out plugin.bin.sig
base64 < plugin.bin.sig > plugin.bin.sig.b64
```

Distribute `radiopad-plugin.pub.pem` via your endpoint configuration
(`RADIOPAD_PLUGIN_PUBKEY`). Distribute `plugin.bin` and the signature
together; never distribute the private key.

## CI verification

The CLI `radiopad plugin verify <path> --sha256 <hex> [--signature <sig>]`
mirrors the desktop check so build pipelines can fail closed before
artifacts reach a workstation.

## Threat model

| Threat | Mitigation |
| --- | --- |
| Tampered binary in transit | sha256 expected digest is signed/transported separately from artifact |
| Tampered binary at rest | constant-time hash compare on every load |
| Forged signature | Ed25519 (RFC 8032) over raw bytes; pubkey distributed out-of-band |
| Stolen workstation | private key never lives on workstations; pubkey only |
| Dev shortcut leaking to prod | release builds refuse unsigned plugins regardless of env |

## macOS sandbox — `sandbox-exec` profile (A3)

On macOS the desktop wraps plugin launches with
`/usr/bin/sandbox-exec -f <resolved-profile> <plugin_binary>`.

The sandbox profile template lives at
[`src-tauri/macos-plugin-sandbox.sb`](src-tauri/macos-plugin-sandbox.sb)
and is resolved at runtime (bundled app ➜ `Contents/Resources/`, dev ➜
source tree). Three variables are substituted before launch:

| Variable | Meaning |
| --- | --- |
| `PLUGIN_DIR` | Directory containing the plugin binary (read-only access). |
| `PLUGIN_BINARY` | Absolute path to the plugin executable (exec allowed). |
| `PLUGIN_WORKDIR` | Per-plugin writable scratch directory under `$TMPDIR/radiopad-plugin-<id>`. |

### Profile policy summary

| Category | Rule |
| --- | --- |
| Default | `(deny default)` — everything blocked unless explicitly allowed. |
| Network | `(deny network*)` — no sockets, no DNS, no HTTP. |
| FS reads | Plugin dir, `/usr/lib`, `/System/Library`, dyld cache. |
| FS writes | **Only** `PLUGIN_WORKDIR`. Hard-deny `/System`, `/Library`, `/usr`. |
| Process | Only the plugin binary may exec. `(deny process-fork)`. |
| IPC | `(allow mach-lookup)` — required by libSystem. |
| Sysctl | Read-only (`hw.ncpu`, `kern.osversion`, etc.). |
| Logging | `(debug deny)` — violations routed to syslog for diagnostics. |

### Fallback

If `/usr/bin/sandbox-exec` is not present (rare, but possible on
stripped CI images), the launch falls back to a noop sandbox and sets
`RADIOPAD_PLUGIN_SANDBOX=noop`. Operators should monitor for this tag.

---

## Audit

Every successful or failed `verify_plugin` invocation that originates from
the frontend should be logged via the existing `IAuditLog.AppendAsync`
backend pipeline (`AuditAction.PluginVerified` / `PluginRejected`). Never
patch existing audit rows — the SHA-256 chain is append-only.

## Server-side trust chain (removed)

The backend plugin trust chain added in iter-33 (manifest-signature verifier, `TrustedPluginPublisher` check,
capability registry, per-OS plugin sandbox) was removed on 2026-11-22 (owner decision) together with the MCP tool registry.
Only the desktop verification described above remains.
