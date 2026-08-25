#!/usr/bin/env python3
"""Print a one-line summary (and any non-passing cases) for a Unity Test Framework NUnit XML file."""
import sys
import xml.etree.ElementTree as ET

path = sys.argv[1]
try:
    root = ET.parse(path).getroot()
except (OSError, ET.ParseError) as e:
    print(f"  no results at {path} ({e}); the editor probably failed to start — check Logs/", file=sys.stderr)
    sys.exit(1)

print(f"  total={root.get('total')} passed={root.get('passed')} failed={root.get('failed')} skipped={root.get('skipped')}")
for tc in root.iter('test-case'):
    if tc.get('result') != 'Passed':
        msg = tc.find('.//message')
        text = (msg.text or '').strip()[:300] if msg is not None else ''
        print(f"  {tc.get('result').upper()} {tc.get('fullname')}: {text}")
sys.exit(1 if int(root.get('failed') or 0) else 0)
