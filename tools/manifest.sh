#!/usr/bin/env bash
# Write dist/latest.json and dist/SHA256SUMS.txt from the zips already in dist/.
#
# Split out from package.sh because the CI release job assembles the manifest from
# artifacts built by two other jobs, on a runner that has no Unity and no Builds/.
source "$(dirname "${BASH_SOURCE[0]}")/release-lib.sh"

VERSION="$(vent_version)"
REPO="$(vent_repo)"
[[ -n "$VERSION" ]] || { echo "Could not determine the version." >&2; exit 1; }
[[ -n "$REPO"    ]] || { echo "Could not determine the GitHub repo (set VENT_REPO)." >&2; exit 1; }

BASE="https://github.com/$REPO/releases/download/v$VERSION"
MAC_ZIP="$DIST_DIR/$(macos_zip_name "$VERSION")"
WIN_ZIP="$DIST_DIR/$(windows_zip_name "$VERSION")"

for f in "$MAC_ZIP" "$WIN_ZIP"; do
  [[ -f "$f" ]] || { echo "Missing $(basename "$f"); run package.sh first." >&2; exit 1; }
done

# The updater refuses to install an update whose hash does not match, so these are the
# security control, not a convenience.
MAC_SHA="$(vent_sha256 "$MAC_ZIP")";  MAC_SIZE="$(vent_size "$MAC_ZIP")"
WIN_SHA="$(vent_sha256 "$WIN_ZIP")";  WIN_SIZE="$(vent_size "$WIN_ZIP")"

NOTES="${VENT_NOTES:-Vent $VERSION}"

# schema 1: an older client that meets a higher number stops self-updating and just
# links the release page, so bumping this can never brick an installed copy.
cat > "$DIST_DIR/latest.json" <<JSON
{
  "schema": 1,
  "version": "$VERSION",
  "releaseUrl": "https://github.com/$REPO/releases/tag/v$VERSION",
  "notes": $(python3 -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$NOTES"),
  "macos": {
    "url": "$BASE/$(macos_zip_name "$VERSION")",
    "sha256": "$MAC_SHA",
    "sizeBytes": $MAC_SIZE,
    "rootName": "Vent.app"
  },
  "windows": {
    "url": "$BASE/$(windows_zip_name "$VERSION")",
    "sha256": "$WIN_SHA",
    "sizeBytes": $WIN_SIZE,
    "rootName": "$(windows_dir_name "$VERSION")"
  }
}
JSON

python3 -c 'import json,sys; json.load(open(sys.argv[1]))' "$DIST_DIR/latest.json"

( cd "$DIST_DIR" && printf '%s  %s\n' \
    "$MAC_SHA" "$(macos_zip_name "$VERSION")" \
    > SHA256SUMS.txt
  printf '%s  %s\n' "$WIN_SHA" "$(windows_zip_name "$VERSION")" >> SHA256SUMS.txt )

echo "Wrote dist/latest.json and dist/SHA256SUMS.txt for Vent $VERSION ($REPO)"
