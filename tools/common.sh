#!/usr/bin/env bash
# Shared locations for the headless scripts.
set -euo pipefail
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "$PROJECT_DIR/ProjectSettings/ProjectVersion.txt")"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity}"
if [[ ! -x "$UNITY" ]]; then
  echo "Unity $UNITY_VERSION not found at $UNITY (set UNITY=/path/to/Unity)" >&2
  exit 1
fi
mkdir -p "$PROJECT_DIR/Logs"
