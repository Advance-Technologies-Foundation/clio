# Story 2 — Auth configuration options (issuer/audience/scopes) + OIDC discovery

**Feature**: mcp-http-standard-authorization · **Jira**: ENG-93386 · **Size**: S · **Status**: ready-for-dev
**Depends on**: 1 · **Blocks**: 3, 4, 5 · **Status: DONE (2026-07-11)**

> Implemented as `AuthConfiguration` + `AuthEnvironment` with 5 `--auth-*` options (`--auth-authority`, `--auth-audience`, `--auth-required-scopes`, `--auth-issuer`, `--auth-allow-insecure-metadata`) and `CLIO_MCP_HTTP_AUTH_*` env counterparts. Enabled iff `--auth-authority` is set. Models discovery-authority + valid-issuers + audiences + required-scopes + `RequireHttpsMetadata` (per Story-1 spike: public-iss vs internal-DNS split, HTTP metadata opt-out). 12 unit tests.

## As a / I want / So that
As an operator, I want to configure the Authorization Server the edge trusts, so clio-as-RS validates tokens from the right issuer/audience without hard-coding a provider.

## Scope
- Add options to `McpHttpServerCommandOptions` (kebab-case, CLIO001): `--auth-issuer`, `--auth-audience`, `--auth-required-scopes` (comma-set), plus env counterparts `CLIO_MCP_HTTP_AUTH_ISSUER` / `_AUTH_AUDIENCE` / `_AUTH_REQUIRED_SCOPES`, unioned like the existing key config.
- A resolver (`AuthConfiguration`) that produces a strongly-typed config; issuer drives OIDC discovery (`/.well-known/openid-configuration`) for authority/JWKS.
- "Authorization enabled" = issuer present (mirrors `PlatformApiKeyGate.PassthroughEnabled` shape).
- Help/docs stubs for the new options (full docs in Story 8).

## Acceptance Criteria
- Options parse; env + flag unioned; missing issuer ⇒ auth disabled (FR-05).
- Unit tests: option parsing, env union, kebab-case, "enabled iff issuer set".
- No CLIO001/CLIO005 warnings introduced.

## Definition of Done
- [ ] Options + resolver + DI registration.
- [ ] Unit tests green (`Module=McpServer`).
- [ ] MCP review: options class touched → verify tool/prompt/resource surface unaffected.

## Notes
Pure config; no pipeline wiring yet (Story 3/5). Keep provider-agnostic — no Entra/Auth0-specific assumptions.
