#!/bin/sh
sqlite3 /opt/docker/radiopad/data/radiopad.db "SELECT t.Slug, u.Email FROM Tenants t JOIN Users u ON u.TenantId = t.Id LIMIT 5;"
