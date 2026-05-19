#!/usr/bin/env python3
"""Patch login page error handling and backend expiry on VPS."""
import sys

# Fix login page error handling
f = '/opt/radiopad/src/frontend/app/login/page.tsx'
with open(f) as fh:
    src = fh.read()

old_catch = "const ex = e as { body?: { error?: string }; message?: string };\n        setErr(ex.body?.error || ex.message || 'The magic link could not be used.');"

new_catch = "const ex = e as { body?: { error?: string } | string; status?: number; message?: string };\n        const bodyErr = typeof ex.body === 'object' && ex.body?.error ? ex.body.error : null;\n        setErr(bodyErr || (ex.status === 401 ? 'This sign-in link has expired or was already used. Please request a new one below.' : ex.message || 'The magic link could not be used.'));"

if old_catch in src:
    src = src.replace(old_catch, new_catch)
    with open(f, 'w') as fh:
        fh.write(src)
    print('OK: Login page error handling fixed')
else:
    print('WARN: old_catch not found in login page, checking...')
    for i, line in enumerate(src.splitlines()):
        if 'setErr' in line and 'magic' in line.lower():
            print(f'  line {i+1}: {line.strip()}')
    if 'bodyErr' in src:
        print('  (already patched)')
    else:
        sys.exit(1)

# Fix backend: expiry 15 -> 60 min and email text
api = '/opt/radiopad/src/backend/RadioPad.Api/src/RadioPad.Api/Controllers/AuthFlowsController.cs'
with open(api) as fh:
    code = fh.read()

changed = False
if 'AddMinutes(15)' in code:
    code = code.replace('AddMinutes(15)', 'AddMinutes(60)')
    changed = True
    print('OK: Expiry changed to 60 minutes')
else:
    print('INFO: Expiry already updated (no AddMinutes(15) found)')

if '15 minutes' in code:
    code = code.replace('15 minutes', '60 minutes')
    changed = True
    print('OK: Email text updated to 60 minutes')
else:
    print('INFO: Email text already uses 60 minutes')

if changed:
    with open(api, 'w') as fh:
        fh.write(code)

print('\nAll patches applied successfully.')
