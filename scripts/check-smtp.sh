#!/bin/sh
SMTP_HOST=${RADIOPAD_SMTP_HOST:-smtp.example.com}
SMTP_PORT=${RADIOPAD_SMTP_PORT:-587}

# Check SMTP env vars
echo "=== ENV ==="
env | grep -iE 'SMTP|PUBLIC'
echo ""

# Test SMTP connectivity
echo "=== SMTP test ==="
timeout 5 sh -c "echo QUIT | nc '$SMTP_HOST' '$SMTP_PORT'" 2>&1 || echo "SMTP connection FAILED/TIMEOUT"
