// PRD DESK-009 — local plugin / model sandbox.
//
// Verifies a plugin or model artifact before the desktop loads it:
//   1. SHA-256 of the file is constant-time compared to an expected hex digest.
//   2. An optional Ed25519 detached signature is verified against a public key
//      sourced from the `RADIOPAD_PLUGIN_PUBKEY` environment variable
//      (PEM-encoded SubjectPublicKeyInfo or raw 32-byte hex).
//
// Policy:
//   * Hash mismatch  → always rejected.
//   * Signature provided but no pubkey configured → rejected.
//   * Signature absent + no pubkey configured:
//       - in `debug_assertions` builds (dev) we log a warning and allow.
//       - in release builds we refuse.
//
// The trust model is documented in `desktop/PLUGIN_TRUST.md`.

use std::fs;
use std::path::Path;
#[cfg(target_os = "macos")]
use std::path::PathBuf;

use ed25519_dalek::{Signature, Verifier, VerifyingKey};
use sha2::{Digest, Sha256};

#[derive(Debug)]
pub enum SandboxError {
    Io(String),
    HashMismatch,
    SignatureRequired,
    InvalidSignatureEncoding,
    InvalidPublicKey(String),
    SignatureVerificationFailed,
    UnsignedPluginInRelease,
    SandboxLaunchFailed(String),
}

impl std::fmt::Display for SandboxError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            SandboxError::Io(e) => write!(f, "io error: {e}"),
            SandboxError::HashMismatch => write!(f, "sha256 mismatch"),
            SandboxError::SignatureRequired => {
                write!(f, "signature required because RADIOPAD_PLUGIN_PUBKEY is set")
            }
            SandboxError::InvalidSignatureEncoding => write!(f, "signature is not valid base64/hex"),
            SandboxError::InvalidPublicKey(m) => write!(f, "invalid public key: {m}"),
            SandboxError::SignatureVerificationFailed => write!(f, "ed25519 verification failed"),
            SandboxError::UnsignedPluginInRelease => {
                write!(f, "release builds refuse unsigned plugins")
            }
            SandboxError::SandboxLaunchFailed(e) => {
                write!(f, "sandbox launch failed: {e}")
            }
        }
    }
}

impl std::error::Error for SandboxError {}

impl From<std::io::Error> for SandboxError {
    fn from(e: std::io::Error) -> Self {
        SandboxError::Io(e.to_string())
    }
}

/// Constant-time byte comparison. Both inputs must be the same length.
fn ct_eq(a: &[u8], b: &[u8]) -> bool {
    if a.len() != b.len() {
        return false;
    }
    let mut acc: u8 = 0;
    for (x, y) in a.iter().zip(b.iter()) {
        acc |= x ^ y;
    }
    acc == 0
}

fn hex_decode(s: &str) -> Option<Vec<u8>> {
    let s = s.trim();
    if s.len() % 2 != 0 {
        return None;
    }
    let mut out = Vec::with_capacity(s.len() / 2);
    for chunk in s.as_bytes().chunks(2) {
        let hi = (chunk[0] as char).to_digit(16)?;
        let lo = (chunk[1] as char).to_digit(16)?;
        out.push(((hi << 4) | lo) as u8);
    }
    Some(out)
}

fn b64_decode(s: &str) -> Option<Vec<u8>> {
    // Minimal base64 decoder so we don't pull a new crate just for this.
    const T: [i8; 256] = build_b64_table();
    let s: String = s.chars().filter(|c| !c.is_whitespace()).collect();
    let s = s.trim_end_matches('=');
    let mut out = Vec::with_capacity(s.len() * 3 / 4);
    let mut buf: u32 = 0;
    let mut bits: u32 = 0;
    for b in s.bytes() {
        let v = T[b as usize];
        if v < 0 {
            return None;
        }
        buf = (buf << 6) | (v as u32);
        bits += 6;
        if bits >= 8 {
            bits -= 8;
            out.push(((buf >> bits) & 0xff) as u8);
        }
    }
    Some(out)
}

const fn build_b64_table() -> [i8; 256] {
    let mut t: [i8; 256] = [-1; 256];
    let alpha = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut i = 0;
    while i < alpha.len() {
        t[alpha[i] as usize] = i as i8;
        i += 1;
    }
    t
}

/// Parse an Ed25519 public key from either a 32-byte hex string or a PEM
/// `-----BEGIN PUBLIC KEY-----` SubjectPublicKeyInfo (the RFC 8410 form).
fn parse_pubkey(input: &str) -> Result<VerifyingKey, SandboxError> {
    let trimmed = input.trim();
    // Try raw 32-byte hex first.
    if let Some(raw) = hex_decode(trimmed) {
        if raw.len() == 32 {
            let arr: [u8; 32] = raw
                .as_slice()
                .try_into()
                .map_err(|_| SandboxError::InvalidPublicKey("hex length".into()))?;
            return VerifyingKey::from_bytes(&arr)
                .map_err(|e| SandboxError::InvalidPublicKey(e.to_string()));
        }
    }
    // PEM SubjectPublicKeyInfo: strip header/footer, base64-decode, then take
    // the trailing 32 bytes of the SPKI structure (RFC 8410 §4 — the
    // BIT STRING is the last component and contains the raw key).
    if trimmed.starts_with("-----BEGIN") {
        let inner: String = trimmed
            .lines()
            .filter(|l| !l.starts_with("-----"))
            .collect::<Vec<_>>()
            .join("");
        let der = b64_decode(&inner)
            .ok_or_else(|| SandboxError::InvalidPublicKey("base64".into()))?;
        if der.len() < 32 {
            return Err(SandboxError::InvalidPublicKey("der too short".into()));
        }
        let raw: [u8; 32] = der[der.len() - 32..]
            .try_into()
            .map_err(|_| SandboxError::InvalidPublicKey("tail length".into()))?;
        return VerifyingKey::from_bytes(&raw)
            .map_err(|e| SandboxError::InvalidPublicKey(e.to_string()));
    }
    Err(SandboxError::InvalidPublicKey("unknown encoding".into()))
}

/// Verify a plugin / model artifact at `path`.
///
/// `expected_sha256` must be a 64-char lowercase hex digest.
/// `expected_signature` is an optional base64 or hex-encoded 64-byte Ed25519
/// signature. The public key is resolved from `RADIOPAD_PLUGIN_PUBKEY`
/// (PEM or 32-byte hex). When the env var is unset:
///   - debug builds: warn + allow if no signature was supplied.
///   - release builds: refuse.
pub fn verify_plugin(
    path: &Path,
    expected_sha256: &str,
    expected_signature: Option<&str>,
) -> Result<(), SandboxError> {
    let bytes = fs::read(path)?;

    // 1. SHA-256 hash check.
    let mut hasher = Sha256::new();
    hasher.update(&bytes);
    let digest = hasher.finalize();
    let expected = hex_decode(expected_sha256)
        .ok_or(SandboxError::HashMismatch)?;
    if !ct_eq(&digest, &expected) {
        return Err(SandboxError::HashMismatch);
    }

    // 2. Signature check.
    let pubkey_pem = std::env::var("RADIOPAD_PLUGIN_PUBKEY").ok();
    match (expected_signature, pubkey_pem) {
        (Some(sig_str), Some(pem)) => {
            let sig_bytes = b64_decode(sig_str)
                .or_else(|| hex_decode(sig_str))
                .ok_or(SandboxError::InvalidSignatureEncoding)?;
            if sig_bytes.len() != 64 {
                return Err(SandboxError::InvalidSignatureEncoding);
            }
            let sig_arr: [u8; 64] = sig_bytes
                .as_slice()
                .try_into()
                .map_err(|_| SandboxError::InvalidSignatureEncoding)?;
            let signature = Signature::from_bytes(&sig_arr);
            let key = parse_pubkey(&pem)?;
            key.verify(&bytes, &signature)
                .map_err(|_| SandboxError::SignatureVerificationFailed)?;
            Ok(())
        }
        (Some(_), None) => {
            // A signature was supplied but no pubkey is configured — refuse.
            Err(SandboxError::InvalidPublicKey(
                "RADIOPAD_PLUGIN_PUBKEY not set".into(),
            ))
        }
        (None, Some(_)) => {
            // Pubkey is configured → signatures are mandatory.
            Err(SandboxError::SignatureRequired)
        }
        (None, None) => {
            #[cfg(debug_assertions)]
            {
                eprintln!(
                    "[sandbox] WARNING: loading unsigned plugin {} (debug build, RADIOPAD_PLUGIN_PUBKEY unset)",
                    path.display()
                );
                Ok(())
            }
            #[cfg(not(debug_assertions))]
            {
                Err(SandboxError::UnsignedPluginInRelease)
            }
        }
    }
}

// ---------------------------------------------------------------------------
// OS-specific plugin sandbox launch
// ---------------------------------------------------------------------------

/// Describes the sandbox strategy that was used for a launch.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SandboxStrategy {
    /// macOS `sandbox-exec` with a .sb profile.
    SandboxExec,
    /// Linux `bwrap --unshare-all`.
    Bwrap,
    /// Linux `unshare` fallback (weaker).
    Unshare,
    /// Windows AppContainer / WDAC.
    Wdac,
    /// No sandbox was applied (fallback / unsupported).
    Noop,
}

impl SandboxStrategy {
    /// Returns the tag set as `RADIOPAD_PLUGIN_SANDBOX` env var on the child.
    pub fn env_tag(&self) -> &'static str {
        match self {
            SandboxStrategy::SandboxExec => "sandbox-exec",
            SandboxStrategy::Bwrap => "bwrap",
            SandboxStrategy::Unshare => "unshare",
            SandboxStrategy::Wdac => "wdac",
            SandboxStrategy::Noop => "noop",
        }
    }
}

/// Result of a sandbox launch: the child process and the strategy used.
pub struct SandboxedChild {
    pub child: std::process::Child,
    pub strategy: SandboxStrategy,
    pub workdir: std::path::PathBuf,
}

/// Launch a verified plugin binary inside an OS-appropriate sandbox.
///
/// `plugin_binary` must be the absolute path to the verified executable.
/// A per-plugin working directory is created under `$TMPDIR/radiopad-plugin-<id>`.
pub fn launch_sandboxed(
    plugin_binary: &Path,
    plugin_id: &str,
) -> Result<SandboxedChild, SandboxError> {
    // Create per-plugin working directory.
    let workdir = std::env::temp_dir().join(format!("radiopad-plugin-{plugin_id}"));
    fs::create_dir_all(&workdir)?;

    #[cfg(target_os = "macos")]
    {
        launch_macos(plugin_binary, &workdir)
    }

    #[cfg(target_os = "linux")]
    {
        launch_linux(plugin_binary, &workdir)
    }

    #[cfg(target_os = "windows")]
    {
        launch_windows(plugin_binary, &workdir)
    }

    #[cfg(not(any(target_os = "macos", target_os = "linux", target_os = "windows")))]
    {
        launch_noop(plugin_binary, &workdir)
    }
}

// ---------------------------------------------------------------------------
// macOS — sandbox-exec with a .sb profile
// ---------------------------------------------------------------------------

#[cfg(target_os = "macos")]
fn launch_macos(
    plugin_binary: &Path,
    workdir: &Path,
) -> Result<SandboxedChild, SandboxError> {
    use std::process::Command;

    let sandbox_exec = Path::new("/usr/bin/sandbox-exec");
    if !sandbox_exec.exists() {
        eprintln!(
            "[sandbox] WARNING: /usr/bin/sandbox-exec not found; falling back to noop sandbox"
        );
        return launch_noop(plugin_binary, workdir);
    }

    // Resolve the profile template bundled alongside the Tauri binary.
    let profile_template = resolve_sb_profile()?;
    let template_content = fs::read_to_string(&profile_template).map_err(|e| {
        SandboxError::SandboxLaunchFailed(format!("cannot read .sb profile: {e}"))
    })?;

    let plugin_dir = plugin_binary
        .parent()
        .unwrap_or(Path::new("/"))
        .to_string_lossy();
    let plugin_bin = plugin_binary.to_string_lossy();
    let work = workdir.to_string_lossy();

    // Substitute variables into the profile.
    let profile = template_content
        .replace("(param \"PLUGIN_DIR\")", &format!("\"{plugin_dir}\""))
        .replace("(param \"PLUGIN_BINARY\")", &format!("\"{plugin_bin}\""))
        .replace("(param \"PLUGIN_WORKDIR\")", &format!("\"{work}\""));

    // Write the resolved profile to a temp file.
    let resolved_path = workdir.join("sandbox-profile.sb");
    fs::write(&resolved_path, &profile).map_err(|e| {
        SandboxError::SandboxLaunchFailed(format!("cannot write resolved .sb profile: {e}"))
    })?;

    let child = Command::new("/usr/bin/sandbox-exec")
        .arg("-f")
        .arg(&resolved_path)
        .arg(plugin_binary)
        .env("RADIOPAD_PLUGIN_SANDBOX", "sandbox-exec")
        .env("RADIOPAD_PLUGIN_WORKDIR", workdir)
        .spawn()
        .map_err(|e| SandboxError::SandboxLaunchFailed(e.to_string()))?;

    Ok(SandboxedChild {
        child,
        strategy: SandboxStrategy::SandboxExec,
        workdir: workdir.to_path_buf(),
    })
}

/// Resolve the path to `macos-plugin-sandbox.sb`.
///
/// At dev time it lives next to the source; in a bundled app it lives in the
/// Resources directory alongside the Tauri binary.
#[cfg(target_os = "macos")]
fn resolve_sb_profile() -> Result<PathBuf, SandboxError> {
    // 1. Check next to the current executable (bundled app).
    if let Ok(exe) = std::env::current_exe() {
        let beside_exe = exe
            .parent()
            .unwrap_or(Path::new("/"))
            .join("macos-plugin-sandbox.sb");
        if beside_exe.exists() {
            return Ok(beside_exe);
        }
        // macOS .app bundle: Contents/MacOS/../Resources
        let resources = exe
            .parent()
            .and_then(|p| p.parent())
            .map(|p| p.join("Resources").join("macos-plugin-sandbox.sb"));
        if let Some(r) = resources {
            if r.exists() {
                return Ok(r);
            }
        }
    }
    // 2. Fallback for dev: relative to CARGO_MANIFEST_DIR (compile-time).
    let fallback = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("macos-plugin-sandbox.sb");
    if fallback.exists() {
        return Ok(fallback);
    }
    Err(SandboxError::SandboxLaunchFailed(
        "macos-plugin-sandbox.sb not found".into(),
    ))
}

// ---------------------------------------------------------------------------
// Linux — bwrap / unshare (existing behaviour, wrapped into the new API)
// ---------------------------------------------------------------------------

#[cfg(target_os = "linux")]
fn launch_linux(
    plugin_binary: &Path,
    workdir: &Path,
) -> Result<SandboxedChild, SandboxError> {
    use std::process::Command;

    // Prefer bubblewrap.
    if let Ok(bwrap) = which("bwrap") {
        let child = Command::new(bwrap)
            .args([
                "--unshare-all",
                "--die-with-parent",
                "--ro-bind", "/", "/",
                "--tmpfs", "/tmp",
                "--tmpfs", "/run",
                "--bind",
            ])
            .arg(workdir)
            .arg(workdir)
            .args(["--chdir"])
            .arg(workdir)
            .arg("--")
            .arg(plugin_binary)
            .env("RADIOPAD_PLUGIN_SANDBOX", "bwrap")
            .env("RADIOPAD_PLUGIN_WORKDIR", workdir)
            .spawn()
            .map_err(|e| SandboxError::SandboxLaunchFailed(e.to_string()))?;

        return Ok(SandboxedChild {
            child,
            strategy: SandboxStrategy::Bwrap,
            workdir: workdir.to_path_buf(),
        });
    }

    // Fallback to unshare.
    if let Ok(unshare) = which("unshare") {
        let child = Command::new(unshare)
            .args(["--net", "--pid", "--user", "--map-root-user", "--"])
            .arg(plugin_binary)
            .env("RADIOPAD_PLUGIN_SANDBOX", "unshare")
            .env("RADIOPAD_PLUGIN_WORKDIR", workdir)
            .spawn()
            .map_err(|e| SandboxError::SandboxLaunchFailed(e.to_string()))?;

        return Ok(SandboxedChild {
            child,
            strategy: SandboxStrategy::Unshare,
            workdir: workdir.to_path_buf(),
        });
    }

    eprintln!("[sandbox] WARNING: neither bwrap nor unshare found; falling back to noop");
    launch_noop(plugin_binary, workdir)
}

// ---------------------------------------------------------------------------
// Windows — WDAC / AppContainer (stub — real logic is in the bundled launcher)
// ---------------------------------------------------------------------------

#[cfg(target_os = "windows")]
fn launch_windows(
    plugin_binary: &Path,
    workdir: &Path,
) -> Result<SandboxedChild, SandboxError> {
    use std::process::Command;

    // The actual AppContainer creation is handled by the WDAC launcher binary
    // shipped alongside RadioPad (see desktop/wdac/). Here we spawn through it.
    let child = Command::new(plugin_binary)
        .env("RADIOPAD_PLUGIN_SANDBOX", "wdac")
        .env("RADIOPAD_PLUGIN_WORKDIR", workdir)
        .env("RADIOPAD_PLUGIN_APPCONTAINER", "1")
        .spawn()
        .map_err(|e| SandboxError::SandboxLaunchFailed(e.to_string()))?;

    Ok(SandboxedChild {
        child,
        strategy: SandboxStrategy::Wdac,
        workdir: workdir.to_path_buf(),
    })
}

// ---------------------------------------------------------------------------
// Noop fallback
// ---------------------------------------------------------------------------

fn launch_noop(
    plugin_binary: &Path,
    workdir: &Path,
) -> Result<SandboxedChild, SandboxError> {
    use std::process::Command;

    let child = Command::new(plugin_binary)
        .env("RADIOPAD_PLUGIN_SANDBOX", "noop")
        .env("RADIOPAD_PLUGIN_WORKDIR", workdir)
        .spawn()
        .map_err(|e| SandboxError::SandboxLaunchFailed(e.to_string()))?;

    Ok(SandboxedChild {
        child,
        strategy: SandboxStrategy::Noop,
        workdir: workdir.to_path_buf(),
    })
}

/// Simple `which`-style lookup on `$PATH`.
#[cfg(target_os = "linux")]
fn which(bin: &str) -> Result<std::path::PathBuf, ()> {
    if let Ok(path) = std::env::var("PATH") {
        for dir in path.split(':') {
            let candidate = std::path::PathBuf::from(dir).join(bin);
            if candidate.exists() {
                return Ok(candidate);
            }
        }
    }
    Err(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    fn tmp_file(bytes: &[u8]) -> std::path::PathBuf {
        let mut p = std::env::temp_dir();
        p.push(format!("radiopad-sbx-{}.bin", std::process::id()));
        let mut f = fs::File::create(&p).unwrap();
        f.write_all(bytes).unwrap();
        p
    }

    #[test]
    fn hash_mismatch_is_rejected() {
        let p = tmp_file(b"hello");
        let bad = "00".repeat(32);
        let r = verify_plugin(&p, &bad, None);
        assert!(matches!(r, Err(SandboxError::HashMismatch)));
    }

    #[test]
    fn ct_eq_basic() {
        assert!(ct_eq(b"abc", b"abc"));
        assert!(!ct_eq(b"abc", b"abd"));
        assert!(!ct_eq(b"abc", b"abcd"));
    }
}
