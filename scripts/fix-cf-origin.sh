#!/bin/sh
# Fix: Make port 80 serve content directly (for CF Flexible SSL mode)
# CF connects to origin on port 80 when SSL mode is Flexible
cat > /data/nginx/custom/http_top.conf << 'NGINX'
gzip_types text/plain text/css application/javascript application/json application/xml image/svg+xml text/javascript application/x-javascript;
gzip_vary on;
gzip_min_length 1024;
gzip_proxied any;

server {
    listen 80;
    listen [::]:80;
    server_name radiology.gmcg.edu.pk;

    include conf.d/include/letsencrypt-acme-challenge.conf;

    location / {
        return 301 https://$host$request_uri;
    }
}

server {
    listen 443 ssl;
    listen [::]:443 ssl;
    server_name radiology.gmcg.edu.pk;

    ssl_certificate /etc/letsencrypt/live/radiology.gmcg.edu.pk/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/radiology.gmcg.edu.pk/privkey.pem;
    location / {
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_pass http://172.17.0.1:8092;
    }
}

# --- radiopad.polytronx.com ---
# Port 80: serve content directly (CF Flexible SSL connects here)
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
        add_header CDN-Cache-Control "public, max-age=31536000, immutable" always;
    }

    location / {
        proxy_pass http://radiopad-web:80;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header X-Forwarded-Host $host;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
        proxy_buffering off;
        proxy_hide_header Cache-Control;
        add_header Cache-Control "no-store, no-cache, must-revalidate, max-age=0" always;
        add_header CDN-Cache-Control "no-store" always;
    }
}

# Port 443: same content over HTTPS (CF Full SSL connects here)
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
        add_header CDN-Cache-Control "public, max-age=31536000, immutable" always;
    }

    location / {
        proxy_pass http://radiopad-web:80;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
        proxy_buffering off;
        proxy_hide_header Cache-Control;
        add_header Cache-Control "no-store, no-cache, must-revalidate, max-age=0" always;
        add_header CDN-Cache-Control "no-store" always;
    }
}
# --- end radiopad.polytronx.com ---
NGINX

nginx -t && nginx -s reload && echo "OK: NPM config updated - port 80 now serves content directly"
