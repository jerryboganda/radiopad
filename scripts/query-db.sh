#!/bin/sh
sqlite3 /tmp/rp.db "SELECT t.Slug, u.Email FROM Tenants t JOIN Users u ON u.TenantId = t.Id LIMIT 10;"
