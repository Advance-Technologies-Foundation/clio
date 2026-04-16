# clio developer shortcuts
# Usage: make <target> [MODULE=Command] [FILTER=...]
#
# Cross-platform: works on macOS/Linux. On Windows use build.ps1 or build.cmd directly.

.PHONY: build build-debug build-release test test-unit test-integration test-analyzers test-mcp-e2e test-module lint check-pr clean help

# ── Build ────────────────────────────────────────────────────────────────────

## build: fast debug build (analyzers on, no cliogate packaging)
build:
	dotnet build clio/clio.csproj -c Debug

## build-debug: alias for build
build-debug: build

## build-release: full release build including cliogate packaging (PowerShell required)
build-release:
	pwsh ./build.ps1

## clean: remove all bin/obj artifacts
clean:
	find . -type d \( -name bin -o -name obj \) -not -path './.git/*' -exec rm -rf {} + 2>/dev/null; true

# ── Test ─────────────────────────────────────────────────────────────────────

## test: run all unit tests
test: test-unit

## test-unit: run full unit test suite
test-unit:
	dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit" --no-build

## test-integration: run integration tests
test-integration:
	dotnet test clio.tests/clio.tests.csproj --filter "Category=Integration"

## test-analyzers: run Roslyn analyzer tests only
test-analyzers:
	dotnet test Clio.Analyzers.Tests/Clio.Analyzers.Tests.csproj --no-build

## test-mcp-e2e: run MCP end-to-end tests
test-mcp-e2e:
	dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj

## test-module: run unit tests for a single module  (MODULE=Command|McpServer|Common|...)
## Example: make test-module MODULE=Command
test-module:
ifndef MODULE
	$(error MODULE is not set. Usage: make test-module MODULE=Command)
endif
	dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=$(MODULE)" --no-build

## test-filter: run tests with a custom --filter expression  (FILTER="...")
## Example: make test-filter FILTER="FullyQualifiedName~MyTest"
test-filter:
ifndef FILTER
	$(error FILTER is not set. Usage: make test-filter FILTER="Category=Unit&Module=Command")
endif
	dotnet test clio.tests/clio.tests.csproj --filter "$(FILTER)" --no-build

# ── Lint / Analyze ───────────────────────────────────────────────────────────

## lint: build in Debug to surface all CLIO analyzer warnings
lint:
	dotnet build clio/clio.csproj -c Debug --no-incremental /p:TreatWarningsAsErrors=false

## verify-docs: check that agent-doc wrappers are in sync with the canonical doc
verify-docs:
	pwsh ./scripts/verify-agent-docs.ps1

# ── PR workflow ───────────────────────────────────────────────────────────────

## check-pr: show current PR status (requires gh CLI)
check-pr:
	bash ./check-pr.sh

## check-pr-release: release-readiness scoring for current PR
check-pr-release:
	bash ./check-pr-release-final.sh

# ── Help ──────────────────────────────────────────────────────────────────────

## help: list all targets with descriptions
help:
	@grep -E '^## ' Makefile | sed 's/^## //' | column -t -s ':'
