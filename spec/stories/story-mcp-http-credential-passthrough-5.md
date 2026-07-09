# Story 5: Edge API-key gate — fail-closed, strictly additive

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-09, FR-10, OQ-05
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 5; FR-09/10, OQ-05)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 1 (spike), Story 2 (spike), Story 4 (header/context middleware)

---

## As a

platform admin

## I want

the edge to honor `X-Integration-Credentials` **only** when an operator-configured platform API key is present and the request presents a matching `Authorization: Bearer <key>`

## So that

a public/shared URL cannot be abused by an unauthenticated caller, and the default (no key) behaves exactly as 8.1.0.72

---

## Acceptance Criteria

- [x] **AC-01** — Given a configured platform API key and a request presenting a matching `Authorization: Bearer <key>` plus `X-Integration-Credentials`, when middleware runs, then `PassthroughModeEnabled=true` and the header credentials are honored (maps FR-09; AC-01).
- [x] **AC-02** — Given no platform API key configured (default), when a request carries `X-Integration-Credentials`, then the header is **not** honored (fail-closed) and the request falls back to pre-registered/loopback behavior — exactly 8.1.0.72 (maps FR-10; AC-08/AC-10).
- [x] **AC-03** — Given a configured API key, when a request omits or mismatches `Authorization: Bearer`, then header credentials are rejected and the request authenticates as **no** tenant (maps FR-09; AC-09).
- [x] **AC-04** — Given key comparison, when performed, then it uses `CryptographicOperations.FixedTimeEquals` (constant-time), and a comma-separated key set is accepted (any member matches) to support rotation (maps OQ-05).
- [x] **AC-05** — Given the key is configured via CLI flag `--platform-api-key` **or** env var `CLIO_MCP_HTTP_PLATFORM_API_KEY`, when either is set, then passthrough mode is considered enabled (maps OQ-05).
- [x] **AC-ERR** — Given a rejected request (missing/mismatched key with a header present), when rejected, then the response is a clean structured error and no secret material (key or credentials) is echoed (maps FR-11).

## Implementation Notes

From ADR step 5 (FR-09/10, OQ-05):

- Middleware honors `X-Integration-Credentials` only when ≥1 platform API key is configured **and** the request's `Authorization: Bearer <key>` matches one (constant-time `CryptographicOperations.FixedTimeEquals`).
- Config surface: `--platform-api-key` (CLI, comma-set) **and** `CLIO_MCP_HTTP_PLATFORM_API_KEY` (env). "Passthrough mode enabled" ≡ ≥1 key configured — set `CredentialContext.PassthroughModeEnabled` accordingly (feeds Story 10 FR-19 and Story 11 gate).
- No key + no header ⇒ exactly 8.1.0.72: loopback bind, host/origin filtering, pre-registered `-e <env>`, no API key required. Strictly additive.
- Wire into the Story 4 middleware before the credential context is treated as trusted.

Key files: `clio/Command/McpServer/McpHttpServerCommand.cs` (gate in middleware), `McpHttpServerCommandOptions` (flag added here or consolidated in Story 12 — coordinate), config reader for the env var.
Pattern to follow: existing host/origin filtering in `McpHttpServerCommand.Run`; `CryptographicOperations.FixedTimeEquals` usage.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | gate honored with matching key; rejected on missing/mismatched key; fail-closed with no key; comma-set rotation; env-var and flag both enable; constant-time path exercised | `clio.tests/Command/McpServer/PlatformApiKeyGateTests.cs` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; NSubstitute.
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`

## Definition of Done

- [x] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [x] `--platform-api-key` is kebab-case (CLIO001); env-var name documented (`CLIO_MCP_HTTP_PLATFORM_API_KEY`)
- [x] Gate service registered — instance-registered in the `mcp-http` host (`McpHttpServerCommand.Run`) with the interface added to the `BindingsModule` auto-registration skip-list; no MediatR; no raw `HttpClient`
- [x] No secret (key or credentials) in errors/logs (FR-11)
- [x] MCP surface + docs reviewed (FR-15) — `mcp-http` is the MCP host itself, not a wrapped MCP tool, so no tool/prompt/resource to update; full CLI/GitHub docs for the gate deferred to Story 14. Stated outcome: MCP reviewed, no update required.
- [x] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`
- [x] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: yes — `Category=Unit&Module=McpServer` = 1780 passed / 0 failed / 1 skipped; full `Category=Unit` (BindingsModule skip-list touched) = 5049 passed / 0 failed / 35 skipped (net10.0)
- Notes:
  - New: `clio/Command/McpServer/PlatformApiKeyGate.cs` — `IPlatformApiKeyGate` + `PlatformApiKeyGate` (constant-time `CryptographicOperations.FixedTimeEquals` over all keys, no early-out) + `PlatformApiKeyConfiguration.Resolve` (union of `--platform-api-key` flag + `CLIO_MCP_HTTP_PLATFORM_API_KEY` env, comma-split, trimmed, empties dropped).
  - `--platform-api-key` option added to `McpHttpServerCommandOptions`.
  - Gate middleware wired in `McpHttpServerCommand.Run` AFTER `ValidateOrigin` and BEFORE the Story-4 `CaptureCredentialContext`; sets `Items["clio.mcp.passthrough-enabled"]` (true only when a key is configured AND the credential header is present AND `Authorization: Bearer <key>` authorizes; false/absent otherwise; 401 on header-present-but-unauthorized).
  - `CaptureCredentialContext` adjusted: only parses/acts when `passthrough-enabled == true`; otherwise ignores the credential header entirely (no 400) so an untrusted/no-key request behaves exactly as 8.1.0.72 (AC-02).
  - `IPlatformApiKeyGate` added to the `BindingsModule` auto-registration skip-list.
  - DEFERRED (not in Story 5 scope): ADR line 133 requires passthrough to be *doubly*-gated — the incubation feature flag `mcp-http-credential-passthrough` (via `IFeatureToggleService`/`ISettingsRepository.IsFeatureEnabled`) AND this API-key gate. Story 5 implements only the API-key gate. Which story owns the feature-toggle wire-up is an open architect decision.
