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

/// Environment the bundled sidecar is launched with.
///
/// This is a CONTRACT with the .NET backend: each `RADIOPAD_*_ENABLED` variable switches on an
/// on-device capability that the backend gates by reading that exact name. The two sides live in
/// different languages and different repos-worth of code, with nothing but this list joining them.
///
/// That gap shipped a real defect: `RADIOPAD_LOCAL_FORMATTER_ENABLED` was never set here, while
/// `LocalMedGemmaFormatter.Available` gated on it — so `POST /api/dictation/draft-local` answered
/// `503 formatter_unavailable` in every build ever shipped. MedGemma was provisioned, pinned,
/// smoke-tested in CI and completely unreachable to a user, because one line was missing from this
/// list. Hence the test below: it asserts the capability flags rather than trusting a reader to
/// notice an absence.
///
/// Note the deliberate asymmetry: this enables the local formatter as a CAPABILITY, so the
/// on-device endpoint serves. It does NOT set `RADIOPAD_LOCAL_FORMATTER_DEFAULT` — cloud stays the
/// default report formatter per decision D1, and the radiologist opts in per draft.
fn sidecar_env(bind: &str, db_conn: Option<&str>) -> Vec<(String, String)> {
    let mut env = vec![
        ("RADIOPAD_BIND".to_string(), bind.to_string()),
        // Boot with local defaults instead of demanding cloud production secrets
        // (RADIOPAD_AUTH_SECRET / RADIOPAD_COLUMN_KEY_*).
        ("ASPNETCORE_ENVIRONMENT".to_string(), "Development".to_string()),
        // On-device, offline STT (MedASR by default, Parakeet/SAPI selectable). The sidecar
        // downloads the model to %LOCALAPPDATA% on first run and decodes the desktop's 16 kHz mono
        // WAV in-process — no ffmpeg, no cloud.
        ("RADIOPAD_LOCAL_STT_ENABLED".to_string(), "1".to_string()),
        // On-device MedGemma report formatting (§4.2 local path), so the transcript never leaves
        // the machine when the radiologist chooses it.
        ("RADIOPAD_LOCAL_FORMATTER_ENABLED".to_string(), "1".to_string()),
    ];
    if let Some(conn) = db_conn {
        env.push(("RADIOPAD_DB".to_string(), conn.to_string()));
    }
    env
}

/// Loopback bind for the bundled sidecar.
///
/// This sidecar is NOT the application's data backend — the desktop UI talks to the hosted
/// production API (see `get_backend_url`). It exists ONLY to run on-device work (dictation
/// transcription and, when the radiologist selects it, MedGemma report formatting), so its bind is
/// a fixed loopback address, deliberately INDEPENDENT of `RADIOPAD_BACKEND` (which points the UI at
/// production). `RADIOPAD_LOCAL_BIND` can override it for local development of the sidecar itself.
fn stt_sidecar_bind() -> String {
    std::env::var("RADIOPAD_LOCAL_BIND").unwrap_or_else(|_| DEFAULT_BACKEND_URL.to_string())
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
/// The STT sidecar never stores clinical data here — this DB only exists so the
/// ASP.NET host boots cleanly; the transcript itself is returned, never persisted.
fn sidecar_db_conn(app: &AppHandle) -> Option<String> {
    let dir = app.path().app_local_data_dir().ok()?;
    if let Err(e) = std::fs::create_dir_all(&dir) {
        tracing::warn!("radiopad-api: could not create data dir {dir:?}: {e}");
        return None;
    }
    Some(format!("Data Source={}", dir.join("radiopad-stt.db").display()))
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
    let bind = stt_sidecar_bind();
    let db_conn = sidecar_db_conn(&app);
    let mut restart_count = 0;

    loop {
        // Adopt an already-healthy sidecar instead of spawning a conflicting one.
        // If a previous shell exited abruptly (crash / force-quit / OS kill), the OS
        // may leave its sidecar child running and still bound to the loopback port.
        // A freshly launched shell that blindly spawns a *second* sidecar would fail
        // to bind that port, crash-loop to MAX_RESTARTS, and raise a false "backend
        // sidecar exited repeatedly" danger banner — even though dictation works fine
        // off the surviving sidecar. Detect that case and monitor the existing
        // process instead of fighting it. (Only RadioPad serves /api/health/ready
        // with 200 on this loopback port, so this won't adopt a stranger.)
        if backend_health::check_ready_async(&bind).await.is_ok() {
            emit_status(&app, "ready", None, restart_count);
            loop {
                tokio::time::sleep(Duration::from_millis(1500)).await;
                if backend_health::check_ready_async(&bind).await.is_err() {
                    break;
                }
            }
            // The adopted sidecar went away — reset and fall through to (re)spawn ours.
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
                    Some(format!("on-device dictation engine unavailable: {e}")),
                    restart_count,
                );
                break;
            }
        };

        // The environment is scoped to this child process only — it does not touch machine env.
        let mut command = sidecar;
        for (key, value) in sidecar_env(&bind, db_conn.as_deref()) {
            command = command.env(key, value);
        }

        let (mut rx, child) = match command.spawn() {
            Ok(spawned) => spawned,
            Err(e) => {
                emit_status(
                    &app,
                    "failed",
                    Some(format!("failed to start on-device dictation engine: {e}")),
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
                Some("on-device dictation engine exited repeatedly; restart RadioPad to try again".into()),
                restart_count,
            );
            break;
        }

        emit_status(&app, "restarting", None, restart_count);
        tokio::time::sleep(restart_delay(restart_count)).await;
    }
}

#[cfg(test)]
mod tests {
    use super::sidecar_env;

    fn value_of<'a>(env: &'a [(String, String)], key: &str) -> Option<&'a str> {
        env.iter().find(|(k, _)| k == key).map(|(_, v)| v.as_str())
    }

    /// Every on-device capability the desktop depends on must actually be switched on for the
    /// sidecar. `RADIOPAD_LOCAL_FORMATTER_ENABLED` was missing for the whole life of the feature,
    /// so `/api/dictation/draft-local` returned 503 in every shipped build while MedGemma sat
    /// downloaded and unreachable. Asserting the flags is the only thing that makes an ABSENCE
    /// visible — nothing else fails when a line is simply not there.
    #[test]
    fn enables_every_on_device_capability() {
        let env = sidecar_env("127.0.0.1:7457", None);
        assert_eq!(value_of(&env, "RADIOPAD_LOCAL_STT_ENABLED"), Some("1"));
        assert_eq!(value_of(&env, "RADIOPAD_LOCAL_FORMATTER_ENABLED"), Some("1"));
        assert_eq!(value_of(&env, "RADIOPAD_BIND"), Some("127.0.0.1:7457"));
        // Boots without cloud production secrets.
        assert_eq!(value_of(&env, "ASPNETCORE_ENVIRONMENT"), Some("Development"));
    }

    /// Decision D1: cloud AI stays primary. Enabling the local formatter as a CAPABILITY must not
    /// also make it the default report formatter — those are separate flags precisely so that
    /// making the on-device endpoint reachable cannot silently reroute every desktop report draft.
    #[test]
    fn does_not_make_the_local_formatter_the_default() {
        let env = sidecar_env("127.0.0.1:7457", None);
        assert_eq!(value_of(&env, "RADIOPAD_LOCAL_FORMATTER_DEFAULT"), None);
    }

    #[test]
    fn passes_the_db_connection_only_when_present() {
        assert_eq!(value_of(&sidecar_env("127.0.0.1:7457", None), "RADIOPAD_DB"), None);
        let with_db = sidecar_env("127.0.0.1:7457", Some("Data Source=x.db"));
        assert_eq!(value_of(&with_db, "RADIOPAD_DB"), Some("Data Source=x.db"));
    }
}
