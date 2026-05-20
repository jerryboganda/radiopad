#!/bin/sh
# Test SMTP connectivity on port 465 (SMTPS - direct SSL).
# Requires RADIOPAD_SMTP_USER, RADIOPAD_SMTP_PASS, RADIOPAD_SMTP_FROM, and RADIOPAD_SMTP_TEST_TO.
SMTP_HOST=${RADIOPAD_SMTP_HOST:-smtp.example.com}
SMTP_PORT=${RADIOPAD_SMTP_PORT:-465}
SMTP_USER=${RADIOPAD_SMTP_USER:?set RADIOPAD_SMTP_USER}
SMTP_PASS=${RADIOPAD_SMTP_PASS:?set RADIOPAD_SMTP_PASS}
SMTP_FROM=${RADIOPAD_SMTP_FROM:?set RADIOPAD_SMTP_FROM}
SMTP_TO=${RADIOPAD_SMTP_TEST_TO:?set RADIOPAD_SMTP_TEST_TO}

timeout 10 curl -v "smtps://${SMTP_HOST}:${SMTP_PORT}" \
  --user "${SMTP_USER}:${SMTP_PASS}" \
  --mail-from "${SMTP_FROM}" \
  --mail-rcpt "${SMTP_TO}" \
  -T - <<'MAILEOF'
From: RadioPad SMTP Test <no-reply@example.com>
To: Operator <operator@example.com>
Subject: RadioPad SMTP Test

This is a synthetic RadioPad SMTP connectivity test. Do not include PHI.
MAILEOF
echo "EXIT: $?"
