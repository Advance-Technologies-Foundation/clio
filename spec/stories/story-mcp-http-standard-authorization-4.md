# Story 4 — Protected Resource Metadata + WWW-Authenticate (SDK handler)

**Feature**: mcp-http-standard-authorization · **Jira**: ENG-93386 · **Size**: S · **Status**: ready-for-dev
**Depends on**: 2, 3 · **Blocks**: 5

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
