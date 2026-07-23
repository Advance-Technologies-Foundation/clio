# mcp-http

## Command Type

    AI integration commands

## Name

mcp-http - start MCP server in HTTP transport mode

## Description

Starts a Model Context Protocol (MCP) server that communicates over
HTTP using the Streamable HTTP transport (JSON-RPC 2.0 over POST).
This server exposes the same clio tools available in stdio mode
(`clio mcp-server`) to AI agents and platforms that connect over HTTP.

The server runs until the process is terminated (Ctrl+C or SIGTERM).

## Prerequisites

`clio mcp-http` hosts the MCP endpoint on ASP.NET Core, so clio requires the
**ASP.NET Core shared runtime** (`Microsoft.AspNetCore.App`) in addition to the base
.NET runtime. The .NET SDK bundles it — SDK installs need nothing extra. Runtime-only
installs that ship only `Microsoft.NETCore.App` are not supported (see the Installation
section of the main README).

**Patched runtime (deployment / runtime security).** Because the OAuth edge exposes a public
Kestrel endpoint, the operative security control is the **installed ASP.NET Core shared runtime**,
not clio's NuGet pins — clio references ASP.NET Core through `Microsoft.AspNetCore.App`
(`FrameworkReference`), so the Kestrel/JwtBearer assemblies loaded at runtime come from the machine's
shared runtime. CVE-2025-55315 (request-smuggling / security-feature-bypass, CVSS 9.9) affects
ASP.NET Core 8 runtimes `<= 8.0.20`. Deploy the OAuth-enabled edge on a **patched** runtime:
**.NET 8 `>= 8.0.21`** or **.NET 10 `>= 10.0.10`** (current servicing).

## Security

The HTTP transport exposes the same tools as stdio mode — each acting with the operator's
stored credentials for every registered environment — so the server validates incoming
requests to prevent DNS-rebinding and cross-origin abuse (the MCP spec makes this the
host's responsibility):

- **Host header** is restricted to the bound host (`--host`, plus loopback aliases when
  bound to loopback); unexpected `Host` values are rejected (HTTP 400).
- **Origin header**, when present, is restricted to the bound host / loopback; requests
  from any other origin are rejected (HTTP 403). Native MCP clients send no `Origin`
  header and are unaffected.

Standard OAuth 2.1 Resource-Server authorization is available (see
[Whole-endpoint authorization](#whole-endpoint-authorization--public-bind-guard) below) but **off by
default**. With it off, `--host 0.0.0.0` exposes the endpoint to every host that can reach the machine
on the LAN with no credential check — use it only on trusted networks, or configure `--auth-authority`.

## Options

| Option | Default | Description |
|--------|---------|-------------|
| `--port` | `8005` | Port to listen on |
| `--host` | `127.0.0.1` | Host address to bind to |
| `--path` | `/mcp` | MCP endpoint path |
| `--platform-api-key` | _(unset)_ | **Non-OAuth dev/offline fallback.** Comma-separated platform API key set (also read from `CLIO_MCP_HTTP_PLATFORM_API_KEY`). Setting at least one key **enables** per-request credential passthrough; with no key set the credential header is ignored (fail-closed, off by default). **IGNORED entirely when `--auth-authority` is configured** — see [Platform-API-key disposition](#platform-api-key-disposition-devoffline-fallback-only). See also [Credential passthrough](#credential-passthrough-multi-tenant-edge). |
| `--allowed-base-urls` | _(unset)_ | Comma-separated SSRF allowlist of origins a passthrough target `url` may reach. Each entry **must include a scheme** (e.g. `https://tenant.creatio.com`). |
| `--session-idle-ttl` | `5m` | Idle time after which an unused per-session container is evicted. Accepts `90s` / `5m` / `1h` / `1d`, bare seconds (`300`), or a `TimeSpan` (`00:05:00`). |
| `--max-sessions` | `50` | Maximum per-session containers kept in memory; the least-recently-used one is evicted when exceeded. |
| `--credentials-header-name` | `X-Integration-Credentials` | Name of the request header carrying the base64-encoded JSON credentials. |
| `--auth-authority` | _(unset)_ | OIDC authority (discovery/JWKS base URL) of the OAuth 2.1 Authorization Server whose access tokens this edge accepts (also `CLIO_MCP_HTTP_AUTH_AUTHORITY`). Setting it **enables** standard bearer-JWT authorization; unset ⇒ off, behaves as before. |
| `--auth-audience` | _(unset)_ | Comma-separated accepted audience(s) validated against the token `aud` (also `CLIO_MCP_HTTP_AUTH_AUDIENCE`). |
| `--auth-required-scopes` | _(unset)_ | Comma-separated scope(s) every request must carry, checked against the `scope`/`scp` claim (also `CLIO_MCP_HTTP_AUTH_REQUIRED_SCOPES`). |
| `--auth-issuer` | _(unset)_ | Comma-separated accepted issuer(s) for the token `iss` (also `CLIO_MCP_HTTP_AUTH_ISSUER`). Optional; defaults to the discovery document's issuer. Use when the public token `iss` differs from the internal `--auth-authority`. |
| `--auth-allow-insecure-metadata` | `false` | Allow OIDC metadata/JWKS over plain HTTP (also a truthy `CLIO_MCP_HTTP_AUTH_ALLOW_INSECURE_METADATA`). Default HTTPS-only; use only for an internal-DNS HTTP authority on a trusted network. |
| `--auth-resource` | _(unset)_ | Explicit Protected Resource Metadata `resource` (canonical MCP endpoint URI) override (also `CLIO_MCP_HTTP_AUTH_RESOURCE`). Unset ⇒ derived per-request from the incoming scheme/host/path — correct behind any Host-forwarding ingress. |
| `--allow-insecure-public` | `false` | Allow starting with a public/wildcard `--host` (e.g. `0.0.0.0`), or any other concrete non-loopback host/IP, while authorization is **off** (also a truthy `CLIO_MCP_HTTP_ALLOW_INSECURE_PUBLIC`). Without this flag, clio **refuses to start** in that combination — see [Whole-endpoint authorization](#whole-endpoint-authorization--public-bind-guard). |
| `--auth-allow-any-audience` | `false` | Allow enabling authorization (`--auth-authority`) with **neither** `--auth-audience` **nor** `--auth-required-scopes` configured (also a truthy `CLIO_MCP_HTTP_AUTH_ALLOW_ANY_AUDIENCE`). Without this flag, clio **refuses to start** in that combination — see [Audience/scope guard](#audiencescope-guard). |

When authorization is enabled, **every** request to the MCP endpoint requires a valid token — pre-registered `-e <env>` / stored-credential access included, not just the credential-passthrough leg (`RequireAuthorization` on the `/mcp` endpoint). The edge serves Protected Resource Metadata (RFC 9728) **anonymously** at `/.well-known/oauth-protected-resource`, and an unauthenticated or invalid-token request receives `401` (insufficient scope ⇒ `403`) with a `WWW-Authenticate: Bearer resource_metadata="…"` header naming that discovery URL — powered by the `ModelContextProtocol.AspNetCore` SDK's `McpAuthenticationHandler` (no hand-rolled discovery/challenge code).

### Whole-endpoint authorization + public-bind guard

With no `--auth-authority` configured (the default), authorization is off and the server behaves **exactly** as before this feature — stdio, `-e <env>`, and loopback binds are unaffected.

**Exception (security-first default):** starting with a **reachable** `--host` — a public/wildcard bind (`0.0.0.0`, `*`, `::`, `[::]`) or any other concrete non-loopback address/hostname — while authorization is off is refused — an unauthenticated reachable bind would expose every registered environment's stored credentials to any caller that can reach the port. Configure `--auth-authority`, or pass `--allow-insecure-public` to start anyway (not recommended outside a fully trusted network; a loud warning is logged).

> **Note:** the full authorization model (OAuth 2.1 Resource Server, whole-endpoint enforcement) landed across ENG-93386 Stories 2–5: token-validation configuration, JWT bearer validation, RFC 9728 discovery, the `mcp` authorization policy applied to `/mcp` via `RequireAuthorization`, and this public-bind guard. The final Story 8 adversarial review broadened the guard's definition of "public" from the four literal wildcard spellings to any non-loopback host — a bind to a concrete LAN/public IP or DNS name had silently bypassed it.

### Audience/scope guard

Enabling authorization requires only `--auth-authority`. With **neither** `--auth-audience` **nor** `--auth-required-scopes` also configured, the endpoint would accept **any** token the configured authority ever mints for **any** client/resource — a confused-deputy risk, since the same identity-platform issuer is typically shared across several resource servers (feature-flag-service, control-plane, etc.). clio **refuses to start** in that combination by default; configure `--auth-audience` and/or `--auth-required-scopes`, or pass `--auth-allow-any-audience` to start anyway (not recommended; a loud warning is logged). This guard was added during the Story 8 final adversarial review, which found the combination silently disabled audience validation.

### Platform-API-key disposition (dev/offline fallback only)

Once standard OAuth authorization is configured (`--auth-authority` set), `--platform-api-key` is
**retired as a front door and bypassed entirely** — it is not combined with OAuth (D-6). This is
not a design preference: `Authorization: Bearer …` can carry only one thing at a time, and once
OAuth is enabled that header is claimed by the JWT bearer handler, so the old key check has nothing
of its own left to read. Concretely:

- **OAuth enabled:** passthrough eligibility comes solely from the authenticated principal that
  `RequireAuthorization` already guaranteed for this request. A configured `--platform-api-key` is
  never consulted — a request cannot use a valid key to bypass a missing/invalid OAuth token, and a
  missing/wrong key never blocks an otherwise-valid OAuth request either.
- **OAuth not configured (default):** `--platform-api-key` behaves exactly as before ENG-93386 —
  the sole gate for the credential-passthrough leg, unaffected.
- **Both configured together:** not a security problem (the key is simply inert), but clio logs a
  loud startup warning so it is never a silent misconfiguration — remove `--platform-api-key` /
  unset `CLIO_MCP_HTTP_PLATFORM_API_KEY` once OAuth is the standing front door.

## Credential passthrough (multi-tenant edge)

By default `mcp-http` targets **pre-registered** environments (`-e <env>`) or explicit
connection arguments, exactly like `mcp-server`. The **credential-passthrough edge** lets a
gateway target a **different Creatio tenant on each request** — the target URL and
credentials ride the request header instead of clio settings. This is aimed at AI-platform
gateways that broker many tenants through one clio process.

### Platform-API-key gate (off by default, fail-closed; dev/offline fallback only)

When `--auth-authority` is **not** configured (default), passthrough is gated **solely** by the
platform API key: at least one key set via `--platform-api-key` or the
`CLIO_MCP_HTTP_PLATFORM_API_KEY` environment variable turns it on. With **no** key configured
(the default) the gate fail-closes — the credential header is ignored and the server behaves
exactly as the pre-passthrough release. The verb, stdio mode, and `-e <env>` targeting remain
**always available** regardless of the key.

When `--auth-authority` **is** configured, this key is **ignored entirely** — see
[Platform-API-key disposition](#platform-api-key-disposition-devoffline-fallback-only).

> **The platform API key gates ONLY the passthrough leg.** It does **not** protect
> pre-registered `-e <env>` or explicit-URI access — those paths use credentials already
> stored in clio settings and are reachable regardless of the API key. On a public bind,
> host/origin filtering or a fronting proxy remains the control for stored-credential access.

### Standard-auth header strip (when `--auth-authority` is configured)

When standard OAuth authorization is enabled (`--auth-authority` set), the
`X-Integration-Credentials` header is honored **only on an authenticated request** — a request
whose bearer token failed or was absent has the header **ignored outright**, not merely
deferred. This closes a back-door: a caller cannot smuggle a Creatio credential in through the
passthrough header without first clearing the standard bearer-JWT check that already gates the
whole `/mcp` endpoint (see [Whole-endpoint authorization](#whole-endpoint-authorization--public-bind-guard)
above). With `--auth-authority` unset (default), this has no effect — passthrough keeps working
via the platform-API-key gate alone, unchanged.

The two credential planes never mix: the inbound gateway/platform bearer token (on
`Authorization`) is never attached to any outbound Creatio request — the ephemeral
`EnvironmentSettings` built for the tenant call is constructed **solely** from the parsed
`X-Integration-Credentials` payload. clio is never a confused deputy that forwards its own
inbound token upstream.

> **Gateway→tenant authorization (out of scope for v1).** A finer control — the authenticated
> gateway's JWT claims must explicitly permit acting for the tenant asserted in the header — is
> **not implemented**: the identity-platform's `client_credentials` token authenticates the
> gateway as a whole and mints no per-tenant/org claim for it (verified against the live
> Authorization Server, not assumed). Today, any request that clears the standard bearer-JWT
> check is trusted to assert any tenant via the header — the same trust boundary the
> platform-API-key gate already had, so this is not a regression. Enforcing a real per-tenant
> claim is deferred until the platform team defines that contract.

### Header contract

An authorized passthrough request carries two headers:

```text
Authorization: Bearer <platform-api-key>
X-Integration-Credentials: <base64 JSON>
```

- **`Authorization: Bearer <platform-api-key>`** — must match one of the configured keys.
  Comparison is **constant-time** and no key material is echoed. A missing/invalid key on a
  request that carries the credential header is rejected with **HTTP 401**. Rotate keys by
  supplying both the old and new key in the comma-set.
- **`X-Integration-Credentials`** (name configurable via `--credentials-header-name`) — a
  base64-encoded JSON object. A malformed payload is rejected with **HTTP 400** naming the
  defect only (no secret is echoed).

The JSON payload always requires `url`, authentication material, and the explicit `isNetCore`
runtime discriminator. `isNetCore` must be a JSON boolean (the property name is matched
case-insensitively): `true` selects the root .NET Core/NET 8 route layout, while `false`
selects the .NET Framework route layout. Auth is resolved by precedence **accessToken →
login+password**:

```jsonc
// access token (must be Bearer-typed; .NET Core/NET 8 tenant)
{ "url": "https://tenant.creatio.com", "accessToken": "<access-token>", "isNetCore": true }

// login + password (both required; .NET Framework tenant)
{ "url": "https://tenant.creatio.com", "login": "<login>", "password": "<password>", "isNetCore": false }
```

The runtime field is header context, not an MCP tool argument. Omitting it, setting it to
`null`, or using a string, number, array, or object is rejected with **HTTP 400 before target
validation, client creation, or any outbound call**. There is no default and clio does not
probe the tenant to infer the runtime. The selected runtime is also part of the in-memory
tenant cache and lock identity, so equivalent credentials for the two route layouts never
share a client accidentally. For `true`, service routes remain at their root paths; for
`false`, clio inserts exactly one `/0/` segment for .NET Framework compatibility.

> **Cookie leg (v1):** a `{ "url": ..., "cookie": ..., "isNetCore": false }` payload parses, but a
> cookie-only credential is **rejected as unsupported in v1** — supply an access token instead. A
> non-`Bearer` access-token type is likewise rejected.

### SSRF / egress allowlist

The caller-supplied `url` is validated **before any outbound call** (Story 6):

- **Baseline blocks (always on, regardless of the allowlist):** cloud-metadata
  (`169.254.169.254` and the IPv6 IMDS `fd00:ec2::254`), IPv4/IPv6 link-local (`169.254.0.0/16`,
  `fe80::/10`), IPv6 unique-local (`fc00::/7`), the unspecified addresses (`0.0.0.0` / `::`, which
  the OS routes to the local host), and loopback (loopback is permitted only when the server
  itself is bound to loopback). IP-literal, integer/hex/octal, IPv4-mapped-IPv6, and
  single-trailing-dot encodings are all normalized before the check.
- **HTTPS required for non-loopback targets:** a plaintext `http://` target is rejected for any
  non-loopback host — the forwarded bearer token / password would otherwise cross the network in
  the clear (the bearer transport does not enforce TLS certificate validation). `http://` is
  allowed **only** for an explicit loopback dev target (`localhost` / `127.0.0.0/8` / `::1`).
  > **On-prem note:** an on-prem Creatio served over plain `http` on a private network will be
  > rejected with a scheme error — front it with TLS (`https`) or use pre-registered `-e <env>`
  > targeting instead of the passthrough header.
- **`--allowed-base-urls`:** when set, the target **origin** (scheme+host+port) must be on
  the list. Each entry must include a scheme (`https://…`); a set with no valid absolute
  http/https origin **fails fast at startup** rather than silently degrading to baseline-only.
  When unset, only the baseline blocks apply and any other reachable https host is allowed.

> **For PUBLIC deployments, setting `--allowed-base-urls` is REQUIRED.** The no-allowlist
> baseline blocks only cloud-metadata, link-local, and loopback — it does **not** block
> general RFC1918 private ranges (`10/8`, `172.16/12`, `192.168/16`). Without an allowlist a
> passthrough caller can reach arbitrary private-network hosts the clio process can route to.

> **DNS-rebinding TOCTOU residual:** a non-literal hostname is **not** DNS-resolved by the
> validator. A hostname that passes the allowlist but resolves to a blocked IP *after*
> validation (the client does its own resolution when it dials) is a documented residual and
> is **out of scope for v1**.

### Nothing persisted, memory-pooled

clio stores **no** passthrough credential. Each request builds an **ephemeral**
`EnvironmentSettings` directly from the header — never written to settings, disk, or
`appsettings.json`. Per-tenant containers are **pooled in memory** and evicted by idle-TTL
(`--session-idle-ttl`) and an LRU cap (`--max-sessions`), so a rotating stream of tokens
stays memory-bounded. The bounded cache also auto-recovers stale sessions: an evicted
container is transparently rebuilt from the request on the next call after the idle window.

### Arg policy (mode-gated)

While passthrough is active for a request, supplying `uri` / `login` / `password` /
`client-id` / `client-secret` or an environment name as tool **arguments** is **rejected** —
they would otherwise be silently ignored (the header wins). Credentials must ride the header,
not the arguments. Stdio mode and default (non-passthrough) HTTP requests keep honoring these
arguments unchanged.

## Examples

Start with default settings (port 8005, localhost):

```shell
clio mcp-http
```

Start on a custom port:

```shell
clio mcp-http --port 9000
```

Listen on all network interfaces (no authentication — use only on trusted networks):

```shell
clio mcp-http --host 0.0.0.0
```

Custom endpoint path:

```shell
clio mcp-http --path /api/mcp
```

Enable the multi-tenant credential-passthrough edge (setting a platform API key turns it on):

```shell
clio mcp-http --host 0.0.0.0 \
  --platform-api-key <platform-api-key> \
  --allowed-base-urls https://tenant.creatio.com
```

## Testing with curl

Send an `initialize` request:

```shell
curl -X POST http://localhost:8005/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}'
```

List available tools:

```shell
curl -X POST http://localhost:8005/mcp \
  -H "Content-Type: application/json" \
  -H "Mcp-Session-Id: <session-id-from-initialize>" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
```

## Environment Variables

| Variable | Description |
|----------|-------------|
| `CLIO_MCP_RESPECT_AMBIENT_PROXY` | When `true` or `1`, do NOT neutralize inherited proxy env vars. Default: proxy is bypassed. |
| `CLIO_MCP_HTTP_PLATFORM_API_KEY` | Comma-separated platform API key set, unioned with `--platform-api-key`. Setting at least one key enables per-request credential passthrough. |

## See Also

- [`mcp-server`](mcp-server.md) - Start MCP server in stdio mode
