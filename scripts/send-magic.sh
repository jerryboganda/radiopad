#!/bin/bash
set -euo pipefail
BASE_URL=${RADIOPAD_API_BASE_URL:-http://localhost:7457}
TENANT=${RADIOPAD_TENANT_SLUG:-dev}
EMAIL=${RADIOPAD_DEV_USER_EMAIL:?set RADIOPAD_DEV_USER_EMAIL}

curl -sS "${BASE_URL}/api/auth/magic-link/request" -X POST \
  -H "Content-Type: application/json" \
  -d "{\"tenant\":\"${TENANT}\",\"email\":\"${EMAIL}\"}"
