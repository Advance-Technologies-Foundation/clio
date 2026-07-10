# PRD: Clio MCP HTTP Standard Authorization (OAuth 2.1 Resource Server)

**Status**: Draft
**Author**: PM (with Alex Kravchuk)
**Created**: 2026-07-10
**Jira**: ENG-93386 (sub-task of ENG-92790; follow-up to ENG-93208)
**ADR**: [adr-mcp-http-standard-authorization.md](../adr/adr-mcp-http-standard-authorization.md)
**Umbrella branch**: `claude/clio-mcp-multi-tenant-73a807` (PR #830)

---

## Problem Statement

`clio mcp-http` (ENG-93208, PR #830) authorizes its per-request credential-passthrough leg with a bespoke **static platform API key** (`PlatformApiKeyGate`, presented as `Authorization: Bearer <key>`). Live testing (2026-07-10) confirmed this is inadequate for the **public** deployment the AI Platform integration requires:

- **Non-standard** — MCP defines a standard authorization model (OAuth 2.1 Resource Server); a static shared key is not interoperable with MCP clients/tooling.
- **Partial coverage** — the key gates **only** the passthrough leg; pre-registered `-e <env>` / stored-credential access is reachable with **no** key. Not whole-endpoint protection.
- **Opaque** — 401 fires only when the credential header is present; a missing key silently degrades to "ignore header" → downstream `Environment name is required`. No positive "is my key accepted?" probe.
- **Confusing** — two different `Authorization: Bearer` meanings (gateway trust token vs the tenant Creatio access token) at different hops.

We need to replace the front door with the **standard MCP OAuth 2.1 Resource-Server** model, implemented on ASP.NET Core, protecting the **entire** `/mcp` endpoint, while keeping the tenant Creatio credential as a **separate upstream credential plane** (no token passthrough).

## Goals

- [ ] **Goal 1 — Whole-endpoint standard auth.** Every request to `/mcp` (passthrough AND `-e`/stored) requires a valid OAuth 2.1 bearer JWT validated as a Resource Server.
  - **SM-01**: With authorization enabled, an unauthenticated request receives `401` + a spec-compliant `WWW-Authenticate` carrying the `resource_metadata` URI; a request with a valid token (correct issuer/audience/scope) succeeds. Asserted by e2e against the AS.
- [ ] **Goal 2 — Standard discovery.** clio advertises Protected Resource Metadata (RFC 9728) so an MCP client can discover the AS from a `401`.
  - **SM-02**: `GET /.well-known/oauth-protected-resource` returns `{ resource, authorization_servers, scopes_supported }` matching config; the `401` `WWW-Authenticate` points at it.
- [ ] **Goal 3 — Two credential planes, no token passthrough.** The tenant Creatio credential stays a distinct upstream secret; the client's MCP JWT is never forwarded to Creatio.
  - **SM-03**: A test asserts the inbound MCP JWT appears on **no** outbound Creatio request; the credential header is honored only on an authenticated request and stripped if injected by an unauthenticated caller.
- [ ] **Goal 4 — No regression to stdio / dev.** stdio and unconfigured-auth local use behave as today.
  - **SM-04**: `clio mcp` (stdio) unchanged; `clio mcp-http` with no issuer configured behaves exactly as pre-auth (fail-safe-off) — but a public bind (`--host 0.0.0.0`) with no issuer emits a loud warning (or refuses to start, per OQ-A).

## Blocking Prerequisite

**The Authorization Server capability is unconfirmed.** The design assumes AI-Platform `identity-platform` can issue tokens to a **pre-registered confidential client** via `grant_type=client_credentials` with an RFC 8707 `resource` parameter and (preferably) `private_key_jwt` client auth. This MUST be confirmed with the platform team before the issuer/audience/scope contract is frozen (ADR OQ-B, Story 1 spike). If unsupported, fall back options (client secret; a different grant; a documented service token) must be re-planned.

## Non-goals

- **Will NOT** implement interactive human auth (authorization-code + PKCE) — the client is a machine gateway. Deferred until/unless a direct human-client mode is needed.
- **Will NOT** make clio an Authorization Server (no token minting/storage, no OAuth flow hosting).
- **Will NOT** change the stdio contract or the pre-registered-environment behavior.
- **Will NOT** re-open the ENG-93208 passthrough transport shape; this PRD only replaces/augments the front-door **authorization** and hardens the back-door controls.
- **Will NOT** fix the name-based-tool passthrough gap (ENG-93347) or the `IsNetCore` header gap (ENG-93348) — separate follow-ups.

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| AI Platform gateway | to obtain a short-lived `client_credentials` token (aud = clio-mcp) from identity-platform and present it as `Authorization: Bearer` | I authenticate to a public clio edge the standard MCP way |
| MCP client author | to discover clio's authorization server from a `401`/`.well-known` document | I can wire auth without out-of-band configuration |
| platform admin | every request to a public clio edge to require a valid token (not just the passthrough leg) | a public URL is not partially open |
| security reviewer | proof the client's MCP token never reaches Creatio and the credential header can't be injected unauthenticated | I can certify no confused-deputy / token-passthrough |
| developer | stdio and local unconfigured `mcp-http` to keep working with no token | my dev loop is unaffected |

## Feature Requirements

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | Validate an OAuth 2.1 bearer JWT on every `/mcp` request as a Resource Server: signature (JWKS via OIDC discovery), issuer, audience/`resource` (RFC 8707), lifetime. Reject with `401` otherwise. | Must |
| FR-02 | Serve Protected Resource Metadata (RFC 9728) at `/.well-known/oauth-protected-resource` with `resource`, `authorization_servers`, `scopes_supported`; enrich the `401` `WWW-Authenticate` with the `resource_metadata` URI (via the SDK `McpAuthenticationHandler`). | Must |
| FR-03 | Protect the whole endpoint: `app.MapMcp(path).RequireAuthorization(<scope policy>)` after `UseAuthentication/UseAuthorization`. Passthrough AND `-e`/stored paths both require a token. | Must |
| FR-04 | Configurable issuer/audience/required-scopes via kebab-case flags + env vars (e.g. `--auth-issuer`/`CLIO_MCP_HTTP_AUTH_ISSUER`, `--auth-audience`, `--auth-required-scopes`); issuer resolved by OIDC discovery so the provider is swappable. | Must |
| FR-05 | Fail-safe: when no issuer is configured, authorization is OFF and behavior equals today (stdio/`-e`/loopback unaffected). A public bind (`0.0.0.0`) with no issuer MUST warn loudly; consider refusing to start without an explicit `--allow-insecure-public` escape hatch (OQ-A). | Must |
| FR-06 | Support the machine-to-machine grant: accept tokens issued to a pre-registered confidential gateway client via `client_credentials`. clio only validates them (it is the RS); it does not run the grant. | Must |
| FR-07 | Back-door hardening: the credential header is honored **only** on an authenticated request; any inbound same-named header on an unauthenticated request is stripped/ignored at the edge. | Must |
| FR-08 | Authorize gateway→tenant: the authenticated principal (JWT claims) must be permitted to act for the tenant asserted in `X-Integration-Credentials`; tenant identity is never trusted from the header alone. | Should |
| FR-09 | Invariant: the client's MCP JWT is never attached to any outbound Creatio request (test-asserted). | Must |
| FR-10 | Per-tool authorization: evaluate `.AddAuthorizationFilters()` so individual tools/prompts/resources can carry `[Authorize]`/scope (endpoint-level auth alone does not filter primitives). | Should |
| FR-11 | Unified auth error taxonomy at the auth layer, before the tool layer: `401` (missing/invalid/expired/wrong-audience), `403` (insufficient scope / gateway not authorized for tenant), `400` (malformed). No secret echoed. | Must |
| FR-12 | Decide the fate of `--platform-api-key`: retire as default; if kept, only as an explicit non-OAuth dev/offline fallback behind a separate opt-in flag that cannot weaken the OAuth path (OQ-D). | Must |
| FR-13 | Keep secrets out of logs/metadata/responses: JWT, client assertions, and Creatio credentials never logged or echoed; separate redaction paths for the two planes. | Must |
| FR-14 | Docs + capability map updated (`help/en/mcp-http.txt`, `docs/commands/mcp-http.md`, `Commands.md`, `docs/McpCapabilityMap.md`); e2e coverage against identity-platform. | Must |

## Acceptance Criteria (cross-cutting)

- **AC-01** (Goal 1/FR-03): unauthenticated `/mcp` → `401` + `WWW-Authenticate` with `resource_metadata`; valid token → success; wrong-audience token → `401`.
- **AC-02** (Goal 2/FR-02): PRM document served and accurate vs config.
- **AC-03** (Goal 3/FR-07/FR-09): credential header ignored on unauthenticated request; MCP JWT never on an outbound Creatio call.
- **AC-04** (Goal 4/FR-05): unconfigured-auth `mcp-http` == today; stdio unchanged; public-bind-without-issuer warns/refuses per OQ-A.
- **AC-05** (FR-11): auth failures resolve as 401/403/400 at the auth layer, never as a tool-body "environment name required".
- **AC-ERR**: no secret (JWT, assertion, Creatio credential) appears in any log/response/metadata.

## Open Questions

Inherited from the ADR — OQ-A (option names + public-bind refuse-vs-warn), OQ-B (identity-platform CC/RFC8707/private_key_jwt support — spike), OQ-C (ResourceMetadata/scope shape + canonical URI behind K8s ingress), OQ-D (retire vs keep `--platform-api-key`), OQ-E (raw creds vs opaque reference), OQ-F (pin SDK version exposing the auth surface).
