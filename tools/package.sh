#!/usr/bin/env bash
# Turn Builds/ into distributable zips in dist/.  Usage: package.sh [macos|windows|all]
#
# macOS MUST go through ditto: a plain `zip -r` drops the executable bit on
# Contents/MacOS/Vent and flattens the framework symlinks, producing a bundle that
# will not launch on the other side.
source "$(dirname "${BASH_SOURCE[0]}")/release-lib.sh"

WHAT="${1:-all}"
VERSION="$(vent_version)"
[[ -n "$VERSION" ]] || { echo "Could not determine the version." >&2; exit 1; }

mkdir -p "$DIST_DIR"

package_macos() {
  local app="$PROJECT_DIR/Builds/Vent.app"
  local zip="$DIST_DIR/$(macos_zip_name "$VERSION")"
  [[ -d "$app" ]] || { echo "No macOS build at $app; run 'make build'." >&2; return 1; }

  # An unsigned arm64 binary will not launch at all, so make sure the ad-hoc signature
  # the editor applies is actually intact before we ship it.
  if ! codesign --verify --deep "$app" 2>/dev/null; then
    echo "  re-applying ad-hoc signature"
    codesign --force --deep --sign - "$app"
  fi

  rm -f "$zip"
  ditto -c -k --keepParent --sequesterRsrc "$app" "$zip"
  echo "  $(basename "$zip")  $(vent_size "$zip") bytes"
}

package_windows() {
  local src="$PROJECT_DIR/Builds/Windows"
  local dirname zip stage
  dirname="$(windows_dir_name "$VERSION")"
  zip="$DIST_DIR/$(windows_zip_name "$VERSION")"
  stage="$DIST_DIR/.stage/$dirname"
  [[ -d "$src" ]] || { echo "No Windows build at $src; run 'make build-windows'." >&2; return 1; }

  # Nest under one folder: the archive holds ~194 loose files and unzipping it flat
  # scatters them across the player's Downloads. Vent.exe must stay next to Vent_Data.
  rm -rf "$DIST_DIR/.stage" "$zip"
  mkdir -p "$stage"
  ( cd "$src" && tar -cf - --exclude='*_BurstDebugInformation_DoNotShip' --exclude='.DS_Store' . ) \
    | ( cd "$stage" && tar -xf - )

  ( cd "$DIST_DIR/.stage" && zip -q -r -X "$zip" "$dirname" )
  rm -rf "$DIST_DIR/.stage"
  echo "  $(basename "$zip")  $(vent_size "$zip") bytes"
}

echo "Packaging Vent $VERSION → dist/"
case "$WHAT" in
  macos)   package_macos ;;
  windows) package_windows ;;
  all)     package_macos; package_windows ;;
  *)       echo "Usage: package.sh [macos|windows|all]" >&2; exit 1 ;;
esac
