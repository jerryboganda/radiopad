#!/bin/sh
# Restore / repair the radiopad.polytronx.com origin vhost on the VPS Nginx
# Proxy Manager (container: nginx-proxy-manager-app-1).
#
# DURABILITY: radiopad's server blocks live in their OWN dedicated file
# (radiopad-origin.conf) and are pulled in by a one-line `include` appended to
# the stable http.conf. They are deliberately NOT inlined into http_top.conf —
# that file is periodically full-overwritten by other ad-hoc scripts (e.g. the
# sip.polytronx.com status block), which on 2026-06-28 wiped the radiopad vhost
# and broke desktop login with "Failed to fetch" (TLS unrecognized_name). Keeping
# radiopad in its own included file means a clobber of http_top.conf can no longer
# take it offline.
#
# Run inside the NPM container's data volume (paths are container paths).
set -e

CUSTOM=/data/nginx/custom

# 1) Dedicated radiopad vhost file — nothing else writes this name.
cat > "$CUSTOM/radiopad-origin.conf" << 'NGINX'
# ============================================================
# radiopad.polytronx.com  (origin for Cloudflare)
# Dedicated file — DO NOT inline into http_top.conf/http.conf.
# Anchored via `include` from http.conf (see bottom of this script).
# ============================================================
server {
    listen 80;
    listen [::]:80;
    server_name radiopad.polytronx.com;

    include conf.d/include/letsencrypt-acme-challenge.conf;
    client_max_body_size 25m;

    location /_next/static/ {
        proxy_pass http://radiopad-web:80;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
        add_header Cache-Control "public, max-age=31536000, immutable" always;
    }

    location / {
        proxy_pass http://radiopad-web:80;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header X-Forwarded-Host $host;
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
        proxy_buffering off;
        proxy_hide_header Cache-Control;
        add_header Cache-Control "no-store, no-cache, must-revalidate, max-age=0" always;
    }
}

server {
    listen 443 ssl;
    listen [::]:443 ssl;
    http2 on;
    server_name radiopad.polytronx.com;

    ssl_certificate /data/custom_ssl/radiopad/fullchain.pem;
    ssl_certificate_key /data/custom_ssl/radiopad/privkey.pem;
    include conf.d/include/ssl-ciphers.conf;
    include conf.d/include/block-exploits.conf;

    client_max_body_size 25m;
    access_log /data/logs/radiopad_access.log proxy;
    error_log /data/logs/radiopad_error.log warn;

    location /_next/static/ {
        proxy_pass http://radiopad-web:80;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_hide_header Cache-Control;
        add_header Cache-Control "public, max-age=31536000, immutable" always;
    }

    location / {
        proxy_pass http://radiopad-web:80;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
        proxy_buffering off;
        proxy_hide_header Cache-Control;
        add_header Cache-Control "no-store, no-cache, must-revalidate, max-age=0" always;
    }
}
NGINX

# 2) Anchor the include from the stable http.conf (idempotent).
if ! grep -q "radiopad-origin.conf" "$CUSTOM/http.conf" 2>/dev/null; then
    printf '\n# RadioPad origin vhost (isolated from http_top.conf clobbers)\ninclude %s/radiopad-origin.conf;\n' "$CUSTOM" >> "$CUSTOM/http.conf"
fi

# 3) Validate + reload.
nginx -t && nginx -s reload && echo "OK: radiopad-origin.conf installed + included from http.conf"
