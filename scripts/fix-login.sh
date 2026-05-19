#!/bin/bash
set -e

FILE="/opt/radiopad/src/frontend/app/login/page.tsx"

# Fix 1: Better error handling in magic link consume catch block
sed -i '/\.catch((e: unknown) => {/,/setErr(/ {
  s/const ex = e as { body?: { error?: string }; message?: string };/const ex = e as { body?: { error?: string } | string; status?: number; message?: string };/
  s|setErr(ex.body?.error || ex.message || .The magic link could not be used..);|const bodyErr = typeof ex.body === '\''object'\'' \&\& ex.body?.error ? ex.body.error : null;\n        setErr(bodyErr || (ex.status === 401 ? '\''This sign-in link has expired or was already used. Please request a new one below.'\'' : ex.message || '\''The magic link could not be used.'\''));|
}' "$FILE"

echo "=== Login page fix applied ==="
grep -n 'bodyErr\|setErr\|expired' "$FILE" | head -20

# Fix 2: Increase magic link expiry from 15 to 60 minutes
API="/opt/radiopad/src/backend/RadioPad.Api/src/RadioPad.Api/Controllers/AuthFlowsController.cs"
sed -i 's/ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)/ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60)/' "$API"
echo "=== Expiry updated ==="
grep -n 'AddMinutes' "$API"

# Fix 3: Update the HTML email to say "60 minutes" instead of "15 minutes"
sed -i 's/15 minutes/60 minutes/g' "$API"
echo "=== Email text updated ==="
grep -n '60 minutes' "$API"
