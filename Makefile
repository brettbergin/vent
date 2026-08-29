# Vent — development workflow. Every target is headless unless noted; logs land in Logs/.
# Override the editor with UNITY=/path/to/Unity (tools/common.sh resolves it from ProjectVersion.txt).

SHELL      := /usr/bin/env bash
PROJECT    := $(CURDIR)
VERSION    := $(shell sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)
UNITY      ?= /Applications/Unity/Hub/Editor/$(VERSION)/Unity.app/Contents/MacOS/Unity
APP        := Builds/Vent.app
PLAYER_LOG := $(HOME)/Library/Logs/Vent Studio/Vent/Player.log

.DEFAULT_GOAL := help
.PHONY: help regen test test-edit test-play test-gui test-update build build-windows package release run open logs player-log gpubench clean check

help: ## Show this help
	@grep -hE '^[a-zA-Z_-]+:.*?## ' $(MAKEFILE_LIST) | sort | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-12s\033[0m %s\n", $$1, $$2}'

regen: ## Regenerate every asset, prefab and scene from code (Vent ▸ Rebuild Everything)
	@UNITY="$(UNITY)" ./tools/regen.sh

test: ## Run EditMode then PlayMode tests headless (GPU/input tests are skipped here; see test-gui)
	@UNITY="$(UNITY)" ./tools/test.sh

test-edit: ## EditMode tests only (pure logic; fast)
	@mkdir -p TestResults Logs
	@"$(UNITY)" -batchmode -projectPath "$(PROJECT)" -runTests -testPlatform EditMode \
	  -testResults "$(PROJECT)/TestResults/EditMode.xml" -logFile "$(PROJECT)/Logs/test-EditMode.log"; \
	  python3 tools/summarize-tests.py TestResults/EditMode.xml

test-play: ## PlayMode tests only, headless
	@mkdir -p TestResults Logs
	@"$(UNITY)" -batchmode -projectPath "$(PROJECT)" -runTests -testPlatform PlayMode \
	  -testResults "$(PROJECT)/TestResults/PlayMode.xml" -logFile "$(PROJECT)/Logs/test-PlayMode.log"; \
	  python3 tools/summarize-tests.py TestResults/PlayMode.xml

test-gui: ## PlayMode tests in a windowed editor: also runs the input and rendering tests (opens Unity briefly)
	@mkdir -p TestResults Logs
	@"$(UNITY)" -projectPath "$(PROJECT)" -runTests -testPlatform PlayMode \
	  -testResults "$(PROJECT)/TestResults/PlayMode-gui.xml" -logFile "$(PROJECT)/Logs/test-PlayMode-gui.log" $(if $(FILTER),-testFilter $(FILTER),); \
	  python3 tools/summarize-tests.py TestResults/PlayMode-gui.xml

test-update: ## Exercise the macOS self-update against a throwaway install (make build first)
	@UNITY="$(UNITY)" ./tools/test-update-swap.sh

build: ## Build the macOS player into Builds/Vent.app
	@UNITY="$(UNITY)" ./tools/build.sh

build-windows: ## Build the Windows x64 player into Builds/Windows/ (needs the Windows Build Support module)
	@UNITY="$(UNITY)" ./tools/build-windows.sh

package: ## Zip the built players into dist/ with checksums and the update manifest
	@./tools/package.sh all
	@./tools/manifest.sh

release: ## Cut a release end to end: test, build both players, package, tag, publish (VERSION=0.2.0)
	@test -n "$(VERSION)" || { echo "Usage: make release VERSION=0.2.0 [ARGS=--dry-run]" >&2; exit 1; }
	@UNITY="$(UNITY)" ./tools/release.sh "$(VERSION)" $(ARGS)

run: ## Launch the built player (make build first)
	@test -d "$(APP)" || { echo "No build at $(APP); run 'make build'." >&2; exit 1; }
	@open "$(APP)"

open: ## Open the project in the Unity editor
	@open -a "$(UNITY)" --args -projectPath "$(PROJECT)" 2>/dev/null || "$(UNITY)" -projectPath "$(PROJECT)" &

logs: ## Tail the most recent regen/test/build log
	@ls -t Logs/*.log 2>/dev/null | head -1 | xargs -I{} sh -c 'echo "== {}"; tail -n 40 "{}"'

gpubench: ## Time a fixed GPU workload on every Metal device: tells a throttled machine from a slow build
	@swiftc -O -o "$(CURDIR)/Logs/gpubench" tools/gpubench.swift && "$(CURDIR)/Logs/gpubench"

player-log: ## Show errors/exceptions from the last player session
	@test -f "$(PLAYER_LOG)" || { echo "No player log at $(PLAYER_LOG)" >&2; exit 1; }
	@grep -nE "Exception|Error|error" "$(PLAYER_LOG)" | grep -v "Licensing" || echo "No errors in $(PLAYER_LOG)"

check: regen test ## Regenerate, then run the headless suites (what CI should do)

clean: ## Remove build output, test results and logs (never touches Library/)
	@rm -rf Builds TestResults Logs dist
	@echo "cleaned Builds/ TestResults/ Logs/ dist/"
