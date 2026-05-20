#!/bin/sh
echo "=== Testing port 8025 on 185.252.233.186 ==="
timeout 5 curl -v telnet://185.252.233.186:8025 2>&1 | head -5
echo ""
echo "=== Testing port 443 on google.com ==="
timeout 5 curl -sS -o /dev/null -w "HTTP %{http_code}" https://www.google.com 2>&1
echo ""
echo "=== Testing port 2525 on 185.252.233.186 ==="
timeout 5 curl -v telnet://185.252.233.186:2525 2>&1 | head -5
