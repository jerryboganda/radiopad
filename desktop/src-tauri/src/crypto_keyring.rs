// PRD DESK-005 / DESK-006 / DESK-008 — OS-keyring backed secrets.
//
// All long-lived secrets used by the desktop shell live in the OS credential
// store (Windows Credential Manager / macOS Keychain / Linux Secret Service)
// via the `keyring` crate. The master AES-256-GCM key for the encrypted
// offline-draft store and the encrypted local cache is generated on first run
// and never written to disk in plaintext.

use keyring::Entry;
use rand::RngCore;

pub const SERVICE: &str = "com.radiopad.desktop";
pub const KEY_MASTER: &str = "radiopad-master-key";
pub const KEY_DEVICE_FINGERPRINT: &str = "radiopad-device-fingerprint";
pub const KEY_DEVICE_PAIRING_TOKEN: &str = "radiopad-device-pairing-token";

fn hex_encode(bytes: &[u8]) -> String {
    const HEX: &[u8; 16] = b"0123456789abcdef";
    let mut out = String::with_capacity(bytes.len() * 2);
    for b in bytes {
        out.push(HEX[(b >> 4) as usize] as char);
        out.push(HEX[(b & 0x0f) as usize] as char);
    }
    out
}

pub(crate) fn hex_decode(s: &str) -> Option<Vec<u8>> {
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

/// Returns the 32-byte master key, generating + persisting it on first call.
pub fn master_key() -> Result<[u8; 32], String> {
    let entry = Entry::new(SERVICE, KEY_MASTER).map_err(|e| e.to_string())?;
    match entry.get_password() {
        Ok(hex) => {
            let bytes = hex_decode(&hex).ok_or_else(|| "master key: invalid hex".to_string())?;
            if bytes.len() != 32 {
                return Err("master key: invalid length".into());
            }
            let mut arr = [0u8; 32];
            arr.copy_from_slice(&bytes);
            Ok(arr)
        }
        Err(_) => {
            let mut key = [0u8; 32];
            rand::rngs::OsRng.fill_bytes(&mut key);
            entry
                .set_password(&hex_encode(&key))
                .map_err(|e| e.to_string())?;
            Ok(key)
        }
    }
}

pub fn keyring_get(slot: &str) -> Result<Option<String>, String> {
    let entry = Entry::new(SERVICE, slot).map_err(|e| e.to_string())?;
    match entry.get_password() {
        Ok(v) => Ok(Some(v)),
        Err(keyring::Error::NoEntry) => Ok(None),
        Err(e) => Err(e.to_string()),
    }
}

pub fn keyring_set(slot: &str, value: &str) -> Result<(), String> {
    let entry = Entry::new(SERVICE, slot).map_err(|e| e.to_string())?;
    entry.set_password(value).map_err(|e| e.to_string())
}

pub fn keyring_delete(slot: &str) -> Result<(), String> {
    let entry = Entry::new(SERVICE, slot).map_err(|e| e.to_string())?;
    match entry.delete_password() {
        Ok(()) | Err(keyring::Error::NoEntry) => Ok(()),
        Err(e) => Err(e.to_string()),
    }
}
