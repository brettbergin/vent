#!/usr/bin/env bash
# Cut a release end to end from this machine:  tools/release.sh 0.2.0 [--skip-tests] [--dry-run]
#
# This is the fallback path for the GitHub Actions workflow, and the way the first
# release is cut. It needs Unity (both build modules) and an authenticated `gh`.
source "$(dirname "${BASH_SOURCE[0]}")/release-lib.sh"

VERSION="${1:-}"
shift || true
SKIP_TESTS=0
DRY_RUN=0
for arg in "$@"; do
  case "$arg" in
    --skip-tests) SKIP_TESTS=1 ;;
    --dry-run)    DRY_RUN=1 ;;
    *) echo "Unknown option: $arg" >&2; exit 1 ;;
  esac
done

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Usage: tools/release.sh <major.minor.patch> [--skip-tests] [--dry-run]" >&2
  exit 1
fi

TAG="v$VERSION"
cd "$PROJECT_DIR"

# --- guards ---------------------------------------------------------------
[[ -z "$(git status --porcelain)" ]] || { echo "Working tree is dirty; commit or stash first." >&2; exit 1; }

BRANCH="$(git rev-parse --abbrev-ref HEAD)"
[[ "$BRANCH" == "main" ]] || { echo "On '$BRANCH', not main." >&2; exit 1; }

git rev-parse -q --verify "refs/tags/$TAG" >/dev/null && { echo "Tag $TAG already exists." >&2; exit 1; }
command -v gh >/dev/null || { echo "The gh CLI is required." >&2; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "gh is not authenticated (gh auth login)." >&2; exit 1; }

export VENT_VERSION="$VERSION"
echo "==> Releasing Vent $VERSION as $TAG"

# BuildScript writes bundleVersion into ProjectSettings.asset, so a build dirties the tree.
# The tag is the source of truth for a release, so put the file back on the way out —
# otherwise every release leaves an unrelated edit staged behind it.
restore_project_settings() {
  git -C "$PROJECT_DIR" checkout -- ProjectSettings/ProjectSettings.asset 2>/dev/null || true
}
trap restore_project_settings EXIT

# --- test -----------------------------------------------------------------
if [[ "$SKIP_TESTS" -eq 0 ]]; then
  echo "==> Tests"
  make -C "$PROJECT_DIR" test
else
  echo "==> Tests skipped"
fi

# --- build ----------------------------------------------------------------
# Deliberately no `make regen`: the generated assets are committed, and regenerating
# would rebake lighting into something other than what the tests just ran against.
echo "==> Building macOS"
"$PROJECT_DIR/tools/build.sh"
echo "==> Building Windows"
"$PROJECT_DIR/tools/build-windows.sh"

# --- package --------------------------------------------------------------
echo "==> Packaging"
rm -rf "$DIST_DIR"
"$PROJECT_DIR/tools/package.sh" all
"$PROJECT_DIR/tools/manifest.sh"

# Fail loudly rather than shipping a player stamped with the wrong version: the
# updater compares Application.version against the manifest, so a mismatch would
# make an up-to-date copy offer itself the update forever.
BUILT="$(plutil -extract CFBundleShortVersionString raw "$PROJECT_DIR/Builds/Vent.app/Contents/Info.plist" 2>/dev/null || echo '?')"
[[ "$BUILT" == "$VERSION" ]] || { echo "Built app reports version '$BUILT', expected '$VERSION'." >&2; exit 1; }

if [[ "$DRY_RUN" -eq 1 ]]; then
  echo "==> Dry run: built and packaged, no tag or release created."
  ls -lh "$DIST_DIR"
  exit 0
fi

# --- publish --------------------------------------------------------------
echo "==> Tagging $TAG"
git tag -a "$TAG" -m "Vent $VERSION"
git push origin "$TAG"

echo "==> Creating the GitHub release"
gh release create "$TAG" \
  "$DIST_DIR/$(macos_zip_name "$VERSION")" \
  "$DIST_DIR/$(windows_zip_name "$VERSION")" \
  "$DIST_DIR/latest.json" \
  "$DIST_DIR/SHA256SUMS.txt" \
  --title "Vent $VERSION" \
  --generate-notes

echo "Released: https://github.com/$(vent_repo)/releases/tag/$TAG"
