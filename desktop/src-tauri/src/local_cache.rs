// PRD DESK-005 — encrypted local cache for non-draft data (study metadata,
// prior reports). Uses the same AES-256-GCM + OS-keyring master key as the
// offline-draft store. Each scope ("prior_reports", "study_meta", …) gets its
// own backing file under the app data dir. Entries carry a per-entry TTL
// (default 1 hour) and are evicted lazily on read.

use std::time::{SystemTime, UNIX_EPOCH};

use aes_gcm::aead::{Aead, OsRng};
use aes_gcm::{AeadCore, Aes256Gcm, Key, KeyInit, Nonce};
use base64::engine::general_purpose::STANDARD as B64;
use base64::Engine;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use tauri::{AppHandle, Runtime};
use tauri_plugin_store::StoreExt;

use crate::crypto_keyring::master_key;

const DEFAULT_TTL_SECONDS: u64 = 3600;

#[derive(Serialize, Deserialize)]
struct CacheEntry {
    value: Value,
    #[serde(rename = "expiresAt")]
    expires_at: u64,
}

fn now_secs() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

fn safe_scope(scope: &str) -> Result<String, String> {
    if scope.is_empty()
        || !scope
            .chars()
            .all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-')
    {
        return Err("invalid cache scope".into());
    }
    Ok(format!("local-cache.{}.enc.json", scope))
}

fn encrypt(plain: &[u8]) -> Result<Vec<u8>, String> {
    let key_bytes = master_key()?;
    let key = Key::<Aes256Gcm>::from_slice(&key_bytes);
    let cipher = Aes256Gcm::new(key);
    let nonce = Aes256Gcm::generate_nonce(&mut OsRng);
    let ct = cipher.encrypt(&nonce, plain).map_err(|e| e.to_string())?;
    let mut out = Vec::with_capacity(12 + ct.len());
    out.extend_from_slice(&nonce);
    out.extend_from_slice(&ct);
    Ok(out)
}

fn decrypt(blob: &[u8]) -> Result<Vec<u8>, String> {
    if blob.len() < 12 {
        return Err("ciphertext too short".into());
    }
    let key_bytes = master_key()?;
    let key = Key::<Aes256Gcm>::from_slice(&key_bytes);
    let cipher = Aes256Gcm::new(key);
    let nonce = Nonce::from_slice(&blob[..12]);
    cipher
        .decrypt(nonce, &blob[12..])
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn local_cache_put<R: Runtime>(
    app: AppHandle<R>,
    scope: String,
    key: String,
    value: Value,
    ttl_seconds: Option<u64>,
) -> Result<(), String> {
    let file = safe_scope(&scope)?;
    let ttl = ttl_seconds.unwrap_or(DEFAULT_TTL_SECONDS);
    let entry = CacheEntry {
        value,
        expires_at: now_secs().saturating_add(ttl),
    };
    let plain = serde_json::to_vec(&entry).map_err(|e| e.to_string())?;
    let blob = encrypt(&plain)?;
    let store = app.store(&file).map_err(|e| e.to_string())?;
    store.set(key, Value::String(B64.encode(&blob)));
    store.save().map_err(|e| e.to_string())?;
    Ok(())
}

#[tauri::command]
pub async fn local_cache_get<R: Runtime>(
    app: AppHandle<R>,
    scope: String,
    key: String,
) -> Result<Option<Value>, String> {
    let file = safe_scope(&scope)?;
    let store = app.store(&file).map_err(|e| e.to_string())?;
    let Some(v) = store.get(key.as_str()) else {
        return Ok(None);
    };
    let s = v.as_str().ok_or("invalid stored value")?;
    let blob = B64.decode(s).map_err(|e| e.to_string())?;
    let plain = decrypt(&blob)?;
    let entry: CacheEntry = serde_json::from_slice(&plain).map_err(|e| e.to_string())?;
    if entry.expires_at <= now_secs() {
        store.delete(key.as_str());
        let _ = store.save();
        return Ok(None);
    }
    Ok(Some(entry.value))
}

#[tauri::command]
pub async fn local_cache_clear<R: Runtime>(
    app: AppHandle<R>,
    scope: String,
) -> Result<(), String> {
    let file = safe_scope(&scope)?;
    let store = app.store(&file).map_err(|e| e.to_string())?;
    store.clear();
    store.save().map_err(|e| e.to_string())?;
    Ok(())
}
