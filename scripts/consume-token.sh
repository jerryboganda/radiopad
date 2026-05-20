#!/bin/sh
curl -sS http://radiopad-api:7457/api/auth/magic-link/consume \
  -X POST \
  -H "Content-Type: application/json" \
  -d '{"token":"ml_Dsgtbm9dCkaM16zhM6VyQVJ1ASazB8zbb6tf2IQp9i8"}'
