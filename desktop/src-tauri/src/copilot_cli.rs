use std::process::Stdio;
use std::time::Duration;

use serde::Serialize;
use tokio::io::AsyncWriteExt;
use tokio::process::Command;

const DEFAULT_HOST: &str = "github.com";

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CopilotCliStatus {
    pub available: bool,
    pub authenticated: bool,
    pub copilot_available: bool,
    pub host: String,
    pub version: Option<String>,
    pub login: Option<String>,
    pub environment_token_present: bool,
    pub warnings: Vec<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CopilotCommandResult {
    pub ok: bool,
    pub message: String,
}

fn host_or_default(host: Option<String>) -> String {
    let h = host.unwrap_or_else(|| DEFAULT_HOST.to_string());
    if h.trim().is_empty() {
        DEFAULT_HOST.to_string()
    } else {
        h.trim().to_string()
    }
}

fn gh_binary() -> String {
    std::env::var("RADIOPAD_GH_BIN")
        .ok()
        .filter(|v| !v.trim().is_empty())
        .unwrap_or_else(|| "gh".to_string())
}

fn copilot_binary() -> String {
    std::env::var("RADIOPAD_COPILOT_BIN")
        .ok()
        .filter(|v| !v.trim().is_empty())
        .unwrap_or_else(|| "copilot".to_string())
}

fn env_token_present() -> bool {
    ["COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN"]
        .iter()
        .any(|name| std::env::var(name).ok().is_some_and(|v| !v.trim().is_empty()))
}

fn scrub(s: &[u8]) -> String {
    let text = String::from_utf8_lossy(s);
    text.lines()
        .filter(|line| {
            !line.contains("ghp_")
                && !line.contains("github_pat_")
                && !line.to_ascii_lowercase().contains("token")
        })
        .collect::<Vec<_>>()
        .join("\n")
}

async fn run_gh(args: &[&str], stdin: Option<String>, timeout_ms: u64) -> Result<(i32, String, String), String> {
    let mut command = Command::new(gh_binary());
    command.args(args);
    command.stdin(if stdin.is_some() { Stdio::piped() } else { Stdio::null() });
    command.stdout(Stdio::piped());
    command.stderr(Stdio::piped());
    command.kill_on_drop(true);

    let mut child = command.spawn().map_err(|e| format!("GitHub CLI unavailable: {e}"))?;
    if let Some(input) = stdin {
        if let Some(mut writer) = child.stdin.take() {
            writer
                .write_all(input.as_bytes())
                .await
                .map_err(|e| format!("failed to write GitHub CLI stdin: {e}"))?;
        }
    }
    let output = tokio::time::timeout(Duration::from_millis(timeout_ms), child.wait_with_output())
        .await
        .map_err(|_| "GitHub CLI command timed out".to_string())?
        .map_err(|e| format!("GitHub CLI command failed: {e}"))?;
    Ok((output.status.code().unwrap_or(-1), scrub(&output.stdout), scrub(&output.stderr)))
}

async fn run_copilot(args: &[&str], stdin: Option<String>, timeout_ms: u64) -> Result<(i32, String, String), String> {
    let mut command = Command::new(copilot_binary());
    command.args(args);
    command.stdin(if stdin.is_some() { Stdio::piped() } else { Stdio::null() });
    command.stdout(Stdio::piped());
    command.stderr(Stdio::piped());
    command.kill_on_drop(true);

    let mut child = command.spawn().map_err(|e| format!("GitHub Copilot CLI unavailable: {e}"))?;
    if let Some(input) = stdin {
        if let Some(mut writer) = child.stdin.take() {
            writer
                .write_all(input.as_bytes())
                .await
                .map_err(|e| format!("failed to write Copilot prompt to stdin: {e}"))?;
        }
    }
    let output = tokio::time::timeout(Duration::from_millis(timeout_ms), child.wait_with_output())
        .await
        .map_err(|_| "GitHub Copilot CLI command timed out".to_string())?
        .map_err(|e| format!("GitHub Copilot CLI command failed: {e}"))?;
    Ok((output.status.code().unwrap_or(-1), scrub(&output.stdout), scrub(&output.stderr)))
}

fn parse_login(output: &str) -> Option<String> {
    for line in output.lines() {
        if let Some(rest) = line.split("account ").nth(1) {
            let login = rest.split_whitespace().next().unwrap_or("").trim_matches(')');
            if !login.is_empty() {
                return Some(login.to_string());
            }
        }
    }
    None
}

#[tauri::command]
pub async fn copilot_cli_status(host: Option<String>) -> Result<CopilotCliStatus, String> {
    let host = host_or_default(host);
    let version = run_gh(&["--version"], None, 10_000).await;
    let available = version.as_ref().is_ok_and(|(code, _, _)| *code == 0);
    let auth = run_gh(&["auth", "status", "--hostname", &host], None, 10_000).await;
    let authenticated = auth.as_ref().is_ok_and(|(code, _, _)| *code == 0);
    let auth_text = auth.as_ref().map(|(_, stdout, stderr)| format!("{stdout}\n{stderr}")).unwrap_or_default();
    let copilot = run_copilot(&["--help"], None, 10_000).await;
    let copilot_available = copilot.as_ref().is_ok_and(|(code, _, _)| *code == 0);
    let mut warnings = Vec::new();
    if env_token_present() {
        warnings.push("Environment token override detected; tenant policy may block this outside dev/CI.".to_string());
    }
    if !copilot_available && available {
        warnings.push("GitHub CLI is installed, but the Copilot CLI binary is unavailable.".to_string());
    }
    Ok(CopilotCliStatus {
        available,
        authenticated,
        copilot_available,
        host,
        version: version.ok().map(|(_, stdout, _)| stdout.lines().next().unwrap_or("").to_string()),
        login: parse_login(&auth_text),
        environment_token_present: env_token_present(),
        warnings,
    })
}

#[tauri::command]
pub async fn copilot_cli_login_begin(host: Option<String>) -> Result<CopilotCommandResult, String> {
    let host = host_or_default(host);
    let (code, stdout, stderr) = run_gh(&["auth", "login", "--hostname", &host, "--web"], None, 120_000).await?;
    if code != 0 {
        return Err(format!("GitHub CLI login failed: {stderr}"));
    }
    Ok(CopilotCommandResult { ok: true, message: if stdout.is_empty() { "GitHub CLI login completed.".into() } else { stdout } })
}

#[tauri::command]
pub async fn copilot_cli_logout(host: Option<String>) -> Result<CopilotCommandResult, String> {
    let host = host_or_default(host);
    let (code, stdout, stderr) = run_gh(&["auth", "logout", "--hostname", &host], None, 30_000).await?;
    if code != 0 {
        return Err(format!("GitHub CLI logout failed: {stderr}"));
    }
    Ok(CopilotCommandResult { ok: true, message: if stdout.is_empty() { "GitHub CLI logout completed.".into() } else { stdout } })
}

