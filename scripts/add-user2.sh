#!/bin/sh
set -eu
DB_PATH=${RADIOPAD_DB_PATH:-/opt/radiopad/data/radiopad.db}
TENANT_ID=${RADIOPAD_TENANT_ID:?set RADIOPAD_TENANT_ID}
USER_EMAIL=${RADIOPAD_DEV_USER_EMAIL:?set RADIOPAD_DEV_USER_EMAIL}
USER_DISPLAY=${RADIOPAD_DEV_USER_DISPLAY:-Test Radiologist}
USER_ID=$(cat /proc/sys/kernel/random/uuid | tr '[:lower:]' '[:upper:]')
NOW=$(date +%s)

sqlite3 "$DB_PATH" "INSERT INTO Users (Id, CreatedAt, DisplayName, Email, IsActive, MfaEnabled, MfaSecret, PasswordHash, Role, TenantId, UpdatedAt, FailedLoginCount, SessionEpoch) VALUES ('$USER_ID', $NOW, '$USER_DISPLAY', '$USER_EMAIL', 1, 0, '', '', 4, '$TENANT_ID', $NOW, 0, 0);"

echo "Result: $?"
sqlite3 "$DB_PATH" "SELECT Email, DisplayName FROM Users WHERE TenantId='$TENANT_ID';"
