#!/usr/bin/env bash
# Run EditMode then PlayMode tests; results land in TestResults/*.xml.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
mkdir -p "$PROJECT_DIR/TestResults"
status=0
for platform in EditMode PlayMode; do
  echo "== $platform =="
  "$UNITY" -batchmode -projectPath "$PROJECT_DIR" \
    -runTests -testPlatform "$platform" \
    -testResults "$PROJECT_DIR/TestResults/$platform.xml" \
    -logFile "$PROJECT_DIR/Logs/test-$platform.log" || status=$?
  python3 "$(dirname "${BASH_SOURCE[0]}")/summarize-tests.py" "$PROJECT_DIR/TestResults/$platform.xml" || status=1
done
exit $status
