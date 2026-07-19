// RadioPad desktop shell — Tauri 2.0 bootstrap.
//
// Responsibilities:
//   * Load the static export of the Next.js frontend.
//   * Register the documented global hotkeys (PRD DESK-003).
//   * Expose a secure-clipboard command that wipes the clipboard after N ms,
//     with optional clear-on-blur (PRD DESK-004).
//   * Encrypted offline-draft store (PRD DESK-006) + general local cache
//     (PRD DESK-005), keyed off an OS-keyring-backed master key.
//   * Per-install device pairing flow (PRD DESK-008).
//   * PHI redaction layer over the global tracing subscriber (PRD DESK-010).
//   * Spawn the bundled `radiopad-api` sidecar for ON-DEVICE STT only (PRD
//     DESK-015); the app itself talks to the hosted production API.

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod crypto_keyring;
mod backend_health;
mod device_pairing;
mod hotkeys;
mod local_cache;
mod log_redactor;
mod offline_drafts;
mod pacs_plugins;
mod sandbox;
mod sidecar_manager;

use std::path::PathBuf;
use std::sync::{Mutex, OnceLock};

use tauri::Emitter;
use tauri::Manager;
use tauri_plugin_clipboard_manager::ClipboardExt;
use tauri_plugin_dialog::DialogExt;
use tauri_plugin_global_shortcut::ShortcutState;
use tauri_plugin_store::StoreExt;

const SETTINGS_FILE: &str = "radiopad-settings.json";
const SETTING_CLEAR_ON_BLUR: &str = "secureClipboard.clearOnBlur";
static SECURE_CLIPBOARD_TEXT: OnceLock<Mutex<Option<String>>> = OnceLock::new();

fn secure_clipboard_text() -> &'static Mutex<Option<String>> {
    SECURE_CLIPBOARD_TEXT.get_or_init(|| Mutex::new(None))
}

fn remember_secure_clipboard_text(text: &str) {
    if let Ok(mut current) = secure_clipboard_text().lock() {
        *current = Some(text.to_string());
    }
}

fn forget_secure_clipboard_text(expected: &str) {
    if let Ok(mut current) = secure_clipboard_text().lock() {
        if current.as_deref() == Some(expected) {
            *current = None;
        }
    }
}

fn clear_secure_clipboard_if_owned(app: &tauri::AppHandle) -> bool {
    let expected = secure_clipboard_text()
        .lock()
        .ok()
        .and_then(|current| current.clone());
    let Some(expected) = expected else {
        return false;
    };

    let still_ours = app
        .clipboard()
        .read_text()
        .map(|current| current == expected)
        .unwrap_or(false);
    if !still_ours {
        forget_secure_clipboard_text(&expected);
        return false;
    }

    if app.clipboard().write_text(String::new()).is_ok() {
        forget_secure_clipboard_text(&expected);
        return true;
    }
    false
}

/// Base URL for the RadioPad application API. The desktop is a thin client over
/// the hosted production service — auth, reports, AI, settings, everything except
/// on-device dictation goes here. `RADIOPAD_BACKEND` overrides it for development.
#[tauri::command]
fn get_backend_url() -> String {
    std::env::var("RADIOPAD_BACKEND")
        .unwrap_or_else(|_| "https://radiopadstudio.com".to_string())
}

/// Base URL for the bundled on-device STT sidecar (loopback only). The frontend
/// routes ONLY dictation transcription here so PHI audio is transcribed on the
/// device and never leaves it; everything else uses `get_backend_url`. Kept in
/// lock-step with the sidecar's bind in `sidecar_manager` (`RADIOPAD_LOCAL_BIND`
/// overrides both).
#[tauri::command]
fn get_local_stt_url() -> String {
    std::env::var("RADIOPAD_LOCAL_BIND")
        .unwrap_or_else(|_| backend_health::DEFAULT_BACKEND_URL.to_string())
}

/// Copy `text` to the OS clipboard and clear it after `ttl_ms` milliseconds.
/// Use this for any value that may contain PHI (accession numbers, MRNs).
///
/// Falls back gracefully when the clipboard plugin is unavailable: returns
/// `Err` instead of panicking, so the frontend can degrade to a manual
/// copy-paste fallback.
#[tauri::command]
async fn secure_copy(app: tauri::AppHandle, text: String, ttl_ms: u64) -> Result<(), String> {
    let clipboard = app.clipboard();
    clipboard
        .write_text(text.clone())
        .map_err(|e| format!("clipboard unavailable: {e}"))?;
    remember_secure_clipboard_text(&text);
    let app2 = app.clone();
    tauri::async_runtime::spawn(async move {
        tokio::time::sleep(std::time::Duration::from_millis(ttl_ms)).await;
        if clear_secure_clipboard_if_owned(&app2) {
            let _ = app2.emit("radiopad://clipboard-cleared", ());
        }
    });
    Ok(())
}

/// Save an authenticated report export through the operating system's native
/// Save As dialog. WebView2 can silently discard temporary blob downloads, so
/// desktop exports write the already-fetched bytes to the selected destination.
#[tauri::command]
async fn save_export_file(
    app: tauri::AppHandle,
    file_name: String,
    bytes: Vec<u8>,
) -> Result<bool, String> {
    let safe_name = PathBuf::from(&file_name)
        .file_name()
        .and_then(|name| name.to_str())
        .filter(|name| !name.is_empty())
        .unwrap_or("radiopad-export")
        .to_string();
    let selected = app
        .dialog()
        .file()
        .set_file_name(safe_name)
        .blocking_save_file();
    let Some(selected) = selected else {
        return Ok(false);
    };
    let path = selected
        .into_path()
        .map_err(|e| format!("invalid export destination: {e}"))?;
    std::fs::write(&path, bytes).map_err(|e| format!("could not save export: {e}"))?;
    Ok(true)
}

/// PRD DESK-009 — verify a plugin or model artifact before the desktop loads
/// it. Returns `Ok(())` on success or a string describing the failure.
#[tauri::command]
fn verify_plugin(
    path: String,
    expected_sha256: String,
    expected_signature: Option<String>,
) -> Result<(), String> {
    let p = PathBuf::from(&path);
    sandbox::verify_plugin(&p, &expected_sha256, expected_signature.as_deref())
        .map_err(|e| e.to_string())
}

/// Read the `secureClipboard.clearOnBlur` setting from the persisted
/// settings store. Defaults to `false` (preserve existing UX) when the store
/// can't be opened or the key is unset.
fn clear_on_blur_enabled<R: tauri::Runtime>(app: &tauri::AppHandle<R>) -> bool {
    let Ok(store) = app.store(SETTINGS_FILE) else {
        return false;
    };
    store
        .get(SETTING_CLEAR_ON_BLUR)
        .and_then(|v| v.as_bool())
        .unwrap_or(false)
}

fn main() {
    // PRD DESK-010 — install the redacting tracing subscriber before any
    // other code can produce log lines.
    log_redactor::init();

    tauri::Builder::default()
        // Single-instance guard MUST be the first plugin registered. When the user
        // launches RadioPad again while it is already running, this fires in the
        // EXISTING process instead of letting a second process boot — that second
        // process would spawn its own sidecar, fail to bind the already-held loopback
        // port (7457), crash-loop, and raise a false "backend sidecar exited
        // repeatedly" banner even though the app is perfectly healthy. Here we simply
        // surface and focus the running window.
        .plugin(tauri_plugin_single_instance::init(|app, _argv, _cwd| {
            if let Some(win) = app.get_webview_window("main") {
                let _ = win.unminimize();
                let _ = win.show();
                let _ = win.set_focus();
            }
        }))
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_dialog::init())
        // DESK-001 — auto-updater. The frontend "Check for updates" button
        // (and the silent check-on-launch) drive this plugin; `process` lets it
        // relaunch into the freshly installed build once download+install finish.
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(tauri_plugin_process::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .plugin(tauri_plugin_store::Builder::new().build())
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                // Dispatch through the rebindable table in `hotkeys` rather than matching fixed
                // key codes: the chord for an action is whatever the user bound it to, so the
                // shortcut→action mapping cannot live in a `match` here. See hotkeys.rs.
                .with_handler(|app, shortcut, event| {
                    if event.state != ShortcutState::Pressed {
                        return;
                    }
                    hotkeys::dispatch(app, shortcut);
                })
                .build(),
        )
        // Live shortcut→action table backing the rebindable global hotkeys. Must be managed before
        // `setup` runs, since registering the defaults writes into it.
        .manage(hotkeys::HotkeyState::default())
        .invoke_handler(tauri::generate_handler![
            get_backend_url,
            get_local_stt_url,
            secure_copy,
            save_export_file,
            verify_plugin,
            offline_drafts::offline_drafts_list,
            offline_drafts::offline_drafts_save,
            offline_drafts::offline_drafts_get,
            offline_drafts::offline_drafts_delete,
            local_cache::local_cache_get,
            local_cache::local_cache_put,
            local_cache::local_cache_clear,
            device_pairing::device_fingerprint,
            device_pairing::device_pairing_token_set,
            device_pairing::device_pairing_token_get,
            device_pairing::device_pairing_token_clear,
            pacs_plugins::pacs_plugins_list,
            pacs_plugins::pacs_plugins_verify,
            pacs_plugins::pacs_plugins_set_enabled,
            hotkeys::hotkeys_apply,
            hotkeys::hotkeys_supported_actions,
        ])
        .setup(|app| {
            // Register the built-in defaults now so the shortcuts work before the webview has
            // loaded; the frontend then pushes the user's effective bindings via `hotkeys_apply`,
            // which unregisters this set and replaces it.
            hotkeys::register_defaults(app.handle());

            // PRD DESK-004 — clear the clipboard on focus loss when the
            // tenant has opted in via `secureClipboard.clearOnBlur`.
            if let Some(window) = app.get_webview_window("main") {
                let app_handle = app.handle().clone();
                window.on_window_event(move |event| {
                    if let tauri::WindowEvent::Focused(false) = event {
                        if clear_on_blur_enabled(&app_handle)
                            && clear_secure_clipboard_if_owned(&app_handle)
                        {
                            let _ = app_handle.emit("radiopad://clipboard-cleared", ());
                        }
                    }
                });
            }

            // PRD DESK-015 — supervise the bundled backend sidecar without
            // panicking when the binary is missing or exits unexpectedly.
            sidecar_manager::start(app.handle().clone());
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running RadioPad desktop");
}
