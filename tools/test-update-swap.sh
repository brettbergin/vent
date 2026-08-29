#!/usr/bin/env bash
# End-to-end check of the macOS half of the updater, without publishing anything.
#
# Builds a throwaway "installed" copy of the app, packages a second copy as the update,
# generates the REAL helper script out of Vent.Core.Updates, runs it, and asserts the
# install was replaced and relaunched. This is the part that would eat someone's app.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

# Normalise: TMPDIR already ends in a slash, and a doubled slash in the path stops
# pgrep matching the launched process later on.
WORK="$(cd "${TMPDIR:-/tmp}" && pwd)/vent-update-test"
INSTALL="$WORK/My Games"          # a space, on purpose: that is the quoting bug that hurts
APP="$INSTALL/Vent.app"
STAGE="$WORK/updates/stage"
LOG="$WORK/updates/update.log"
SCRIPT="$WORK/updates/update.sh"

[[ -d "$PROJECT_DIR/Builds/Vent.app" ]] || { echo "No build at Builds/Vent.app; run 'make build'." >&2; exit 1; }

rm -rf "$WORK"
mkdir -p "$INSTALL" "$WORK/updates"

echo "==> Installing a throwaway copy at $APP"
ditto "$PROJECT_DIR/Builds/Vent.app" "$APP"

echo "==> Packaging it again as the update payload"
ZIP="$WORK/updates/Vent-update.zip"
ditto -c -k --keepParent --sequesterRsrc "$PROJECT_DIR/Builds/Vent.app" "$ZIP"

# Mark the installed copy so we can prove it was actually replaced rather than left alone.
MARKER="$APP/Contents/Resources/OLD-VERSION-MARKER"
touch "$MARKER"

echo "==> Generating the helper script from Vent.Core.Updates"
# A pid that has already exited, so the helper's wait loop falls straight through.
DEAD_PID=$( bash -c 'echo $$' )
VENT_DUMP_OUT="$SCRIPT" VENT_DUMP_PID="$DEAD_PID" VENT_DUMP_ZIP="$ZIP" \
VENT_DUMP_STAGE="$STAGE" VENT_DUMP_APP="$APP" VENT_DUMP_LOG="$LOG" \
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT_DIR" \
  -executeMethod Vent.Editor.UpdaterDebug.DumpMacScript \
  -logFile "$PROJECT_DIR/Logs/dump-update-script.log" || true

# Judge on the artifact, not the exit code: the editor sometimes dies noisily on shutdown
# long after the file is on disk.
[[ -s "$SCRIPT" ]] || { echo "The helper script was not generated; see Logs/dump-update-script.log." >&2; exit 1; }
chmod +x "$SCRIPT"

echo "==> Running it"
"$SCRIPT" || { echo "Helper exited $?" >&2; cat "$LOG" 2>/dev/null; exit 1; }

echo "==> Checking the result"
fail=0
check() { if eval "$2"; then echo "  ok   $1"; else echo "  FAIL $1"; fail=1; fi; }

check "the app is still there"            '[[ -d "$APP" ]]'
check "the old copy was replaced"         '[[ ! -e "$MARKER" ]]'
check "the executable survived the swap"  '[[ -x "$APP/Contents/MacOS/Vent" ]]'
check "the signature is still valid"      'codesign --verify --deep "$APP" 2>/dev/null'
check "the rollback copy was cleaned up"  '[[ ! -e "$APP.old" ]]'
check "the staging dir was cleaned up"    '[[ ! -d "$STAGE" ]]'
check "the payload was cleaned up"        '[[ ! -f "$ZIP" ]]'
check "no quarantine flag remains"        '! xattr -p com.apple.quarantine "$APP" >/dev/null 2>&1'

echo "==> Helper log"
sed 's/^/  /' "$LOG"

# The helper ends with `open`, which launches the swapped copy: that it runs at all is the
# real proof the bundle survived. Give it a moment, then close it.
launched=0
for _ in $(seq 1 20); do
  if pgrep -f "$APP/Contents/MacOS/Vent" >/dev/null; then launched=1; break; fi
  sleep 0.5
done
if [[ "$launched" -eq 1 ]]; then
  echo "  ok   the replaced app launched"
  pkill -f "$APP/Contents/MacOS/Vent" || true
  sleep 1
else
  echo "  FAIL the replaced app did not launch"
  fail=1
fi

rm -rf "$WORK"
[[ "$fail" -eq 0 ]] && echo "PASS" || { echo "FAIL"; exit 1; }
