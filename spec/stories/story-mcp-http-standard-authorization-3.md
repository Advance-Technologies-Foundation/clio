# Story 3 — JWT bearer Resource-Server validation (AddJwtBearer)

**Feature**: mcp-http-standard-authorization · **Jira**: ENG-93386 · **Size**: M · **Status**: ready-for-dev
**Depends on**: 1, 2 · **Blocks**: 4, 5

## As a / I want / So that
As a public MCP edge, I want to validate incoming bearer JWTs as an OAuth 2.1 Resource Server, so only tokens issued for me by the trusted AS are accepted.

## Scope
- `AddAuthentication().AddJwtBearer("Bearer", …)`: `Authority = <issuer>` (OIDC discovery → JWKS), `Audience = <auth-audience>`, `TokenValidationParameters` validating issuer, audience/`resource` (RFC 8707), lifetime, signing key.
- Only wired when authorization is enabled (issuer configured); otherwise the pipeline is exactly as today (FR-05).
- Accept `client_credentials`-issued tokens (FR-06) — no special handling beyond standard validation (clio is the RS, not the AS).
- `AddAuthorization` with a scope policy from `--auth-required-scopes`.

## Acceptance Criteria
- Valid token (correct iss/aud/scope, unexpired) authenticates; wrong audience / wrong issuer / expired / bad signature ⇒ `401` (AC-01).
- Insufficient scope ⇒ `403` (AC-05).
- Auth-disabled mode: no JWT validation, behavior == today (AC-04).
- Both HTTP and stdio host graphs still pass `ValidateOnBuild`.
- Unit tests with a test signing key (issue tokens, assert accept/reject matrix).

## Definition of Done
- [ ] JWT validation wired behind the enabled-flag.
- [ ] Accept/reject unit matrix green.
- [ ] No new analyzer warnings; DI graph validates.

## Notes
Pin and verify the `ModelContextProtocol.AspNetCore` / `Microsoft.AspNetCore.Authentication.JwtBearer` versions (OQ-F). Do not couple validation to a specific provider.
