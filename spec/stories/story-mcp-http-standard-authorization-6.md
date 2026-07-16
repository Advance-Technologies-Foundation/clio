# Story 6 — Back-door hardening: header strip, gateway→tenant authorization, no-token-passthrough invariant

**Feature**: mcp-http-standard-authorization · **Jira**: ENG-93386 · **Size**: M · **Status**: ready-for-dev
**Depends on**: 1, 5 · **Blocks**: 7 · **Status: DONE (2026-07-11) — FR-08 scoped down, see below**

> **FR-07 (header strip) + FR-09 (no-token-passthrough invariant) + FR-13 (redaction) implemented and tested.** `CaptureCredentialContext` gained a `requireAuthenticatedPrincipal` parameter (threaded from `authConfiguration.Enabled`): when standard OAuth is configured, an unauthenticated request's `X-Integration-Credentials` header is now explicitly ignored — not merely deferred by middleware order — even on a path other than `/mcp` where `RequireAuthorization` never ran. Additive: with OAuth not configured, passthrough keeps working via the platform-API-key gate alone (unchanged). FR-09 is proven structurally: `ToolCommandResolver.BuildEphemeralSettings` is built solely from the parsed `CredentialContext` (itself derived only from `X-Integration-Credentials`) and never reads `HttpContext.Request.Headers.Authorization` — confirmed by grepping the whole outbound-client construction path (`ApplicationClientFactory` + the child-container site) and finding no code that forwards the inbound MCP/platform bearer token anywhere. FR-13 is confirmed via a combined-secrets redaction test: a message carrying both a Creatio-plane token and an MCP-plane JWT has both scrubbed by the existing `SensitiveErrorTextRedactor` (no separate pipeline needed — the generic Bearer/JWT/credential-pair patterns already cover both planes). 4 new tests in `CredentialPassthroughAuthHardeningTests.cs` + 1 in `SensitiveErrorTextRedactorTests.cs`.
>
> **FR-08 (gateway→tenant authorization) explicitly SCOPED DOWN, not implemented — see ADR OQ-G.** The Story-1 spike proved the identity-platform's `client_credentials` token authenticates the gateway as a whole and mints no per-tenant/org claim for it — there is no real claim contract to enforce. Inventing one now would be a fictional security control that authorizes nothing while looking like a check. Today, any request that clears the standard bearer-JWT check is trusted to assert any tenant via the header, identical to the platform-API-key gate's existing trust boundary — not a regression, but not the finer per-tenant control FR-08 originally envisioned. Filed as a platform-team follow-up in the ENG-93386 Jira comment thread.

## As a / I want / So that
As a security reviewer, I want the tenant Creatio credential plane kept strictly separate from the MCP token plane, so clio cannot become a confused deputy and no token is passed through.

## Scope
- **Header strip (FR-07):** `X-Integration-Credentials` is honored **only** on an authenticated request; on an unauthenticated request the header is ignored/stripped at the edge so it cannot be injected.
- **Gateway→tenant authorization (FR-08):** the authenticated principal's JWT claims must permit acting for the tenant asserted in the header; otherwise `403`. Define the claim/scope contract (from Story 1 findings) — e.g. an `allowed_tenants` / scope pattern; never trust tenant identity from the header alone.
- **Invariant (FR-09):** assert the inbound MCP JWT is never attached to any outbound Creatio request (the Creatio credential is the only upstream auth).
- Keep the two planes in separate log-redaction paths (FR-13).

## Acceptance Criteria
- Unauthenticated request carrying the header ⇒ header ignored (not honored) (AC-03).
- Authenticated principal not permitted for the asserted tenant ⇒ `403` (AC-05).
- Test proves the MCP JWT appears on no outbound Creatio call (AC-03/FR-09).
- No secret from either plane appears in logs/responses (AC-ERR).

## Definition of Done
- [x] Header-strip implemented (FR-07).
- [~] Gateway→tenant authZ — **scoped down, not implemented** (FR-08; no platform claim contract exists yet — see ADR OQ-G and the Jira follow-up comment).
- [x] No-token-passthrough invariant test green (FR-09).
- [x] Redaction verified for both planes (FR-13).

## Notes
OQ-E (raw creds vs opaque reference) may change how the header carries the credential; if opaque-reference is chosen, this story resolves the reference server-side and the strip/authorize logic still applies. Coordinate with ENG-93208 SSRF/isolation (unchanged).
