# Story 5 — Protect the whole endpoint (RequireAuthorization) + fail-safe + public-bind guard

**Feature**: mcp-http-standard-authorization · **Jira**: ENG-93386 · **Size**: M · **Status**: ready-for-dev
**Depends on**: 3, 4 · **Blocks**: 6 · **Status: DONE (2026-07-11)**

> **OQ-A resolved: REFUSE, security-first.** A public/wildcard `--host` with authorization off refuses to start (`EvaluatePublicBindGuard` → `Refuse`, `WriteError` + exit 1), unless `--allow-insecure-public` (+ `CLIO_MCP_HTTP_ALLOW_INSECURE_PUBLIC`) is explicitly set, in which case it starts with a loud `WriteWarning`. A non-wildcard host, or authorization enabled, is never gated. Implemented as `McpHttpServerCommand.EvaluatePublicBindGuard` (pure, unit-tested — 4/4 cases) called before `builder.Build()`.
>
> **Endpoint enforcement:** `app.MapMcp(options.Path)` now chains `.RequireAuthorization(McpHttpAuthentication.PolicyName)` when `authConfiguration.Enabled` — closing the "gates only passthrough" gap: passthrough AND `-e`/stored-credential access both now require a valid token. Middleware ordering (host/origin filtering → authN → authZ → credential-capture) was ALREADY correct from Story 3 — no reordering needed; verified because `UseAuthorization()` short-circuits before the ENG-93208 API-key-gate/credential-capture middleware ever runs.
>
> Verified with a **real in-memory `TestServer` pipeline** mirroring `Run()`'s exact conditional wiring (auth services + `UseAuthentication`/`UseAuthorization` + `RequireAuthorization` all applied only when enabled): auth-disabled path reaches the endpoint with no token (AC-04); all Story-4 401/403 cases still hold with enforcement now on the endpoint itself. 5 new tests (4 guard-matrix + 1 disabled-path pipeline).

## As a / I want / So that
As a platform admin, I want every request to a public clio edge to require a valid token (not just the passthrough leg), so a public URL is never partially open.

## Scope
- `app.MapMcp(path).RequireAuthorization(<scope policy>)` after `UseAuthentication()/UseAuthorization()`, applied when auth is enabled → passthrough AND `-e`/stored both require a token (FR-03).
- Fail-safe (FR-05): no issuer configured ⇒ auth off, pipeline == today (stdio/`-e`/loopback unaffected).
- Public-bind guard: `--host 0.0.0.0` (or any wildcard) with no issuer ⇒ loud startup warning; decide refuse-to-start unless `--allow-insecure-public` (OQ-A) — implement the chosen behavior.
- Middleware ordering: host/origin filtering → authN → authZ → credential-capture (the ENG-93208 capture middleware now runs only for authenticated requests).

## Acceptance Criteria
- Auth enabled: unauthenticated request to any tool ⇒ `401` (AC-01); valid token ⇒ works.
- Auth disabled + loopback: unchanged (AC-04).
- Wildcard bind + no issuer ⇒ warning emitted (or refuses to start per OQ-A) — test-asserted.
- Both host graphs pass `ValidateOnBuild`.

## Definition of Done
- [ ] Endpoint-level authorization wired behind the enabled-flag.
- [ ] Fail-safe + public-bind guard implemented per OQ-A verdict.
- [ ] Tests: enabled/disabled matrix + public-bind guard.

## Notes
This is where the "gates only passthrough" and "opaque fallback" defects are actually closed. Coordinate ordering with the ENG-93208 `EnforcePlatformApiKeyGate`/`CaptureCredentialContext` middleware (Story 7 decides the api-key gate's fate).
