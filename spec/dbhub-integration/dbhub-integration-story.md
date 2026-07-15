# Story: Deliver dbHub Installation and Automatic Creatio Source Synchronization

**Feature**: dbhub-integration
**Issue**: #882
**FR coverage**: FR-01 through FR-18
**PRD**: [dbhub-integration-prd.md](dbhub-integration-prd.md)
**ADR**: [dbhub-integration-adr.md](dbhub-integration-adr.md)
**Status**: review
**Size**: L

---

## As a

Windows Creatio developer using clio and dbHub

## I want

clio to install/adopt dbHub and reconcile sources during manual and local environment lifecycle operations

## So that

database access remains safe, current, and visible across CLI, MCP, and ClioRing without manual secret copying.

## Acceptance Criteria

- [x] Settings, install/adopt/repair, task, health, and pinned-package acceptance criteria AC-01 through AC-05 are implemented.
- [x] Source discovery, ownership, preservation, collision, locking, atomicity, and manual sync acceptance criteria AC-06 through AC-09 are implemented.
- [x] Deploy/uninstall ordering, best-effort warning, hot reload/offline, and secret-safety acceptance criteria AC-10 through AC-14 are implemented.
- [x] Documentation, MCP E2E, Ring contract/tests, and NativeAOT acceptance criterion AC-15 is implemented.

## Implementation Notes

Follow [dbhub-integration-adr.md](dbhub-integration-adr.md). Keep commands thin, behavior behind interfaces, and source ownership in atomically committed managed TOML markers. Rebase onto #881's generic warning vocabulary before editing shared progress/Ring vocabulary. Do not add installer or sync MCP tools; update existing deploy/uninstall tools and guidance only where automatic behavior changes.

## Test Requirements

All `TC-U-*` and `TC-I-*` cases in [dbhub-integration-test-plan.md](dbhub-integration-test-plan.md) are mandatory. Relevant MCP E2E tests run locally for net8.0 and net10.0. Ring tests and Windows x64 NativeAOT publish are mandatory. Use the user-supplied Creatio archive only for the disposable lifecycle scenarios described in the test plan, with verified cleanup.

## Definition of Done

- [x] Code builds with no new CLIO diagnostics.
- [x] Public API has XML documentation; CLI flags are kebab-case.
- [x] Unit/integration/E2E/runtime/Ring gates in the test plan pass.
- [x] Secrets are absent from every observable surface.
- [x] Command docs/help/index/wiki anchors and settings schema are aligned.
- [x] MCP reviewed; no dedicated installer/sync tool added; existing lifecycle E2E updated.
- [x] ClioRing compatibility reviewed and required exact commands pass.
- [x] Comprehensive agentic review has no Blocker/High findings.
- [ ] Ready PR references `Fixes #882`, is assigned, and auto-merge is armed.

## Dev Agent Record

- Implementation started: 2026-07-15
- Implementation completed: 2026-07-15
- Tests passing: full Unit net10.0/net8.0 (6059 passed, 25 skipped each); MCP lifecycle E2E (5 each); developer-local explicit archive lifecycle E2E (1 each); ClioRing (101); Windows x64 NativeAOT publish.
- Notes: Rebased onto merged issue #881 safety semantics; comprehensive quality, correctness, and security review is clear. The archive-backed lifecycle fixture is explicitly local-only and forbidden in TeamCity.
