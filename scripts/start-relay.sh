#!/bin/bash
set -euo pipefail
SMTP_HOST=${RADIOPAD_SMTP_HOST:-smtp.example.com}
SMTP_PORT=${RADIOPAD_SMTP_PORT:-587}
RELAY_PORT=${RADIOPAD_SMTP_RELAY_PORT:-2525}

# Kill any existing relay
pkill -f "socat.*${RELAY_PORT}" 2>/dev/null
sleep 1

# Start socat relay for local SMTP connectivity testing.
socat "TCP-LISTEN:${RELAY_PORT},fork,reuseaddr" "TCP:${SMTP_HOST}:${SMTP_PORT}" &
RELAY_PID=$!
sleep 2

# Verify it's running
if kill -0 $RELAY_PID 2>/dev/null; then
  echo "RELAY OK: PID=$RELAY_PID"
  ss -tlnp | grep "${RELAY_PORT}" || netstat -tlnp | grep "${RELAY_PORT}"
else
  echo "RELAY FAILED"
fi
