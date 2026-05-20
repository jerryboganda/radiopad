#!/bin/sh
set -eu
BASE_URL=${RADIOPAD_API_BASE_URL:-http://radiopad-api:7457}
TENANT=${RADIOPAD_TENANT_SLUG:-dev}
EMAIL=${RADIOPAD_DEV_USER_EMAIL:?set RADIOPAD_DEV_USER_EMAIL}
CALLBACK=${RADIOPAD_MAGIC_CALLBACK_URL:-http://localhost:3000/login}

curl -sS "${BASE_URL}/api/auth/magic-link/request" \
  -X POST \
  -H "Content-Type: application/json" \
  -d "{\"tenant\":\"${TENANT}\",\"email\":\"${EMAIL}\",\"callbackUrl\":\"${CALLBACK}\"}"
