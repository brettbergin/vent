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
  python3 - "$PROJECT_DIR/TestResults/$platform.xml" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
print(f"  total={root.get('total')} passed={root.get('passed')} failed={root.get('failed')} skipped={root.get('skipped')}")
for tc in root.iter('test-case'):
    if tc.get('result') != 'Passed':
        msg = tc.find('.//message')
        print(f"  FAIL {tc.get('fullname')}: {(msg.text or '').strip()[:300] if msg is not None else ''}")
PY
done
exit $status
