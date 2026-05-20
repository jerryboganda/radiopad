#!/bin/sh
set -eu
DB_PATH=${RADIOPAD_DB_PATH:-/opt/radiopad/data/radiopad.db}
TENANT_SLUG=${RADIOPAD_TENANT_SLUG:-dev}
USER_EMAIL=${RADIOPAD_DEV_USER_EMAIL:-test-radiologist@example.local}

# Get the dev tenant ID
TENANT_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM Tenants WHERE Slug='$TENANT_SLUG' LIMIT 1;")
echo "Tenant ID: $TENANT_ID"

# Check if user already exists
EXISTING=$(sqlite3 "$DB_PATH" "SELECT Email FROM Users WHERE Email='$USER_EMAIL' AND TenantId='$TENANT_ID';")
if [ -n "$EXISTING" ]; then
  echo "User already exists: $EXISTING"
  exit 0
fi

# Get schema info for Users table
echo "Schema:"
sqlite3 "$DB_PATH" ".schema Users" | head -20

# Get a sample row to see what fields are needed
echo "Sample row:"
sqlite3 "$DB_PATH" "SELECT * FROM Users LIMIT 1;"
