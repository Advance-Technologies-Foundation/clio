# Story 8 — Per-tool authorization filters (optional), docs, and e2e

**Feature**: mcp-http-standard-authorization · **Jira**: ENG-93386 · **Size**: M · **Status**: ready-for-dev
**Depends on**: 7 · **Blocks**: — · **Status: DONE (2026-07-11) — FR-10 scoped down, see below**

> **FR-10 (per-tool authorization filters) explicitly SCOPED DOWN, not implemented.** Same reasoning class as Story 6's FR-08: the identity-platform issues one undifferentiated scope/audience per gateway client, so a per-tool `.AddAuthorizationFilters()` policy could only require either the same baseline scope already enforced endpoint-wide (a no-op) or an elevated scope the Authorization Server never issues (reject-all for those tools) — no useful middle exists. Building the filter infrastructure for a differentiation the AS can't feed would be speculative. Endpoint-level `RequireAuthorization` (Stories 3/5) is the whole of the enforceable authorization surface until the AS can issue differentiated scopes. Documented as ADR "Out of scope" bullet; no separate Jira ask filed (no driver requesting it yet, unlike FR-08 which had an explicit story asking for the claim contract).
>
> **Docs (FR-14): all four targets updated.** `clio/help/en/mcp-http.txt`, `clio/docs/commands/mcp-http.md` (both already substantially current from Stories 2–7; added the two new Story-8 guard flags), `clio/Commands.md` (mcp-http one-line summary now mentions OAuth + the platform-api-key fallback disposition), `docs/McpCapabilityMap.md` (section 4 rewritten to cover both the passthrough edge and standard OAuth). No stale "no built-in authentication" wording was found (already replaced in earlier stories).
>
> **MCP surface: reviewed, no update required.** `mcp-http` is the MCP *server* itself; no `[McpServerTool]`/`[McpServerPrompt]`/`[McpServerResource]` wraps it as a callable primitive (grep-confirmed), so there is no MCP tool contract to update.
>
> **E2E: authored + skip-gated, NOT run against a live stand.** `clio.mcp.e2e/McpHttpOAuthAuthorizationE2ETests.cs` (5 tests: anonymous discovery, unauthenticated-401, invalid-token-401, valid-token-happy-path/list-tools, OAuth+passthrough interop) + `Support/Mcp/McpHttpOAuthStand.cs` (env-var skip-gate, mirrors `McpHttpPassthroughStand`) + `Support/Mcp/OAuthClientCredentialsTokenFetcher.cs` (minimal `client_credentials` token fetch). Verified in-session that the skip-gate fires cleanly (all 5 skip with no live-stand env vars set, both TFMs) — but the happy path has genuinely **not** been exercised against a real Authorization Server; the fixture's own XML doc says so explicitly, and the two negative tests intentionally assert only a broad `ThrowAsync<Exception>()` pending that first live run (tightening to a specific 401 assertion is noted as a follow-up in the test comments).
>
> **Final adversarial review (the mandatory gate before ready-to-merge): run and closed.** Three parallel reviewers (quality, correctness/performance, security) reviewed the full branch diff vs `claude/clio-mcp-multi-tenant-73a807`. All three independently converged on the same two real gaps:
> - **HIGH — audience/scope fail-open:** `--auth-authority` alone enables authorization; with neither `--auth-audience` nor `--auth-required-scopes` also configured, `ValidateAudience` becomes `false` and the scope check is vacuously satisfied — the endpoint would accept ANY token the configured (often-shared) issuer ever mints for ANY client/resource. **Fixed**: new `EvaluateAudienceScopeGuard` (REFUSE-by-default, same Ok/Warn/Refuse shape as the Story 5 public-bind guard), overridable only via `--auth-allow-any-audience`.
> - **MEDIUM — public-bind guard too narrow:** only recognized the four literal wildcard spellings; a bind to a concrete LAN/public IP or DNS hostname silently bypassed it. **Fixed**: broadened to `IsPublicBind` (any non-loopback host).
> - Lower-severity findings fixed: a duplicated/inconsistent truthy-env-var parser (`AuthConfiguration.IsTruthy` now shared with `McpHttpServerCommand.IsTruthyEnvironmentFlag`), stale `McpHttpServerSession` doc wording, unverified-E2E-assertion transparency comments, ADR status flipped `Proposed` → `Accepted`.
> - Findings NOT acted on (deliberately, with reasoning recorded): PRM advertising the full issuer set including an internal-DNS authority (Low, requires a new config concept to fix properly — deferred); no full-`Parser` CLI coverage for the new `--auth-*` flags (Low, kebab-case already analyzer-enforced at build); comment density/ticket-tag verbosity (subjective, the security-invariant rationale itself was called "genuinely valuable and proportionate").
> - 5 new tests (`AudienceScopeGuardTests.cs`) + 11 new/updated tests (`PublicBindGuardTests.cs`, renamed parameter + new `IsPublicBind` coverage).
>
> Full McpServer unit suite green throughout: 2087 passed / 0 failed / 1 skipped, both net8.0/net10.0.

## As a / I want / So that

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
- [~] Filters decision — **scoped down, not implemented** (FR-10; no AS-issued differentiated scope exists yet — see ADR "Out of scope" and this file's DONE summary). Endpoint-level suffices for v1, per the AC's own escape hatch.
- [x] All doc targets updated + accurate (`mcp-http.txt`, `mcp-http.md`, `Commands.md`, `McpCapabilityMap.md`). Docs reviewed, no stale wording found.
- [x] E2E fixtures authored (skip-gated) — confirmed the skip fires cleanly with no live-stand env vars. **NOT yet run against a live AS** (no live stand available in this session); stated explicitly, not implied as passing.
- [x] MCP surface reviewed — `mcp-http` is the server itself, not wrapped by any MCP tool/prompt/resource; no update required.
- [x] Final adversarial review (quality/correctness-performance/security) run over the full branch diff; both convergent Blocker/High-equivalent findings (audience fail-open, narrow public-bind guard) fixed and tested; full McpServer unit suite green (2087/0/1 skipped).

## Notes
Final gate before ready-to-merge: comprehensive adversarial review over the whole ENG-93386 diff (per AGENTS.md code-review gate).
