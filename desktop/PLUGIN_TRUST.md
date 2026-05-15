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

## Iter-33 (MCP-007) — server-side trust chain

The desktop SHA-256 + signature pipeline above remains the front line. As
of iter-33 the **backend** also enforces the chain on every plugin /
manifest it accepts, regardless of which client uploaded it.

### `manifest.json.sig` over canonical JSON

A plugin bundle is now expected to ship two files side-by-side:

| File | Contents |
| --- | --- |
| `manifest.json` | The plugin descriptor (id, version, executable, capabilities…). |
| `manifest.json.sig` | Detached **ed25519** signature (64 raw bytes) over the **canonical-JSON** serialisation of `manifest.json`. |

Canonical-JSON serialisation is implemented by
[`PluginManifestSignatureVerifier.Canonicalize`](../backend/RadioPad.Api/src/RadioPad.Application/Services/Mcp/PluginManifestSignatureVerifier.cs):
parse the JSON, sort object keys ordinal-ascending, drop whitespace,
re-emit. Identical input documents produce identical bytes regardless of
key order or indentation, which is what the signer must hash.

### `TrustedPluginPublisher` table

The accepted public keys are tenant-scoped. Each row carries:

| Column | Meaning |
| --- | --- |
| `Id`, `TenantId`, `CreatedAt`, `UpdatedAt` | Standard entity columns. |
| `PublisherName` | Human-readable label (e.g. `"Sectra signing 2026-Q1"`). |
| `Ed25519PublicKeyBase64` | Raw 32-byte ed25519 public key, base64. |
| `RevokedAt` | Non-null = key is no longer trusted. Append-only — never `DELETE`. |

Rotating a key adds a new row; revoking sets `RevokedAt`. The verifier
ignores revoked rows. A tenant with **no** active publisher key cannot
load any signed plugin (deny-by-default).

### Capability-scoped registry (`IMcpCapabilityRegistry`)

Plugins must declare requested capabilities under
`"capabilities": [...]` in the manifest. The supported v0.1 vocabulary:

- `dicomweb.read` — read-only DICOMweb QIDO/WADO calls via the
  RadioPad gateway.
- `report.draft.suggest` — propose findings/impression text for an
  in-progress draft (always rendered with `.ai-mark`).
- `rulebook.lookup` — read approved rulebook entries (no writes).

After a successful signature verification, the host registers the
`(pluginId, capability)` tuples in
[`InMemoryMcpCapabilityRegistry`](../backend/RadioPad.Api/src/RadioPad.Application/Services/Mcp/InMemoryMcpCapabilityRegistry.cs).
Any tool call whose capability is not registered is refused with
`PluginPolicyException(reason="capability_not_registered")`. The default
state is empty — **deny-by-default**.

### Sandbox guarantees (`IPluginSandbox`)

The MCP host invokes plugin executables only through a per-OS sandbox
wrapper:

| OS | Wrapper | Mechanism |
| --- | --- | --- |
| Windows | `WindowsAppContainerSandbox` | Spawns the child inside an AppContainer SID via the bundled launcher (low-trust profile, no broker access). Sets `RADIOPAD_PLUGIN_APPCONTAINER=1`. |
| Linux (preferred) | `LinuxNamespaceSandbox` (bwrap path) | When `bwrap` is on `PATH` we wrap the launch with `bwrap --unshare-all --die-with-parent --ro-bind / / --tmpfs /tmp --tmpfs /run --bind <workdir> <workdir> --chdir <workdir> --`. `--unshare-all` covers net / pid / user / ipc / uts / cgroup; the bind layout denies any FS write outside the per-plugin work directory (the same effective contract the Linux `landlock` LSM gives us). Tags `RADIOPAD_PLUGIN_SANDBOX=bwrap`. |
| Linux (fallback) | `LinuxNamespaceSandbox` (unshare path) | When `bwrap` is missing, falls back to `unshare --net --pid --user --map-root-user --`. Net/PID/user are still stripped; FS is unrestricted, so the child is tagged `RADIOPAD_PLUGIN_SANDBOX=unshare` so the operator can see the reduced guarantee. |
| macOS | `MacOsSandboxExecSandbox` | Wraps the launch with `/usr/bin/sandbox-exec -p '<profile>'`. The profile denies all network access, allows read-only access to `/`, and grants read-write access only to the per-plugin work directory. Tags `RADIOPAD_PLUGIN_SANDBOX=sandbox-exec`. If `/usr/bin/sandbox-exec` is missing the wrapper logs a warning and falls back to a noop launch (`RADIOPAD_PLUGIN_SANDBOX=noop`). |

Each launch creates a per-plugin work directory under
`$TMPDIR/radiopad-plugin-<id>` and exports `RADIOPAD_PLUGIN_WORKDIR` so
the plugin knows where it can persist state. On Linux/macOS this
directory is the only writable FS location granted to the sandboxed
child.

Selection is automatic at startup based on
`RuntimeInformation.IsOSPlatform`. Calling `WindowsAppContainerSandbox` on
non-Windows (or `LinuxNamespaceSandbox` on non-Linux) throws
`PlatformNotSupportedException` so a misconfigured runtime cannot fall
through to an unsandboxed launch.

### Threat model — additions

| Threat | Mitigation |
| --- | --- |
| Plugin tampered after signing | Canonical-JSON signature; verifier blocks + audits `ProviderBlocked{kind=plugin_policy, reason=bad_signature}`. |
| Stolen publisher key | Tenant admin sets `RevokedAt` on the row; verifier rejects on next load. |
| Capability creep at runtime | Registry is deny-by-default; tools may call only the `(pluginId, capability)` tuples registered from the verified manifest. |
| Plugin escape via filesystem / network | Per-OS sandbox wrapper. Linux: `bwrap --unshare-all --ro-bind /` (preferred) or `unshare` fallback. macOS: `sandbox-exec` profile that denies network and FS writes outside the per-plugin work directory. Windows: AppContainer SID. |
| Capability list forged after signing | The list lives **inside** `manifest.json`; tampering invalidates the signature. |

Audit: every block path — bad signature, missing signature, no trusted
publisher, malformed JSON — appends one
`AuditAction.ProviderBlocked` row with
`details = { kind: "plugin_policy", pluginId, reason }` before the
`PluginPolicyException` is rethrown. The append-only SHA-256 chain
defined in [docs/04-security/security-architecture.md](../docs/04-security/security-architecture.md)
covers these rows.
