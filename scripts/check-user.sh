#!/bin/sh
sqlite3 /app/data/radiopad.db "SELECT t.Slug, u.Email FROM Tenants t JOIN Users u ON u.TenantId = t.Id WHERE t.Slug='dev' LIMIT 5;"
