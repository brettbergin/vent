#!/usr/bin/env bash
# Regenerate every asset, prefab and scene from code (headless).
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$PROJECT_DIR" \
  -executeMethod Vent.Editor.Bootstrap.RebuildAll \
  -logFile "$PROJECT_DIR/Logs/regen.log"
echo "Regeneration finished (log: Logs/regen.log)"
