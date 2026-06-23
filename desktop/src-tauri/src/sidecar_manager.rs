use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;

use serde::Serialize;
use tauri::{AppHandle, Emitter, Manager};
use tauri_plugin_shell::process::CommandEvent;
use tauri_plugin_shell::ShellExt;

use crate::backend_health::{self, DEFAULT_BACKEND_URL};

const STATUS_EVENT: &str = "radiopad://backend-status";
const MAX_RESTARTS: u32 = 3;

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct BackendStatus {
    state: &'static str,
    message: Option<String>,
    restart_count: u32,
}

fn backend_url() -> String {
    std::env::var("RADIOPAD_BACKEND").unwrap_or_else(|_| DEFAULT_BACKEND_URL.to_string())
}

fn emit_status(app: &AppHandle, state: &'static str, message: Option<String>, restart_count: u32) {
    let _ = app.emit(
        STATUS_EVENT,
        BackendStatus {
            state,
            message,
            restart_count,
        },
    );
}

fn restart_delay(restart_count: u32) -> Duration {
    Duration::from_secs(u64::from(restart_count.min(MAX_RESTARTS)).saturating_mul(2).max(2))
}

fn spawn_health_loop(app: AppHandle, bind: String, stop: Arc<AtomicBool>, restart_count: u32) {
    tauri::async_runtime::spawn(async move {
        let mut last_state: Option<&'static str> = None;
        let mut last_message: Option<String> = None;

        while !stop.load(Ordering::Relaxed) {
            let (state, message) = match backend_health::check_ready_async(&bind).await {
                Ok(()) => ("ready", None),
                Err(e) => ("degraded", Some(e)),
            };
            if last_state != Some(state) || last_message != message {
                emit_status(&app, state, message.clone(), restart_count);
                last_state = Some(state);
                last_message = message;
            }

            for _ in 0..10 {
                if stop.load(Ordering::Relaxed) {
                    break;
                }
                tokio::time::sleep(Duration::from_millis(500)).await;
            }
        }
    });
}

/// Resolve a per-user writable SQLite connection string for the bundled
/// sidecar. The desktop install lives under a read-only location (e.g.
/// `C:\Program Files\RadioPad`), so the API must not fall back to its
/// CWD-relative default DB, which would be unwritable for a non-elevated user.
fn sidecar_db_conn(app: &AppHandle) -> Option<String> {
    let dir = app.path().app_local_data_dir().ok()?;
    if let Err(e) = std::fs::create_dir_all(&dir) {
        tracing::warn!("radiopad-api: could not create data dir {dir:?}: {e}");
        return None;
    }
    Some(format!("Data Source={}", dir.join("radiopad.db").display()))
}

pub fn start(app: AppHandle) {
    if std::env::var("RADIOPAD_NO_SIDECAR").ok().as_deref() == Some("1") {
        emit_status(&app, "disabled", None, 0);
        return;
    }

    tauri::async_runtime::spawn(async move {
        supervise(app).await;
    });
}

async fn supervise(app: AppHandle) {
    let bind = backend_url();
    let db_conn = sidecar_db_conn(&app);
    let mut restart_count = 0;

    loop {
        // Adopt an already-healthy sidecar instead of spawning a conflicting one.
        // If a previous shell exited abruptly (crash / force-quit / OS kill), the OS
        // may leave its sidecar child running and still bound to the loopback port.
        // A freshly launched shell that blindly spawns a *second* sidecar would fail
        // to bind that port, crash-loop to MAX_RESTARTS, and raise a false "backend
        // sidecar exited repeatedly" danger banner — even though the window reads live
        // data fine off the surviving sidecar. Detect that case and monitor the
        // existing backend instead of fighting it. (Only RadioPad serves
        // /api/health/ready with 200 on this port, so this won't adopt a stranger.)
        if backend_health::check_ready_async(&bind).await.is_ok() {
            emit_status(&app, "ready", None, restart_count);
            loop {
                tokio::time::sleep(Duration::from_millis(1500)).await;
                if backend_health::check_ready_async(&bind).await.is_err() {
                    break;
                }
            }
            // The adopted backend went away — reset and fall through to (re)spawn ours.
            restart_count = 0;
            continue;
        }

        emit_status(&app, "starting", None, restart_count);
        let sidecar = match app.shell().sidecar("radiopad-api") {
            Ok(sidecar) => sidecar,
            Err(e) => {
                emit_status(
                    &app,
                    "failed",
                    Some(format!("backend sidecar unavailable: {e}")),
                    restart_count,
                );
                break;
            }
        };

        // The bundled sidecar is a single-user, loopback-only instance. Run it
        // in the local "Development" profile so it uses built-in local defaults
        // instead of demanding cloud production secrets (RADIOPAD_AUTH_SECRET /
        // RADIOPAD_COLUMN_KEY_*) and seeds the local workspace. RADIOPAD_DEV_HEADERS
        // enables the passwordless dev/local sign-in endpoint (Development alone
        // does not — the gate checks for "Testing" or this flag). The environment
        // is scoped to this child process only — it does not touch machine env.
        let mut command = sidecar
            .env("RADIOPAD_BIND", &bind)
            .env("ASPNETCORE_ENVIRONMENT", "Development")
            .env("RADIOPAD_DEV_HEADERS", "1")
            // UBAG AI via the web-server passthrough. The internal UBAG gateway is
            // unreachable from a clinician's machine, so the sidecar's UbagClient is
            // pointed at the radiopad.polytronx.com /api/ubag-gw passthrough, which
            // injects the real UBAG secret server-side. The desktop only carries a
            // scoped proxy token (baked at build time from a CI secret — never in
            // source). If the token wasn't baked in, UBAG simply stays unconfigured.
            .env("RADIOPAD_UBAG_BASE_URL", "https://radiopad.polytronx.com/api/ubag-gw")
            // Desktop AI targets. The gateway's deepseek_web extractor was fixed
            // 2026-06-22 (it now returns clean final answers, not the R1 thinking pane),
            // so DeepSeek is a first-class target again alongside Gemini. We deliberately
            // do NOT set RADIOPAD_UBAG_ALLOWED_TARGETS: with no cap the backend allows any
            // live UBAG catalog target, so any provider the operator logs into via the
            // UBAG Chromium session is usable immediately and surfaces in the picker
            // automatically (UbagProviderDiscoveryService). The ordered web-chain runs the
            // two curated primaries.
            .env("RADIOPAD_UBAG_ORDERED_TARGETS", "gemini_web,deepseek_web")
            .env("RADIOPAD_UBAG_API_VERSION", "2026-05-22");
        if let Some(token) = option_env!("RADIOPAD_DESKTOP_PROXY_TOKEN") {
            // Strip a UTF-8 BOM (U+FEFF) and surrounding whitespace. A CI secret
            // set via a BOM-prepending pipe bakes the BOM into the literal, which
            // corrupts the Authorization header (U+FEFF is invalid in HTTP header
            // values) and silently breaks every UBAG call.
            let token = token.trim_matches(|c: char| c == '\u{feff}' || c.is_whitespace());
            if !token.is_empty() {
                command = command.env("RADIOPAD_UBAG_AUTH_SECRET", token);
            }
        }
        if let Some(ref conn) = db_conn {
            command = command.env("RADIOPAD_DB", conn);
        }

        let (mut rx, child) = match command.spawn() {
            Ok(spawned) => spawned,
            Err(e) => {
                emit_status(
                    &app,
                    "failed",
                    Some(format!("failed to start backend sidecar: {e}")),
                    restart_count,
                );
                break;
            }
        };

        let _child = child;
        let stop_health = Arc::new(AtomicBool::new(false));
        spawn_health_loop(app.clone(), bind.clone(), stop_health.clone(), restart_count);

        while let Some(event) = rx.recv().await {
            match event {
                CommandEvent::Stderr(line) => {
                    tracing::info!("radiopad-api: {}", String::from_utf8_lossy(&line));
                }
                CommandEvent::Terminated(status) => {
                    tracing::warn!("radiopad-api exited: {status:?}");
                    break;
                }
                _ => {}
            }
        }
        stop_health.store(true, Ordering::Relaxed);

        restart_count += 1;
        if restart_count > MAX_RESTARTS {
            emit_status(
                &app,
                "failed",
                Some("backend sidecar exited repeatedly; restart RadioPad to try again".into()),
                restart_count,
            );
            break;
        }

        emit_status(&app, "restarting", None, restart_count);
        tokio::time::sleep(restart_delay(restart_count)).await;
    }
}
