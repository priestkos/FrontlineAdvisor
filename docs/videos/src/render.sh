#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
if [[ ! -d node_modules ]]; then
  npm ci
fi
# Playwright bundles ffmpeg for recordVideo (system ffmpeg is used for MP4/GIF encode).
npx playwright install ffmpeg
node render-video.cjs
