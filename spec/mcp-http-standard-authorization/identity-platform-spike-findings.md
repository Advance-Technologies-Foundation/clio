# Story 1 spike findings — identity-platform M2M capability (OQ-B)

**Jira**: ENG-93386 · **Story**: story-mcp-http-standard-authorization-1 · **Date**: 2026-07-10
**Method**: read-only investigation of the AI-Platform metarepo `crt-ai-agentic-platform-metarepo` (primary source; `creatio.ghe.com/engineering/crt-ai-agentic-platform-metarepo`). All line refs are to that metarepo.

## Verdict summary

| OQ-B assumption | Verdict | Consequence for the design |
|---|---|---|
| `client_credentials` grant available | **GO** | Gateway can be a confidential client; clio validates the resulting JWT as RS. |
| RFC 8707 `resource` param → audience | **NO-GO** | IdP ignores `resource`; `aud` is **scope-derived**. clio must validate `aud` against the scope's registered audience string, **not** clio's canonical URI. Documented deviation from the MCP-spec letter. |
| `private_key_jwt` (RFC 7523) client auth | **NO-GO (secret only)** | Gateway authenticates to the IdP with a **client secret**, not a key assertion. Drop the "prefer private_key_jwt" line. |
| Issuer / OIDC discovery / JWKS | **GO** | OpenIddict 7.5.0, RS256, `{issuer}/.well-known/openid-configuration`, access tokens **unencrypted** (RS sees plain JWT). |
| Canonical audience | **GO (caveat)** | Reuse shared `creatio_ai_api`, or register a dedicated `clio_mcp_api` scope. No `<service>-api` (hyphen) convention; audiences are snake_case `<domain>_api`. |
| Existing RS precedent to copy | **GO** | `feature-flag-service` (clean `AddJwtBearer`) + `control-plane IdentityPlatformTokenValidator` (manual JWKS/RS256). Config section `IdentityPlatformAuth__*`. |
| clio-mcp-server auth today | **NONE** | Launched `mcp-http --host 0.0.0.0 --port 8005 --path /mcp`; egress-allowlisted `clio-mcp-server:8005`; MCP surface explicitly unauthenticated. |

## Evidence (decisive lines)

**IdP = OpenIddict 7.5.0**; server config `creatio-identity-platform/src/Creatio.IdentityPlatform.Infrastructure/DependencyInjection.cs`:
- Grants (`:451-455`): `AllowAuthorizationCodeFlow` + **`AllowClientCredentialsFlow`** + `AllowPasswordFlow` + `AllowRefreshTokenFlow` + token-exchange custom flow.
- Audience/resource (`:469-472`): `// We're not using explicit audience/resource parameters` → `DisableAudienceValidation(); DisableResourceValidation();`
- Registered scopes (`:458-467`): `openid profile email phone identity_platform_api identity_platform_admin creatio_ai_api` (+ feature-flags/audit/... in `IdentityServerHostOptions.cs`).
- Issuer (`:445-447`): `SetIssuer(effectiveIssuerUri)`; default `IssuerUri = https://localhost:7080` (`IdentityServerHostOptions.cs:7`). OIDC endpoints `:433-437`. Signing = RSA/RS256 (`:901,905`); `DisableAccessTokenEncryption()` (`:476`).
- Audience binding mechanism: each OpenIddict scope seeds `descriptor.Resources.Add(resource)` (`OpenIddictSeedService.cs:67-74`); minted `aud` = that resource. Comment: `aud=creatio_ai_api` (`IdentityServerHostOptions.cs:91-93`). Patterns: `AddSelfResourceScope(...)` (aud = scope name) vs explicit `Resources = [ApiScopeName]` (`ProvisioningManifestFactory.cs:80-124`).
- Client auth: secret only — `ClientSecret`/`ClientSecretEncrypted` (`IdentityServerHostOptions.cs:15,35,45,183,199` + OpenIddict app-table migrations). No `private_key_jwt`/`client_secret_jwt`/`ClientAssertion` anywhere in identity-platform.

**RS precedents:**
- `creatio-ai-feature-flag-service/.../Program.cs:405-425`: `AddAuthentication(JwtBearerDefaults...).AddJwtBearer(...)` with `options.Authority`, `options.Audience`, `RequireHttpsMetadata`; config section `IdentityPlatformAuth` → env `IdentityPlatformAuth__Authority/__Audience/__RequireHttpsMetadata` (fail-fast on empty Authority `:354-355`). Scope enforced from the `scope` claim, space-split (`:623,644-651`).
- `creatio-ai-control-plane/.../Auth/IdentityPlatformTokenValidator.cs`: discovery `{Authority}/.well-known/openid-configuration` (`:51`); issuer validated against **both** discovery `config.Issuer` and configured `Authority`, trailing-slash-normalized (`:141-183`); `ValidAudiences = { ClientId, Audience }` (`:150-157`); RS256/PS256 only (`:198-203`); client_credentials detected via `principal_type=service` / absence of user claims (`:471-489`). Options (`IdentityPlatformAuthOptions.cs`): `SectionName="IdentityPlatformAuth"`, default `Audience="creatio_ai_api"`, `Scope="openid profile email creatio_ai_api offline_access"`, `RequireHttpsMetadata=true`, `TenantClaim="org_slug"`, `OrgClaim="org_id"`.

**Authority values (env/helm/compose):** internal K8s DNS `http://creatio-identity-platform.<ns>.svc.cluster.local:8080` (helm local `:226`); compose `http://identity-platform:8080` (`:230,352`); stage `https://id-ms6.creatio.com` / `https://stg-identity.creatio.com`. Token `iss` = **public** authority; internal pods fetch discovery/JWKS over **internal** DNS → the RS must accept both issuers (control-plane precedent).

**clio-mcp-server today:** `helm/clio-mcp-server/values.yaml:46` "MCP surface is unauthenticated"; `docker/Dockerfile:49-50` ENTRYPOINT `clio mcp-http` CMD `--host 0.0.0.0 --port 8005 --path /mcp` (CLIO_VERSION 8.1.0.72); egress allowlist `clio-mcp-server:8005` (`docker-compose.yml:479,678,869-870`). No `--platform-api-key`, no `McpServices__Services__clio` registration found.

## Design implications (fold into ADR/PRD)

1. **Grant:** `client_credentials` with a **client secret** (NOT private_key_jwt). Gateway pre-registered as a confidential client at identity-platform.
2. **Audience:** validate `aud` against a **scope-derived audience string** — reuse **`creatio_ai_api`** (the shared Agentic Platform audience the control-plane already carries) OR register a dedicated **`clio_mcp_api`** scope in `IdentityServerHostOptions`/seed. Do **not** validate against a per-server canonical URI (the IdP won't emit that).
3. **No RFC 8707 `resource`** on the client side; do not require it. This is a **documented deviation** from the MCP-spec letter, justified by platform interoperability (every existing platform RS does scope→audience). PRM `authorization_servers` still advertised for discovery; `ScopesSupported` = the platform scope(s).
4. **Issuer:** accept **both** the discovery-doc issuer (public) and the configured `Authority` (internal DNS), trailing-slash-normalized — mirror `IdentityPlatformTokenValidator.cs:141-157`.
5. **Config surface:** reuse the platform convention `IdentityPlatformAuth__{Authority,Audience,RequireHttpsMetadata}` (and `ClientSecret` only if clio ever needs to be a client; as an RS it needs Authority+Audience). Map clio's own kebab-case flags onto these or read the same section.
6. **Implementation model:** the `feature-flag-service` `AddJwtBearer` shape is the minimal precedent; the C# MCP SDK `AddMcp`/`McpAuthenticationHandler` layers PRM discovery on top. Both are compatible.
7. **RS256** signing; tokens unencrypted — standard `AddJwtBearer` JWKS validation works directly.

## GO/NO-GO for Story 1
**GO to proceed** with the standard-auth design, **with two corrections**: drop `private_key_jwt` (use client secret) and drop RFC 8707 `resource`/canonical-URI audience (use scope-derived `creatio_ai_api` or a new `clio_mcp_api`). No blocker remains; the concrete audience/scope choice (reuse vs register) is the one decision to confirm with the platform team (see follow-up).
