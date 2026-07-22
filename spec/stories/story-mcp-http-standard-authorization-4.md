# Story 4 — Protected Resource Metadata + WWW-Authenticate (SDK handler)

**Feature**: mcp-http-standard-authorization · **Jira**: ENG-93386 · **Size**: S · **Status**: ready-for-dev
**Depends on**: 2, 3 · **Blocks**: 5 · **Status: DONE (2026-07-11)**

> **OQ-F resolved (verified by decompiling the pinned SDK, not guessing):** `ModelContextProtocol.AspNetCore` 1.4.0's `AddMcp()` registers scheme `McpAuth` (`McpAuthenticationDefaults.AuthenticationScheme`) with `ForwardAuthenticate = "Bearer"` set in its ctor by default — matches `JwtBearerDefaults.AuthenticationScheme`, so no extra forwarding config is needed. Composition: `AddAuthentication(o => { o.DefaultScheme = "Bearer"; o.DefaultChallengeScheme = "McpAuth"; }).AddJwtBearer(...).AddMcp(o => o.ResourceMetadata = ...)`. `McpAuthenticationHandler` implements `IAuthenticationRequestHandler`, so ASP.NET Core's authentication middleware serves `/.well-known/oauth-protected-resource` **unconditionally and anonymously** (no endpoint mapping needed) and its `HandleChallengeAsync` appends `WWW-Authenticate: Bearer resource_metadata="..."` on a 401. `ProtectedResourceMetadata` (namespace `ModelContextProtocol.Authentication`) ships in the transitively-referenced `ModelContextProtocol.Core` package — no new PackageReference needed.
>
> **OQ-C resolved by design, not by a hardcoded value:** `Resource` is left `null` by default so the handler derives it per-request from the incoming scheme/host/path — correct behind any ingress that forwards the `Host` header, without clio needing to know its own external URL. An explicit `--auth-resource` override (+ `CLIO_MCP_HTTP_AUTH_RESOURCE`) is available for a deployment where auto-derivation is wrong.
>
> Implemented in `McpHttpAuthentication.BuildResourceMetadata` + updated `ConfigureServices`. Verified with a **real in-memory `TestServer` pipeline** (`McpHttpAuthenticationPipelineTests`, not just config-object assertions): unauthenticated → 401 + `WWW-Authenticate` naming `resource_metadata`; `/.well-known/oauth-protected-resource` served anonymously with the configured issuer(s)/scopes; valid token → 200; wrong audience / expired → 401; missing scope → 403. 11 new unit tests (5 `BuildResourceMetadata`/config + 6 pipeline).

## As a / I want / So that
As an MCP client, I want to discover clio's authorization server from a `401`, so I can obtain a token without out-of-band configuration (RFC 9728).

## Scope
- Register the SDK MCP auth scheme (`.AddMcp("Mcp", …)` with `ForwardAuthenticate = "Bearer"`) and a `ResourceMetadata { Resource = <canonical URI>, AuthorizationServers = [<issuer>], ScopesSupported = [<scopes>] }` built from Story 2 config.
- `McpAuthenticationHandler` serves `/.well-known/oauth-protected-resource` and augments the `401` with the `resource_metadata` URI — do not hand-roll.
- Canonical URI derivation for `Resource` must handle the K8s service / ingress form (OQ-C).

## Acceptance Criteria
- `GET /.well-known/oauth-protected-resource` returns a document matching config (AC-02).
- An unauthenticated `/mcp` request's `401` carries `WWW-Authenticate: Bearer resource_metadata="…"`.
- Metadata endpoint is reachable **without** a token (discovery must be anonymous).
- Unit/integration tests for the metadata document + challenge header.

## Definition of Done
- [ ] PRM endpoint + enriched challenge via SDK handler.
- [ ] Tests assert document shape + `WWW-Authenticate`.
- [ ] Canonical URI resolution documented + tested for the ingress form.

## Notes
Verify the exact `AddMcp`/`ResourceMetadata` API against the pinned SDK version (OQ-F); the auth surface has changed across previews.
