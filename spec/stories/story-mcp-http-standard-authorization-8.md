# Story 8 — Per-tool authorization filters (optional), docs, and e2e

**Feature**: mcp-http-standard-authorization · **Jira**: ENG-93386 · **Size**: M · **Status**: ready-for-dev
**Depends on**: 7 · **Blocks**: —

## As a / I want / So that
As an operator, I want per-tool scope enforcement and accurate docs/e2e, so the standard-auth edge is finishable and verifiable.

## Scope
- **Per-tool authorization (FR-10):** evaluate `.AddAuthorizationFilters()` and apply `[Authorize]`/scope where a tool should require more than the endpoint-level policy (e.g. destructive tools). Endpoint-level `RequireAuthorization` alone does not filter individual primitives.
- **Docs (FR-14):** update `clio/help/en/mcp-http.txt`, `clio/docs/commands/mcp-http.md`, `clio/Commands.md`, `docs/McpCapabilityMap.md` — the new auth options, the two-plane model, PRM/`.well-known` discovery, the fail-safe + public-bind behavior, and the `--platform-api-key` disposition. Remove/replace the old "There is no built-in authentication" wording.
- **E2E:** against a dev identity-platform — obtain a `client_credentials` token, call a passthrough tool successfully; assert `401`/`403`/discovery behaviors; assert MCP-token-never-forwarded. Env-var skip-gated like the ENG-93208 fixtures (manual/not-in-CI until an AS is available in CI).

## Acceptance Criteria
- Per-tool filters applied where warranted + tested (or an explicit note that endpoint-level suffices for v1).
- Docs accurate vs code (options, headers, discovery, defaults); "docs reviewed" stated in the change summary.
- E2E: token-happy-path + 401 + 403 + discovery + no-token-passthrough fixtures authored.

## Definition of Done
- [ ] Filters decision implemented/tested.
- [ ] All doc targets updated + accurate.
- [ ] E2E fixtures authored (skip-gated) and pass against a live AS.
- [ ] MCP surface reviewed (`create-mcp-tool`/`test-mcp-tool` skills as applicable).

## Notes
Final gate before ready-to-merge: comprehensive adversarial review over the whole ENG-93386 diff (per AGENTS.md code-review gate).
