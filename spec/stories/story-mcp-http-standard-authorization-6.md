# Story 6 — Back-door hardening: header strip, gateway→tenant authorization, no-token-passthrough invariant

**Feature**: mcp-http-standard-authorization · **Jira**: ENG-93386 · **Size**: M · **Status**: ready-for-dev
**Depends on**: 1, 5 · **Blocks**: 7

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
- [ ] Header-strip + gateway→tenant authZ implemented.
- [ ] No-token-passthrough invariant test green.
- [ ] Redaction verified for both planes.

## Notes
OQ-E (raw creds vs opaque reference) may change how the header carries the credential; if opaque-reference is chosen, this story resolves the reference server-side and the strip/authorize logic still applies. Coordinate with ENG-93208 SSRF/isolation (unchanged).
