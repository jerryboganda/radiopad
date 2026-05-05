// PRD DESK-006 — encrypted offline draft store.
//
// Persists a JSON map { draftId: { sections, updatedAt, dirty } } via
// `tauri-plugin-store`, with each value encrypted at rest using AES-256-GCM
// keyed off the OS-keyring-backed master key. Operations are mirrored to an
// append-only audit log file in the app data dir (`offline_drafts_audit.log`).

use std::fs::OpenOptions;
use std::io::Write;
use std::path::PathBuf;
use std::time::{SystemTime, UNIX_EPOCH};

use aes_gcm::aead::{Aead, OsRng};
use aes_gcm::{AeadCore, Aes256Gcm, Key, KeyInit, Nonce};
use base64::engine::general_purpose::STANDARD as B64;
use base64::Engine;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use tauri::{AppHandle, Manager, Runtime};
use tauri_plugin_store::StoreExt;

use crate::crypto_keyring::master_key;

pub const STORE_FILE: &str = "offline-drafts.enc.json";
const AUDIT_FILE: &str = "offline_drafts_audit.log";

#[derive(Serialize, Deserialize, Clone)]
pub struct DraftRecord {
    pub sections: Value,
    #[serde(rename = "updatedAt")]
    pub updated_at: u64,
    pub dirty: bool,
}

fn now_secs() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

fn audit_path<R: Runtime>(app: &AppHandle<R>) -> Result<PathBuf, String> {
    let mut p = app.path().app_data_dir().map_err(|e| e.to_string())?;
    std::fs::create_dir_all(&p).map_err(|e| e.to_string())?;
    p.push(AUDIT_FILE);
    Ok(p)
}

fn audit<R: Runtime>(app: &AppHandle<R>, action: &str, draft_id: &str) -> Result<(), String> {
    let p = audit_path(app)?;
    let mut f = OpenOptions::new()
        .create(true)
        .append(true)
        .open(&p)
        .map_err(|e| e.to_string())?;
    // Draft ids are opaque ULIDs/UUIDs — never PHI — but defensively escape
    // quotes so a malformed id cannot break the JSON-Lines format.
    let safe = draft_id.replace('"', "\\\"");
    writeln!(
        f,
        "{{\"ts\":{},\"action\":\"{}\",\"draftId\":\"{}\"}}",
        now_secs(),
        action,
        safe
    )
    .map_err(|e| e.to_string())?;
    Ok(())
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
pub async fn offline_drafts_list<R: Runtime>(app: AppHandle<R>) -> Result<Vec<String>, String> {
    let store = app.store(STORE_FILE).map_err(|e| e.to_string())?;
    Ok(store.keys())
}

#[tauri::command]
pub async fn offline_drafts_save<R: Runtime>(
    app: AppHandle<R>,
    draft_id: String,
    sections: Value,
) -> Result<(), String> {
    let rec = DraftRecord {
        sections,
        updated_at: now_secs(),
        dirty: true,
    };
    let plain = serde_json::to_vec(&rec).map_err(|e| e.to_string())?;
    let blob = encrypt(&plain)?;
    let store = app.store(STORE_FILE).map_err(|e| e.to_string())?;
    store.set(draft_id.clone(), Value::String(B64.encode(&blob)));
    store.save().map_err(|e| e.to_string())?;
    audit(&app, "save", &draft_id)?;
    Ok(())
}

#[tauri::command]
pub async fn offline_drafts_get<R: Runtime>(
    app: AppHandle<R>,
    draft_id: String,
) -> Result<Option<DraftRecord>, String> {
    let store = app.store(STORE_FILE).map_err(|e| e.to_string())?;
    let val = store.get(draft_id.as_str());
    audit(&app, "get", &draft_id)?;
    let Some(v) = val else { return Ok(None) };
    let s = v.as_str().ok_or("invalid stored value")?;
    let blob = B64.decode(s).map_err(|e| e.to_string())?;
    let plain = decrypt(&blob)?;
    let rec: DraftRecord = serde_json::from_slice(&plain).map_err(|e| e.to_string())?;
    Ok(Some(rec))
}

#[tauri::command]
pub async fn offline_drafts_delete<R: Runtime>(
    app: AppHandle<R>,
    draft_id: String,
) -> Result<(), String> {
    let store = app.store(STORE_FILE).map_err(|e| e.to_string())?;
    store.delete(draft_id.as_str());
    store.save().map_err(|e| e.to_string())?;
    audit(&app, "delete", &draft_id)?;
    Ok(())
}
