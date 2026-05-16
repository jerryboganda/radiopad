#!/usr/bin/env sh
set -eu
HOOK_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
node "$HOOK_DIR/session-start.mjs"