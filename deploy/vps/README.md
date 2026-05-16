# RadioPad — VPS Deployment (Nginx Proxy Manager pattern)

This directory contains the deployment manifests for a VPS that uses
**Nginx Proxy Manager** (NPM) for TLS termination and domain routing.
This differs from the root `deploy/` directory which uses Caddy for TLS.

## Architecture

```
Internet → NPM (port 80/443) → radiopad-web:8093 (nginx + Next.js static)
                                     ↓ /api/* proxy
                               radiopad-api:7457 (ASP.NET Core 8)
                                     ↓
                               /data/radiopad.db (SQLite volume)
```

## Files

| File | Purpose |
|---|---|
| `web.Dockerfile` | Multi-stage: pnpm 9.15.9 + Next.js build → nginx:alpine |
| `api.Dockerfile` | Multi-stage: .NET SDK 8 build → aspnet:8.0 runtime |
| `nginx.conf` | Nginx config: static SPA + `/api/*` proxy to API container |
| `docker-compose.yml` | Compose manifest for VPS (no Caddy — NPM handles TLS) |

## Prerequisites on VPS

- Docker 26+ with Compose plugin
- Nginx Proxy Manager running on ports 80/443
- SSH access to VPS (SSH key at `~/.ssh/id_ed25519`)

## First-time setup

```bash
mkdir -p /opt/radiopad/data
cd /opt/radiopad
git clone https://github.com/Sub-organization-maternal-mind/radiopad.git src

# Create secrets file
cat > .secrets.env << 'EOF'
RADIOPAD_COLUMN_KEK=<generate-with: openssl rand -base64 32>
# Optional: ANTHROPIC_API_KEY=...
EOF
chmod 600 .secrets.env

# Build and start
cd src
docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env up -d --build
```

## Updating

```bash
cd /opt/radiopad
# Backup VPS-specific config
cp -r src/.deploy /tmp/deploy-bak

# Pull latest
rm -rf /tmp/fresh && git clone --depth=1 https://github.com/Sub-organization-maternal-mind/radiopad.git /tmp/fresh
rsync -av --exclude='.deploy' /tmp/fresh/ src/ --delete

# Restore VPS config (if using .deploy/ instead of deploy/vps/)
cp -r /tmp/deploy-bak src/.deploy

# Rebuild
cd src
docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env down
docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env up -d --build --no-cache
```

## NPM Proxy Host

Add a proxy host in Nginx Proxy Manager:
- **Domain**: `radiopad.your-domain.com`
- **Scheme**: `http`
- **Forward Hostname/IP**: `127.0.0.1`
- **Forward Port**: `8093`
- **SSL**: Let's Encrypt (recommended)

## Health check

```bash
curl http://127.0.0.1:8093/api/health
# Expected: {"status":"ok","service":"radiopad-api","time":"..."}
```
