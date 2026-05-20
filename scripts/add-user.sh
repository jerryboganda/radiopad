#!/bin/sh
set -eu
DB_PATH=${RADIOPAD_DB_PATH:-/opt/radiopad/data/radiopad.db}
TENANT_SLUG=${RADIOPAD_TENANT_SLUG:-dev}
USER_EMAIL=${RADIOPAD_DEV_USER_EMAIL:?set RADIOPAD_DEV_USER_EMAIL}
USER_DISPLAY=${RADIOPAD_DEV_USER_DISPLAY:-Test Radiologist}

# Get the dev tenant ID
TENANT_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM Tenants WHERE Slug='$TENANT_SLUG' LIMIT 1;")
echo "Tenant ID: $TENANT_ID"

# Check if user already exists
EXISTING=$(sqlite3 "$DB_PATH" "SELECT Email FROM Users WHERE Email='$USER_EMAIL' AND TenantId='$TENANT_ID';")
if [ -n "$EXISTING" ]; then
  echo "User already exists: $EXISTING"
  exit 0
fi

# Generate a UUID for the new user
USER_ID=$(cat /proc/sys/kernel/random/uuid)
NOW=$(date +%s)

# Insert the user
sqlite3 "$DB_PATH" "INSERT INTO Users (Id, TenantId, Email, DisplayName, Role, IsActive, MfaEnabled, CreatedAt, UpdatedAt, FailedLoginCount, SessionEpoch) VALUES ('$USER_ID', '$TENANT_ID', '$USER_EMAIL', '$USER_DISPLAY', 'Radiologist', 1, 0, $NOW, $NOW, 0, 0);"

echo "User added: $USER_EMAIL (ID: $USER_ID)"

# Verify
sqlite3 "$DB_PATH" "SELECT Email, DisplayName, Role FROM Users WHERE TenantId='$TENANT_ID';"
