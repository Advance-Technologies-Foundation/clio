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

There is **no built-in authentication**. `--host 0.0.0.0` still exposes the endpoint to
every host that can reach the machine on the LAN — use it only on trusted networks.

## Options

| Option | Default | Description |
|--------|---------|-------------|
| `--port` | `8005` | Port to listen on |
| `--host` | `127.0.0.1` | Host address to bind to |
| `--path` | `/mcp` | MCP endpoint path |
| `--platform-api-key` | _(unset)_ | Comma-separated platform API key set (also read from `CLIO_MCP_HTTP_PLATFORM_API_KEY`). Setting at least one key **enables** per-request credential passthrough. See [Credential passthrough](#credential-passthrough-multi-tenant-edge). |
| `--allowed-base-urls` | _(unset)_ | Comma-separated SSRF allowlist of origins a passthrough target `url` may reach. Each entry **must include a scheme** (e.g. `https://tenant.creatio.com`). |
| `--session-idle-ttl` | `5m` | Idle time after which an unused per-session container is evicted. Accepts `90s` / `5m` / `1h` / `1d`, bare seconds (`300`), or a `TimeSpan` (`00:05:00`). |
| `--max-sessions` | `50` | Maximum per-session containers kept in memory; the least-recently-used one is evicted when exceeded. |
| `--credentials-header-name` | `X-Integration-Credentials` | Name of the request header carrying the base64-encoded JSON credentials. |

## Credential passthrough (multi-tenant edge)

By default `mcp-http` targets **pre-registered** environments (`-e <env>`) or explicit
connection arguments, exactly like `mcp-server`. The **credential-passthrough edge** lets a
gateway target a **different Creatio tenant on each request** — the target URL and
credentials ride the request header instead of clio settings. This is aimed at AI-platform
gateways that broker many tenants through one clio process.

### Double gate (off by default)

Passthrough is honored only when **both** gates are satisfied:

1. **Incubation feature flag** `mcp-http-credential-passthrough`, enabled with:

   ```shell
   clio experimental --name mcp-http-credential-passthrough --enable
   ```

   It is deliberately **not** a `[FeatureToggle]` on the verb — that would hide `mcp-http`
   entirely. Only the passthrough leg is gated; the verb, stdio mode, and `-e <env>`
   targeting remain **always available**.

2. **Platform API key** — at least one key set via `--platform-api-key` or the
   `CLIO_MCP_HTTP_PLATFORM_API_KEY` environment variable.

When **either** gate is off, the credential header is ignored and the server behaves exactly
as the pre-passthrough release.

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

The JSON payload always requires `url` plus authentication material. Auth is resolved by
precedence **accessToken → login+password**:

```jsonc
// access token (must be Bearer-typed)
{ "url": "https://tenant.creatio.com", "accessToken": "<access-token>" }

// login + password (both required)
{ "url": "https://tenant.creatio.com", "login": "<login>", "password": "<password>" }
```

> **Cookie leg (v1):** a `{ "url": ..., "cookie": ... }` payload parses, but a cookie-only
> credential is **rejected as unsupported in v1** — supply an access token instead. A
> non-`Bearer` access-token type is likewise rejected.

### SSRF / egress allowlist

The caller-supplied `url` is validated **before any outbound call** (Story 6):

- **Baseline blocks (always on, regardless of the allowlist):** cloud-metadata
  (`169.254.169.254`), IPv4/IPv6 link-local, and loopback (loopback is permitted only when
  the server itself is bound to loopback). IP-literal, integer/hex/octal, IPv4-mapped-IPv6,
  and single-trailing-dot encodings are all normalized before the check.
- **`--allowed-base-urls`:** when set, the target **origin** (scheme+host+port) must be on
  the list. Each entry must include a scheme (`https://…`); a set with no valid absolute
  http/https origin **fails fast at startup** rather than silently degrading to baseline-only.
  When unset, only the baseline blocks apply and any other reachable https host is allowed.

> **DNS-rebinding TOCTOU residual:** a non-literal hostname is **not** DNS-resolved by the
> validator. A hostname that passes the allowlist but resolves to a blocked IP *after*
> validation (the client does its own resolution when it dials) is a documented residual and
> is **out of scope for v1**.

### Nothing persisted, memory-pooled

clio stores **no** passthrough credential. Each request builds an **ephemeral**
`EnvironmentSettings` directly from the header — never written to settings, disk, or
`appsettings.json`. Per-tenant containers are **pooled in memory** and evicted by idle-TTL
(`--session-idle-ttl`) and an LRU cap (`--max-sessions`), so a rotating stream of tokens
stays memory-bounded.

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

Enable the multi-tenant credential-passthrough edge:

```shell
clio experimental --name mcp-http-credential-passthrough --enable
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
