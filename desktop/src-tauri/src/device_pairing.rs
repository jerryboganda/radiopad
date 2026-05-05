// PRD DESK-008 — per-install device fingerprint + pairing token.
//
// Device fingerprint is derived from the OS machine id (via the `machine-uid`
// crate) salted with 16 random bytes generated on first run. If `machine-uid`
// fails (e.g. on locked-down Linux containers) we fall back to a fully random
// UUID. The salted hash is stored in the OS keyring so subsequent calls are
// stable across reboots. Pairing tokens (issued by the backend's
// `POST /api/devices/pair` endpoint) are also stored in the OS keyring.

use rand::RngCore;
use sha2::{Digest, Sha256};

use crate::crypto_keyring::{
    keyring_get, keyring_set, KEY_DEVICE_FINGERPRINT, KEY_DEVICE_PAIRING_TOKEN,
};

fn hex_encode(bytes: &[u8]) -> String {
    const HEX: &[u8; 16] = b"0123456789abcdef";
    let mut out = String::with_capacity(bytes.len() * 2);
    for b in bytes {
        out.push(HEX[(b >> 4) as usize] as char);
        out.push(HEX[(b & 0x0f) as usize] as char);
    }
    out
}

fn random_hex(len_bytes: usize) -> String {
    let mut buf = vec![0u8; len_bytes];
    rand::rngs::OsRng.fill_bytes(&mut buf);
    hex_encode(&buf)
}

fn compute_fingerprint() -> String {
    let raw = match machine_uid::get() {
        Ok(id) if !id.is_empty() => id,
        _ => format!("rp-fallback-{}", random_hex(16)),
    };
    let salt = random_hex(16);
    let mut hasher = Sha256::new();
    hasher.update(raw.as_bytes());
    hasher.update(b"|");
    hasher.update(salt.as_bytes());
    let digest = hasher.finalize();
    let hex = hex_encode(&digest);
    format!("rp-{}", &hex[..32])
}

#[tauri::command]
pub async fn device_fingerprint() -> Result<String, String> {
    if let Some(existing) = keyring_get(KEY_DEVICE_FINGERPRINT)? {
        return Ok(existing);
    }
    let fp = compute_fingerprint();
    keyring_set(KEY_DEVICE_FINGERPRINT, &fp)?;
    Ok(fp)
}

#[tauri::command]
pub async fn device_pairing_token_set(token: String) -> Result<(), String> {
    if token.is_empty() || token.len() > 4096 {
        return Err("invalid pairing token length".into());
    }
    keyring_set(KEY_DEVICE_PAIRING_TOKEN, &token)
}

#[tauri::command]
pub async fn device_pairing_token_get() -> Result<Option<String>, String> {
    keyring_get(KEY_DEVICE_PAIRING_TOKEN)
}
