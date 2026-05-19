#!/bin/sh
# Add a unique build header to radiopad-web nginx responses
sed -i 's/add_header Cache-Control "no-cache" always;/add_header Cache-Control "no-cache" always;\n        add_header X-RadioPad-Build "20260519-v2" always;/' /etc/nginx/conf.d/default.conf
nginx -t && nginx -s reload && echo "OK: build header added"
