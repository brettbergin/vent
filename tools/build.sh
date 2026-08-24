#!/usr/bin/env bash
# Build the macOS player into Builds/Vent.app (headless).
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$PROJECT_DIR" \
  -executeMethod Vent.Editor.BuildScript.BuildMacOS \
  -logFile "$PROJECT_DIR/Logs/build.log"
echo "Build finished: $PROJECT_DIR/Builds/Vent.app (log: Logs/build.log)"
