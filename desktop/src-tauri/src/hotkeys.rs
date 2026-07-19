//! Rebindable system-wide hotkeys (RC-10 / PRD DESK-003).
//!
//! The frontend registry in `frontend/lib/hotkeys.ts` is the single source of truth for what each
//! shortcut *is*: it owns the catalog, the defaults, and the per-device overrides in localStorage.
//! Before this module those overrides only ever reached in-page keydown listeners, so a rebound
//! chord silently kept firing the OLD accelerator whenever the window was unfocused — the case
//! system-wide hotkeys exist for. Here we let the frontend push its effective bindings down to the
//! OS registration, so the two agree.
//!
//! Deliberately stateless across restarts: localStorage stays the only persisted copy, and the
//! frontend re-applies on every launch. A second persisted copy here would be a source of drift.

use std::str::FromStr;
use std::sync::Mutex;

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Emitter, Manager, Runtime};
use tauri_plugin_global_shortcut::{Code, GlobalShortcutExt, Modifiers, Shortcut};

/// One action bound to one accelerator, as sent by the frontend.
#[derive(Debug, Clone, Deserialize)]
pub struct HotkeyBinding {
    /// Action id — must match a `HotkeyDef.id` in `frontend/lib/hotkeys.ts`.
    pub id: String,
    /// Accelerator chord, e.g. "Ctrl+Shift+D".
    pub accelerator: String,
}

/// Per-binding outcome so the settings UI can flag a chord the OS refused (usually because another
/// application already owns it) instead of silently doing nothing.
#[derive(Debug, Clone, Serialize)]
pub struct HotkeyApplyResult {
    pub id: String,
    pub accelerator: String,
    pub ok: bool,
    pub error: Option<String>,
}

/// The live shortcut → action-id table consulted by the global-shortcut handler.
///
/// A `Vec` rather than a `HashMap`: the table holds a handful of entries, so a linear scan is
/// cheaper than hashing and only requires `PartialEq` on `Shortcut`.
#[derive(Default)]
pub struct HotkeyState {
    bindings: Mutex<Vec<(Shortcut, String)>>,
}

impl HotkeyState {
    /// Resolve a pressed shortcut to its action id.
    pub fn action_for(&self, shortcut: &Shortcut) -> Option<String> {
        let guard = self.bindings.lock().ok()?;
        guard
            .iter()
            .find(|(sc, _)| sc == shortcut)
            .map(|(_, id)| id.clone())
    }

    fn replace(&self, next: Vec<(Shortcut, String)>) -> Vec<Shortcut> {
        match self.bindings.lock() {
            Ok(mut guard) => {
                let previous = guard.iter().map(|(sc, _)| *sc).collect();
                *guard = next;
                previous
            }
            // A poisoned lock means a handler panicked mid-read. Registering the new set without
            // unregistering the old is better than leaving the user with no hotkeys at all.
            Err(_) => Vec::new(),
        }
    }
}

/// The actions this build actually implements. An unknown id from the frontend (e.g. one of the
/// "Coming soon" entries) is rejected rather than registered to nothing — otherwise the chord would
/// be taken from the OS while doing nothing at all.
const SUPPORTED_ACTIONS: &[&str] = &[
    "dictation-toggle",
    "new-report",
    "focus-window",
    "generate-impression",
    "rewrite-mode",
    "secure-copy-section",
];

/// Map an action id to the frontend event it emits. `focus-window` is absent because it is handled
/// natively (show + focus) rather than by emitting.
fn event_for(action: &str) -> Option<&'static str> {
    match action {
        "dictation-toggle" => Some("radiopad://dictate"),
        "new-report" => Some("radiopad://new-report"),
        "generate-impression" => Some("radiopad://generate-impression"),
        "rewrite-mode" => Some("radiopad://rewrite"),
        "secure-copy-section" => Some("radiopad://secure-copy-section"),
        _ => None,
    }
}

/// Perform the action bound to `shortcut`, if any. Called from the plugin's handler.
pub fn dispatch<R: Runtime>(app: &AppHandle<R>, shortcut: &Shortcut) {
    let Some(action) = app.state::<HotkeyState>().action_for(shortcut) else {
        return;
    };

    if action == "focus-window" {
        if let Some(win) = app.get_webview_window("main") {
            let _ = win.show();
            let _ = win.set_focus();
        }
        return;
    }

    if let Some(event) = event_for(&action) {
        let _ = app.emit(event, ());
    }
}

/// Parse a frontend accelerator ("Ctrl+Shift+D") into the shortcut(s) to register.
///
/// Returns TWO shortcuts for a Ctrl-prefixed chord — the Control variant and the Super (Win/Cmd)
/// variant — because that is what the app has always registered, and dropping the Super variant
/// here would silently break the muscle memory of anyone using it today.
fn parse_accelerator(accelerator: &str) -> Result<Vec<Shortcut>, String> {
    let primary = Shortcut::from_str(accelerator)
        .map_err(|e| format!("unrecognised accelerator '{accelerator}': {e}"))?;

    let mut out = vec![primary];
    if primary.mods.contains(Modifiers::CONTROL) {
        let supered = Shortcut::new(
            Some(primary.mods - Modifiers::CONTROL | Modifiers::SUPER),
            primary.key,
        );
        out.push(supered);
    }
    Ok(out)
}

/// Replace every registered global hotkey with `bindings`.
///
/// Unregisters the previous set first so a rebind never leaves the old chord live. Each binding is
/// reported independently: one chord the OS refuses does not prevent the rest from registering.
#[tauri::command]
pub fn hotkeys_apply<R: Runtime>(
    app: AppHandle<R>,
    bindings: Vec<HotkeyBinding>,
) -> Vec<HotkeyApplyResult> {
    let mut next: Vec<(Shortcut, String)> = Vec::new();
    let mut results: Vec<HotkeyApplyResult> = Vec::new();

    for binding in &bindings {
        if !SUPPORTED_ACTIONS.contains(&binding.id.as_str()) {
            results.push(HotkeyApplyResult {
                id: binding.id.clone(),
                accelerator: binding.accelerator.clone(),
                ok: false,
                error: Some("action is not implemented by this build".into()),
            });
            continue;
        }

        match parse_accelerator(&binding.accelerator) {
            Ok(shortcuts) => {
                for sc in shortcuts {
                    next.push((sc, binding.id.clone()));
                }
                results.push(HotkeyApplyResult {
                    id: binding.id.clone(),
                    accelerator: binding.accelerator.clone(),
                    ok: true,
                    error: None,
                });
            }
            Err(e) => results.push(HotkeyApplyResult {
                id: binding.id.clone(),
                accelerator: binding.accelerator.clone(),
                ok: false,
                error: Some(e),
            }),
        }
    }

    // Swap the table BEFORE touching the OS so the handler can never see a shortcut that is
    // registered but not yet resolvable to an action.
    let previous = app.state::<HotkeyState>().replace(next.clone());

    for sc in previous {
        let _ = app.global_shortcut().unregister(sc);
    }

    for (sc, id) in &next {
        if let Err(e) = app.global_shortcut().register(*sc) {
            tracing::warn!("global shortcut registration failed for {id}: {e}");
            if let Some(r) = results.iter_mut().find(|r| &r.id == id && r.ok) {
                r.ok = false;
                r.error = Some(format!("the system refused this shortcut: {e}"));
            }
        }
    }

    results
}

/// The built-in defaults, mirroring `frontend/lib/hotkeys.ts`. Registered at startup so the
/// shortcuts work before the webview has loaded and pushed the user's own bindings.
pub fn default_bindings() -> Vec<HotkeyBinding> {
    [
        ("focus-window", "Ctrl+Shift+R"),
        ("new-report", "Ctrl+Shift+N"),
        ("generate-impression", "Ctrl+Shift+I"),
        ("rewrite-mode", "Ctrl+Shift+W"),
        ("dictation-toggle", "Ctrl+Shift+D"),
        ("secure-copy-section", "Ctrl+Shift+C"),
    ]
    .into_iter()
    .map(|(id, accelerator)| HotkeyBinding {
        id: id.into(),
        accelerator: accelerator.into(),
    })
    .collect()
}

/// Registration used by `setup`, bypassing the command wrapper.
pub fn register_defaults<R: Runtime>(app: &AppHandle<R>) {
    let _ = hotkeys_apply(app.clone(), default_bindings());
}

/// Expose the actions this build implements so the settings UI can mark the rest "Coming soon"
/// without duplicating the list.
#[tauri::command]
pub fn hotkeys_supported_actions() -> Vec<String> {
    SUPPORTED_ACTIONS.iter().map(|s| (*s).to_string()).collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ctrl_chord_also_registers_the_super_variant() {
        let parsed = parse_accelerator("Ctrl+Shift+D").expect("should parse");
        assert_eq!(parsed.len(), 2, "expected the Control and Super variants");
        assert!(parsed.iter().any(|s| s.mods.contains(Modifiers::CONTROL)));
        assert!(parsed.iter().any(|s| s.mods.contains(Modifiers::SUPER)));
        assert!(parsed.iter().all(|s| s.key == Code::KeyD));
    }

    #[test]
    fn non_ctrl_chord_registers_once() {
        let parsed = parse_accelerator("Alt+F4").expect("should parse");
        assert_eq!(parsed.len(), 1);
    }

    #[test]
    fn garbage_accelerator_is_rejected_not_panicked_on() {
        assert!(parse_accelerator("NotAKey+++").is_err());
    }

    #[test]
    fn every_default_binding_is_a_supported_action() {
        for b in default_bindings() {
            assert!(
                SUPPORTED_ACTIONS.contains(&b.id.as_str()),
                "default binding '{}' is not in SUPPORTED_ACTIONS",
                b.id
            );
        }
    }

    #[test]
    fn every_supported_action_either_emits_or_is_focus_window() {
        for action in SUPPORTED_ACTIONS {
            assert!(
                event_for(action).is_some() || *action == "focus-window",
                "action '{action}' would register a chord that does nothing"
            );
        }
    }

    #[test]
    fn defaults_parse_and_cover_every_supported_action() {
        let defaults = default_bindings();
        for b in &defaults {
            assert!(parse_accelerator(&b.accelerator).is_ok(), "{}", b.accelerator);
        }
        for action in SUPPORTED_ACTIONS {
            assert!(
                defaults.iter().any(|b| b.id == *action),
                "no default binding for '{action}'"
            );
        }
    }
}
