#!/usr/bin/env bash
# Shared release/packaging helpers. Deliberately does NOT require Unity: the CI job that
# assembles a release downloads prebuilt artifacts and never installs an editor.
set -euo pipefail
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST_DIR="$PROJECT_DIR/dist"

# The version being packaged: VENT_VERSION if the caller set it (release.sh, CI),
# otherwise whatever the project is currently stamped with.
vent_version() {
  if [[ -n "${VENT_VERSION:-}" ]]; then
    echo "${VENT_VERSION}"
  else
    sed -n 's/^  bundleVersion: //p' "$PROJECT_DIR/ProjectSettings/ProjectSettings.asset"
  fi
}

# owner/repo, so the generated manifest points at the right release assets.
vent_repo() {
  if [[ -n "${VENT_REPO:-}" ]]; then
    echo "${VENT_REPO}"
  elif [[ -n "${GITHUB_REPOSITORY:-}" ]]; then
    echo "${GITHUB_REPOSITORY}"
  else
    git -C "$PROJECT_DIR" remote get-url origin \
      | sed -E 's#^.*github\.com[:/]##; s#\.git$##'
  fi
}

# shasum on macOS, sha256sum on the Linux runners. Prints the bare hash.
vent_sha256() {
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | awk '{print $1}'
  else
    sha256sum "$1" | awk '{print $1}'
  fi
}

vent_size() {
  # BSD stat wants -f%z and GNU stat wants -c%s, and GNU's -f means something else entirely
  # (filesystem status) rather than failing — so it would happily print nonsense. wc is portable.
  wc -c < "$1" | tr -d ' '
}

# Asset names are part of the update manifest contract; both the packager and the
# manifest writer derive them from here so they cannot drift apart.
macos_zip_name()   { echo "Vent-$1-macOS.zip"; }
windows_dir_name() { echo "Vent-$1-Windows-x64"; }
windows_zip_name() { echo "Vent-$1-Windows-x64.zip"; }
