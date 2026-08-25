#!/usr/bin/env bash
# Build the Windows x64 player into Builds/Windows/Vent.exe (headless, cross-built from macOS).
# Needs the "Windows Build Support (Mono)" module for this Unity version (Unity Hub ▸ Installs ▸ Add modules).
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
if [[ ! -d "$(dirname "$UNITY")/../PlaybackEngines/WindowsStandaloneSupport" && ! -d "/Applications/Unity/Hub/Editor/$UNITY_VERSION/PlaybackEngines/WindowsStandaloneSupport" ]]; then
  echo "Windows Build Support is not installed for Unity $UNITY_VERSION. Add it in Unity Hub (Installs ▸ ⚙ ▸ Add modules ▸ Windows Build Support (Mono))." >&2
  exit 1
fi
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$PROJECT_DIR" \
  -executeMethod Vent.Editor.BuildScript.BuildWindows \
  -logFile "$PROJECT_DIR/Logs/build-windows.log"
echo "Build finished: $PROJECT_DIR/Builds/Windows/Vent.exe (log: Logs/build-windows.log)"
