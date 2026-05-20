#!/bin/sh
# Test consume via the public URL /api/ proxy (same as browser would)
curl -sS http://radiopad-web:80/api/auth/magic-link/consume \
  -X POST \
  -H "Content-Type: application/json" \
  -d '{"token":"test-via-proxy"}'
