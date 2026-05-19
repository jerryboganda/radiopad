#!/usr/bin/env python3
"""Add CF cache-busting headers to radiopad NPM config."""

NPM_CONF = '/data/nginx/custom/http_top.conf'
with open(NPM_CONF) as f:
    conf = f.read()

# Replace the location / block in the radiopad HTTPS server with cache headers
old_location = """    location / {
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
    }"""

new_location = """    # Static assets — immutable hashed filenames, long cache
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

    # HTML pages — must not be cached by CF edge
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
    }"""

if old_location in conf:
    conf = conf.replace(old_location, new_location)
    with open(NPM_CONF, 'w') as f:
        f.write(conf)
    print('OK: NPM config updated with CF cache-busting headers')
else:
    print('ERROR: old location block not found')
    # Show what we have
    import re
    m = re.search(r'location / \{.*?\}', conf[conf.find('radiopad.polytronx.com'):], re.DOTALL)
    if m:
        print(m.group(0)[:200])
