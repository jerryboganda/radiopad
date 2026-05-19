#!/bin/sh
set -e
cat > /tmp/req.json <<'JSONEOF'
{"tenant":"dev","email":"manwara575@gmail.com"}
JSONEOF
docker cp /tmp/req.json radiopad-api:/tmp/req.json >/dev/null
echo "--- body ---"
docker exec radiopad-api cat /tmp/req.json
echo
echo "--- request ---"
docker exec radiopad-api curl -sS \
  -X POST http://127.0.0.1:7457/api/auth/magic-link/request \
  -H 'Content-Type: application/json' \
  -H 'X-Tenant: dev' \
  --data-binary @/tmp/req.json \
  -w '\nHTTP=%{http_code}\n' --max-time 60
echo "--- logs ---"
sleep 2
docker logs radiopad-api --since 30s 2>&1 | grep -iE 'smtp|magic|mail|warn|error|gmail' | tail -20
