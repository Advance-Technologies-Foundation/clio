# ADR: Clio MCP HTTP Standard Authorization — OAuth 2.1 Resource Server

**Status**: Proposed
**Author**: Architect (with Alex Kravchuk)
**Jira**: ENG-93386 (sub-task of ENG-92790; follow-up to ENG-93208; layers on top of the credential-passthrough edge in [adr-mcp-http-credential-passthrough.md](./adr-mcp-http-credential-passthrough.md))
**Umbrella branch**: `claude/clio-mcp-multi-tenant-73a807` (PR #830) — this work branches off it, not `master`
**Created**: 2026-07-10
**Spec target**: MCP Authorization revision **2025-11-25** (current) + OAuth Client Credentials extension

---

## Context

`clio mcp-http` (PR #830, ENG-93208) added a per-request credential-passthrough edge so one process can serve many Creatio tenants. Its **front-door authorization** is a hand-rolled gate (`PlatformApiKeyGate`): a static shared secret presented as `Authorization: Bearer <platform-api-key>`, compared in constant time.

Live testing (2026-07-10) surfaced that this gate is **opaque and insufficient for a public deployment**:

1. **Non-standard.** MCP defines a standard authorization model (OAuth 2.1 Resource Server). A bespoke static key is not interoperable with MCP clients and tooling.
2. **Partial coverage.** The key gates **only** the passthrough leg. Pre-registered `-e <env>` / stored-credential access is reachable with **no** key at all. On a public bind this is not whole-endpoint protection.
3. **Opaque failure modes.** The gate only 401s when the credential header is present; a missing key silently degrades to "ignore the header", which then surfaces downstream as a generic `Environment name is required`. There is no positive "is my key accepted?" probe.
4. **Two conflated `Bearer` meanings.** The gateway-trust token and the tenant's Creatio access token both ride `Authorization: Bearer …` at different hops, which is easy to misread.

**Driver decision (Alex):** mcp-http will be **publicly exposed**, so authorization must (a) protect the **whole** endpoint and (b) follow the **standard** MCP model. Implementation on **ASP.NET Core authentication**.

### What the standard requires (MCP 2025-11-25, normative)

- The MCP server is an **OAuth 2.1 Resource Server (RS)**; a **separate Authorization Server (AS)** issues tokens. Co-hosting is allowed but the roles are distinct.
- The RS **MUST** validate the bearer JWT on **every** request and **MUST** verify the token's **audience/resource** is this server (RFC 8707) — else `401`.
- The RS **MUST** serve **Protected Resource Metadata** (RFC 9728) at `/.well-known/oauth-protected-resource` (path-aware variants recognized in 2025-11-25) listing `authorization_servers`, and **MUST** return `401` with a `WWW-Authenticate` header carrying the `resource_metadata` URI (and MAY carry `scope` for step-up).
- **Token passthrough is explicitly forbidden**: the RS **MUST NOT accept or transit** tokens intended for another service; if it calls an upstream API it uses a **separate** upstream token.
- AS discovery via RFC 8414 or **OIDC Discovery**. **DCR (RFC 7591) is now `MAY`** — **pre-registration is first-priority when a client relationship already exists** (our case). Interactive clients use PKCE (`S256`).

### Our client is a machine, not a human

The MCP client is the **AI-Platform gateway** — a trusted service, no human in the loop. The interactive authorization-code+PKCE flow does not fit. The accepted MCP pattern for this is the official opt-in **OAuth Client Credentials extension** (`io.modelcontextprotocol/oauth-client-credentials`): a pre-registered confidential client requests a short-lived token via `grant_type=client_credentials` with an RFC 8707 `resource` parameter.

### C# SDK already supports this

The `ModelContextProtocol.AspNetCore` package composes with ASP.NET auth: `AddJwtBearer` for JWT validation, `.AddMcp(...)` registering `McpAuthenticationHandler` (serves the PRM endpoint + enriches the `401` challenge) with a `ResourceMetadata { Resource, AuthorizationServers, ScopesSupported }` object, `app.MapMcp(path).RequireAuthorization(...)`, and `.AddAuthorizationFilters()` for per-tool `[Authorize]`. This is an **evolving** surface — the package version must be pinned and verified, not copied from older previews.

---

## Decision

Replace the bespoke `PlatformApiKeyGate` front door with **standard MCP OAuth 2.1 Resource-Server authorization** implemented on ASP.NET Core, protecting the **entire** `/mcp` endpoint. Keep the tenant Creatio credential (`X-Integration-Credentials`) as a **separate upstream credential plane** that is never confused with, or derived from, the client's MCP token.

**Two clearly separated credential planes:**

```
                    ┌─────────────────────── FRONT DOOR (standard MCP auth) ───────────────────────┐
  AI-Platform  ──── │ Authorization: Bearer <JWT>   (aud = clio-mcp canonical URI, iss = id-platform)│ ──── clio mcp-http (RS)
  gateway           │ obtained via client_credentials from identity-platform (AS)                    │        │ validates iss/aud/exp/scope
                    └───────────────────────────────────────────────────────────────────────────────┘        │ RequireAuthorization on /mcp
                                                                                                               │
                    ┌─────────────────────── BACK DOOR (separate upstream plane) ──────────────────┐          ▼
  same request  ──── │ X-Integration-Credentials: <tenant Creatio creds>  (distinct trust domain)   │ ──── Creatio tenant
                    └───────────────────────────────────────────────────────────────────────────────┘   (clio authenticates with a
                                                                                                            SEPARATE credential; the MCP
                                                                                                            JWT is NEVER forwarded)
```

- **Front door** authenticates the *gateway* to clio. clio validates the JWT (issuer = identity-platform, audience = clio-mcp canonical URI, expiry, scope) on every request via ASP.NET `AddJwtBearer` + `RequireAuthorization`.
- **Back door** is the existing model-B passthrough: the tenant's Creatio credential rides `X-Integration-Credentials`. This is **not** forbidden token passthrough because (a) the MCP JWT stays audience-bound to clio and is never forwarded upstream, and (b) the Creatio credential is a distinct, intentionally provisioned secret of a different trust domain.
- **Binding the two planes:** the authenticated gateway principal (JWT claims) must be **authorized to act for the asserted tenant** — clio never trusts tenant identity solely because it appears in the header.

---

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| **A: Standard OAuth 2.1 RS (ASP.NET AddJwtBearer + SDK McpAuthenticationHandler), whole-endpoint** | MCP-standard, interoperable, whole-endpoint, native SDK support, clear PRM/401 discovery | Requires an AS (identity-platform) and a pre-registered confidential gateway client; evolving SDK surface | **Chosen** |
| B: Keep static `--platform-api-key` gate | Zero new deps | Non-standard, partial coverage, opaque failures, not fit for public exposure | Rejected (the problem this ADR fixes) |
| C: mTLS / workload identity only | Strong channel auth for a private gateway link | Not the MCP-standard token model; no per-request principal/scope; poor interop | Rejected as the primary front door; **retained as an optional defense-in-depth layer** for the gateway↔clio hop |
| D: Interactive auth-code + PKCE as the primary flow | The MCP "default" flow | No human in the loop for a platform gateway; wrong ergonomics | Rejected for the gateway; PKCE stays relevant only if a future human/direct-client mode is added |
| Grant type: reusable client secret | Simplest to configure | Long-lived shared secret; weaker than key-based | Rejected in favor of `private_key_jwt` (secret allowed only as a documented fallback) |

---

## Design Decisions

### D-1 — Front-door authentication: ASP.NET JWT bearer, whole endpoint
- `AddAuthentication` + `AddJwtBearer("Bearer", …)` with `Authority = <identity-platform issuer>`, `Audience = <scope-derived audience string, e.g. creatio_ai_api or clio_mcp_api>`, and `TokenValidationParameters` validating issuer, audience, lifetime, and signing key (JWKS via OIDC discovery, **RS256**).
- **Issuer split (spike):** the token `iss` is the **public** authority while internal pods reach the IdP over **internal** K8s DNS — accept **both** the discovery-doc issuer and the configured `Authority`, trailing-slash-normalized (mirror control-plane `IdentityPlatformTokenValidator.cs:141-157`).
- **Precedent to copy:** `creatio-ai-feature-flag-service Program.cs:405-425` (clean `AddJwtBearer` against `IdentityPlatformAuth__{Authority,Audience,RequireHttpsMetadata}`); richer manual-JWKS variant in control-plane `IdentityPlatformTokenValidator.cs`.
- `AddAuthorization` with a scope policy (e.g. `RequireClaim("scope", "mcp:tools")`).
- `app.MapMcp(path).RequireAuthorization(<policy>)` after `UseAuthentication()/UseAuthorization()` — **every** request (passthrough AND `-e`/stored) now needs a valid token. This closes the "gates only passthrough" gap.
- Evaluate `.AddAuthorizationFilters()` for per-tool `[Authorize]`/scope enforcement; endpoint-level auth alone protects the transport but does not filter individual tools/prompts/resources.

### D-2 — Discovery: PRM + WWW-Authenticate via the SDK handler
- Register the SDK MCP authentication scheme (`.AddMcp("Mcp", …)` with `ForwardAuthenticate = "Bearer"`) and a `ResourceMetadata { Resource = <canonical URI>, AuthorizationServers = [<identity-platform>], ScopesSupported = [<scopes>] }`.
- `McpAuthenticationHandler` serves `/.well-known/oauth-protected-resource` and augments the `401` with the `resource_metadata` URI automatically — do not hand-roll these.

### D-3 — Grant type: client credentials (pre-registered confidential client) — CORRECTED by Story-1 spike
- The gateway is a **pre-registered confidential client** at identity-platform (DCR is `MAY` and unnecessary — the relationship is known). ✅ `AllowClientCredentialsFlow()` confirmed (`DependencyInjection.cs:452`).
- **Client authentication = client secret** (❌ `private_key_jwt`/RFC 7523 is NOT available on this OpenIddict IdP — spike NO-GO). The earlier "prefer private_key_jwt" is dropped.
- The gateway requests a **short-lived** token via `grant_type=client_credentials` with a least-privilege **`scope`** whose registered resource is clio's audience. **No RFC 8707 `resource` parameter** — the IdP calls `DisableResourceValidation()` and derives `aud` from the scope (see D-2/D-4). 
- clio (RS) validates issuer, **scope-derived audience**, expiry, scopes.
- Negotiate `io.modelcontextprotocol/oauth-client-credentials` where the client supports it (advisory; not required by this IdP).

### D-4 — Configuration (issuer/audience/scope), OIDC-discovery based, provider-swappable
- New options (kebab-case per CLIO001), each with an env-var counterpart, e.g. `--auth-issuer` / `CLIO_MCP_HTTP_AUTH_ISSUER`, `--auth-audience`, `--auth-required-scopes`. Exact names finalized in stories.
- Issuer resolved via OIDC discovery (`/.well-known/openid-configuration`) so the concrete provider stays swappable behind config.
- **When no issuer is configured, authorization is OFF and the endpoint behaves exactly as today** (fail-safe for local/dev and to keep stdio/`-e` unaffected) — but a **public bind (`--host 0.0.0.0`) with no issuer configured MUST emit a loud startup warning** (candidate: refuse to start unless an explicit `--allow-insecure-public` escape hatch is set; decide in stories).

### D-5 — Back door unchanged in shape, hardened in controls
- `X-Integration-Credentials` remains the tenant Creatio credential (model B). Retain the ENG-93208 SSRF allowlist, nothing-persisted, per-tenant isolation.
- **New hardening (from MCP security best practices):**
  - Accept the credential header **only** on an authenticated request; **strip any inbound same-named header at the public edge** so an unauthenticated caller cannot inject it.
  - **Authorize gateway→tenant**: the JWT principal must be permitted to act for the tenant asserted in the header; never trust tenant identity from the header alone.
  - Keep the MCP-token plane and the Creatio-credential plane in **separate stores / log-redaction paths**; rotate and audit independently.
  - **Consider an opaque credential reference** (the gateway sends a handle; clio resolves the real secret server-side) instead of raw creds in the header — evaluate cost/benefit in a story.
- **Invariant (test-asserted):** the client's MCP JWT is **never** placed on any outbound Creatio request.

### D-6 — Fate of `--platform-api-key`
- Once standard OAuth is in place, `--platform-api-key` is redundant as the front door. Decision: **retire it as the default**, but **may** be kept as an explicit, clearly-labelled non-OAuth dev/offline fallback behind a separate opt-in flag (not on by default). Finalize retire-vs-keep in stories; if kept, it must not weaken the OAuth path.

### D-7 — Unified auth error taxonomy
- All authorization outcomes resolve at the **auth layer, before** the tool layer: `401` (missing/invalid/expired token, or wrong audience) with a spec-compliant `WWW-Authenticate`; `403` (insufficient scope / gateway not authorized for tenant); `400` (malformed request/header). No secret is ever echoed. This removes today's mix of 401/400/tool-level exit-code-1 for auth failures.

---

## Consequences

**Positive**
- Whole-endpoint protection suitable for public exposure; the "gates only passthrough" gap is closed.
- Standard, interoperable MCP authorization; native SDK discovery (PRM + `401` challenge) instead of bespoke behavior.
- Clear separation of the two credential planes removes the "two Bearers" confusion and keeps clio on the right side of the token-passthrough ban.
- Positive, predictable failure taxonomy; discovery is self-describing (a client can find the AS from a `401`).

**Negative / costs**
- Hard dependency on an Authorization Server (identity-platform) and a pre-registered confidential gateway client; deployment now has an identity prerequisite.
- Evolving SDK auth surface — version pinning and verification required.
- A misconfigured issuer/audience bricks the endpoint (mitigated by D-4 fail-safe-off when unconfigured + loud public-bind warning).

**Out of scope (follow-ups)**
- Interactive human/direct-client auth (auth-code+PKCE) — only if a non-gateway client mode is later required.
- mTLS/workload-identity for the gateway↔clio hop — optional hardening, can be added independently (D-3/C).
- The name-based-tool passthrough gap (ENG-93347) and the `IsNetCore` header gap (ENG-93348) are separate follow-ups, not part of this ADR.

---

## Open Questions (for the PRD/stories phase)

- **OQ-A:** Final option/env-var names for issuer/audience/scopes, and whether a public bind with no issuer **refuses to start** vs warns.
- **OQ-B — RESOLVED (Story-1 spike, metarepo evidence; see [identity-platform-spike-findings.md](../mcp-http-standard-authorization/identity-platform-spike-findings.md)):** identity-platform = OpenIddict 7.5.0. `client_credentials` **GO**; RFC 8707 `resource` **NO-GO** (IdP `DisableResourceValidation()`, `aud` is scope-derived); `private_key_jwt` **NO-GO** (client **secret** only); issuer/JWKS/RS256 **GO**. **Design deviation from the MCP-spec letter (accepted for platform interop):** clio validates `aud` against a scope-derived audience string (`creatio_ai_api` or a new `clio_mcp_api`), not against its canonical URI, and does not require the client to send `resource`. Every existing platform RS (feature-flag-service, control-plane) already does exactly this. Remaining sub-decision → OQ-C: reuse `creatio_ai_api` vs register a dedicated `clio_mcp_api` scope.
- **OQ-C:** Exact `ResourceMetadata`/scope shape identity-platform expects, and the canonical URI form for clio-mcp behind the K8s service / ingress.
- **OQ-D:** Retire `--platform-api-key` outright, or keep it as an opt-in dev fallback? (D-6.)
- **OQ-E:** Raw tenant credentials in the header vs an opaque reference resolved server-side (D-5).
- **OQ-F:** Pin which `ModelContextProtocol.AspNetCore` version exposes the `AddMcp`/`McpAuthenticationHandler`/`AddAuthorizationFilters` surface used here.
- **OQ-G — SCOPED DOWN (Story 6, 2026-07-11):** D-5's "gateway→tenant authorization" (the authenticated principal's JWT claims must permit acting for the tenant asserted in `X-Integration-Credentials`) has **no real claim contract to enforce today**. The Story-1 spike confirmed identity-platform's `client_credentials` token authenticates the gateway as a whole (audience is scope-derived — `creatio_ai_api`/`clio_mcp_api`) and mints no per-tenant/org claim for that client type (`org_slug`/`org_id` exist only on control-plane's **user** tokens, a different flow). Inventing a claim name now would be a fictional check that authorizes nothing — worse than an honestly-documented gap. **Decision:** any request that clears the standard bearer-JWT check (Story 3/5) is trusted to assert any tenant via the header — identical to the trust boundary the retiring platform-API-key gate already had, so this is not a regression. Filed to the platform team as a follow-up (ENG-93386 Jira comment): define a per-tenant/org claim for the gateway's client_credentials token, then wire real enforcement here.

---

## References

- MCP Authorization (2025-11-25): https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization
- MCP Security Best Practices (token passthrough / confused deputy): https://modelcontextprotocol.io/docs/tutorials/security/security_best_practices
- OAuth Client Credentials extension: https://modelcontextprotocol.io/extensions/auth/oauth-client-credentials
- C# SDK `McpAuthenticationHandler`: https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationHandler.html
- C# SDK authorization filters: https://csharp.sdk.modelcontextprotocol.io/concepts/filters.html
- RFC 9728 (Protected Resource Metadata), RFC 8707 (Resource Indicators), RFC 8414 (AS Metadata), RFC 7591 (DCR), RFC 7523 (`private_key_jwt`)
- Reference deployments: Cloudflare-hosted MCP authorization — https://developers.cloudflare.com/agents/model-context-protocol/protocol/authorization/
