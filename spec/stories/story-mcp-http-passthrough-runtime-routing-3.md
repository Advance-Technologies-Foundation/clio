# Story 3: Dual-runtime HTTP and live MCP E2E proof

**Feature**: mcp-http-passthrough-runtime-routing
**Jira**: [ENG-93348](https://creatio.atlassian.net/browse/ENG-93348)
**FR coverage**: FR-01, FR-04, FR-09, FR-10 · AC-01, AC-02, AC-03, AC-05, AC-07, AC-08, AC-11
**PRD**: [prd-mcp-http-passthrough-runtime-routing.md](../prd/prd-mcp-http-passthrough-runtime-routing.md)
**ADR**: [adr-mcp-http-passthrough-runtime-routing.md](../adr/adr-mcp-http-passthrough-runtime-routing.md)
**Status**: ready-for-dev
**Size**: L (1–2 days, including live-stand validation)
**Depends on**: Story 2

---

## As a

maintainer responsible for the AI-Platform passthrough integration

## I want

process-level and live MCP tests to prove both runtime route families through `POST /mcp`

## So that

ENG-93348 is not closed on mocked settings alone and the representative .NET Core bearer path is verified.

## Acceptance Criteria

- [ ] **AC-01 (PRD AC-01, AC-11)** — a header-only environment-sensitive tool succeeds against a real .NET Core/NET8 tenant through `POST /mcp`, with no registered environment and AI-Platform-representative bearer authentication.
- [ ] **AC-02 (PRD AC-02, AC-11)** — the same process path succeeds against a real .NET Framework tenant using explicit `isNetCore: false`.
- [ ] **AC-03 (PRD AC-01, AC-02)** — captured outbound request evidence, not only the tool result, proves no `/0/` for Core and exactly one `/0/` for Framework.
- [ ] **AC-04 (PRD AC-03, AC-05, AC-07)** — real `clio mcp-http` process tests return HTTP 400 for missing, `null`, string, and numeric runtime values before downstream context/tool/outbound activity, with secret-free body and logs.
- [ ] **AC-05 (PRD AC-06, AC-08, AC-10)** — multi-tenant, concurrency, OAuth/API-key authorization, mixed-input rejection, cache isolation, and no-secret regressions use explicit runtime values and remain green.
- [ ] **AC-06** — every E2E header encoder requires a boolean argument; stand configuration fails clearly when a tenant runtime variable is missing or invalid rather than defaulting.
- [ ] **AC-07** — committed E2E coverage is marked manual/not in CI, and the implementation report records which live stands and auth modes were actually exercised.

## Implementation Notes

Use the `test-mcp-tool` skill.

- Change `clio.mcp.e2e/Support/Mcp/McpHttpServerSession.cs` header helpers so callers must pass `bool isNetCore`.
- Add strict `CLIO_MCP_HTTP_E2E_TENANT1_IS_NET_CORE` and `CLIO_MCP_HTTP_E2E_TENANT2_IS_NET_CORE` handling in `McpHttpPassthroughStand.cs`; reject missing/non-boolean values.
- Update every multi-tenant, concurrency, OAuth, and passthrough fixture/header producer to supply the field.
- Use an existing read-only environment-sensitive tool such as `list-apps` or `describe-environment`. Do not perform a real deploy, uninstall, or other destructive operation.
- Add a recording/proxy fixture or equivalent captured-request mechanism for URL layout assertions.

## Test Requirements

- Unit/process tests for encoders, strict stand parsing, missing/invalid HTTP 400 short-circuit, and captured Core/Framework routes.
- Live E2E for a real Core bearer tenant and a real Framework tenant through `POST /mcp`.
- Existing multi-tenant, concurrent isolation, mixed-input, authorization, and no-regression E2E suites updated with explicit runtime.
- Search all header builders and examples to ensure none can silently omit `isNetCore`.

All unit tests follow `[Description]`, Arrange/Act/Assert, and `because`; E2E stays cross-platform and manual/not in CI.

## Definition of Done

- [ ] Both live runtime families and captured URL layouts are proven.
- [ ] Targeted `McpServer` unit tests pass, followed by the affected `clio.mcp.e2e` tests with results recorded.
- [ ] No new `CLIO*` diagnostics exist in modified files; `git diff --check` passes.
- [ ] No destructive live operation is used.
- [ ] Stories 1 and 2 remain green.

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Unit/process tests passing:
- Manual E2E stands and results:
- Notes:
