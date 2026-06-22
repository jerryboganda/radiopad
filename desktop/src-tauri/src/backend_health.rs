use std::io::{Read, Write};
use std::net::{TcpStream, ToSocketAddrs};
use std::time::Duration;

pub const DEFAULT_BACKEND_URL: &str = "http://127.0.0.1:7457";

#[derive(Debug, Clone)]
struct BackendEndpoint {
    host: String,
    port: u16,
    host_header: String,
}

fn parse_backend_endpoint(url: &str) -> Result<BackendEndpoint, String> {
    let trimmed = url.trim();
    let without_scheme = trimmed
        .split_once("://")
        .map(|(_, rest)| rest)
        .unwrap_or(trimmed);
    let authority = without_scheme
        .split('/')
        .next()
        .filter(|s| !s.is_empty())
        .unwrap_or("127.0.0.1:7457");
    let (host, port) = authority
        .rsplit_once(':')
        .map(|(h, p)| -> Result<(String, u16), String> {
            let port = p.parse::<u16>().map_err(|_| "invalid backend port".to_string())?;
            Ok((h.trim_matches(&['[', ']'][..]).to_string(), port))
        })
        .transpose()?
        .unwrap_or_else(|| {
            let default_port = if trimmed.starts_with("https://") { 443 } else { 80 };
            (authority.trim_matches(&['[', ']'][..]).to_string(), default_port)
        });
    if host.is_empty() {
        return Err("backend host is empty".into());
    }
    Ok(BackendEndpoint {
        host,
        port,
        host_header: authority.to_string(),
    })
}

fn check_ready(url: &str) -> Result<(), String> {
    let endpoint = parse_backend_endpoint(url)?;
    let mut addrs = (endpoint.host.as_str(), endpoint.port)
        .to_socket_addrs()
        .map_err(|e| format!("resolve backend: {e}"))?;
    let addr = addrs
        .next()
        .ok_or_else(|| "backend address did not resolve".to_string())?;
    let mut stream = TcpStream::connect_timeout(&addr, Duration::from_millis(900))
        .map_err(|e| format!("connect backend: {e}"))?;
    stream
        .set_read_timeout(Some(Duration::from_millis(900)))
        .map_err(|e| format!("set read timeout: {e}"))?;
    stream
        .set_write_timeout(Some(Duration::from_millis(900)))
        .map_err(|e| format!("set write timeout: {e}"))?;
    let request = format!(
        "GET /api/health/ready HTTP/1.1\r\nHost: {}\r\nConnection: close\r\n\r\n",
        endpoint.host_header
    );
    stream
        .write_all(request.as_bytes())
        .map_err(|e| format!("write health request: {e}"))?;
    let mut buf = [0u8; 96];
    let n = stream
        .read(&mut buf)
        .map_err(|e| format!("read health response: {e}"))?;
    let head = String::from_utf8_lossy(&buf[..n]);
    if head.starts_with("HTTP/1.1 200") || head.starts_with("HTTP/1.0 200") {
        Ok(())
    } else {
        Err("backend readiness endpoint is not ready".into())
    }
}

pub async fn check_ready_async(url: &str) -> Result<(), String> {
    let url = url.to_string();
    tokio::task::spawn_blocking(move || check_ready(&url))
        .await
        .map_err(|e| format!("health task failed: {e}"))?
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_default_backend_url() {
        let parsed = parse_backend_endpoint(DEFAULT_BACKEND_URL).expect("parse");
        assert_eq!(parsed.host, "127.0.0.1");
        assert_eq!(parsed.port, 7457);
        assert_eq!(parsed.host_header, "127.0.0.1:7457");
    }

    #[test]
    fn parses_url_without_path() {
        let parsed = parse_backend_endpoint("http://localhost:8123/api").expect("parse");
        assert_eq!(parsed.host, "localhost");
        assert_eq!(parsed.port, 8123);
        assert_eq!(parsed.host_header, "localhost:8123");
    }
}
