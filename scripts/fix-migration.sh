#!/bin/sh
sqlite3 /opt/radiopad/data/radiopad.db "INSERT INTO __EFMigrationsHistory VALUES('20260520095000_TenantSettingsPacsVendor','8.0.10');"
echo "Migration record inserted: $?"
