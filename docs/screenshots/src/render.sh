#!/usr/bin/env bash
# Regenerate PNG mock-ups from HTML sources.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"
if [[ ! -d node_modules/playwright-core ]]; then
  npm install
fi
node render-screenshots.mjs
