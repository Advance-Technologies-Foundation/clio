# mcp-server

## Command Type

    AI integration commands

## Name

mcp-server, mcp - start MCP server in stdio mode

## Description

Starts a Model Context Protocol (MCP) server that communicates over
standard input/output using JSON-RPC 2.0. This server exposes clio
tools to AI agents and code assistants (Copilot, Claude, Cursor, etc.)
that support the MCP protocol.

The server runs until the stdin stream is closed or the process is
terminated.

Available MCP tool categories:
- application     Create, list, and inspect Creatio applications
- entity          Create and update entity schemas (DB-first)
- sync-schemas     Batch entity operations in a single call
- page            Get and update Freedom UI page schemas
- get-component-info  Inspect curated Freedom UI component contracts
- sync-pages       Batch page operations in a single call
- data-binding    Manage data bindings and seed data
- telemetry       Record local product telemetry for app-creation workflows:
  - get-telemetry-consent  Read the locally stored telemetry consent (granted/denied/unknown); never writes
  - send-telemetry         Store one workflow telemetry event as a local OpenTelemetry-shaped event once consent is granted
  - withdraw-telemetry-consent  Withdraw consent: set the stored decision to denied and delete not-yet-uploaded local events; stops all collection and upload

A production OTLP/HTTP collector endpoint ships as the built-in default, so once consent is
granted, stored events are uploaded in the background (on server start and after each stored
event) as OTLP/HTTP JSON and removed locally on success. Override the endpoint with the settings
file "telemetry.endpoint" or the CLIO_TELEMETRY_ENDPOINT / CLIO_TELEMETRY_INGEST_KEY environment
variables (the endpoint must be https, or http only for a loopback host). Disable uploading
entirely — regardless of consent — with CLIO_TELEMETRY_ENABLED=false or "telemetry.enabled": false
in the settings file; the local spool is then only pruned (age and size caps). Nothing is ever
uploaded unless consent is granted.

Local telemetry is stored under &lt;clio-home&gt;/telemetry (relocate with CLIO_TELEMETRY_HOME;
honors CLIO_HOME). Each event carries only product workflow metadata — session_id,
event_name, timestamps, coding_agent, clio_version, platform, an anonymous installation_id,
and skill/plugin versions — never prompts, secrets, tokens, customer data, or generated
content. Spooled events are pruned after at most 30 days locally; the collected metrics are
retained up to 1 year server-side.

Available MCP guidance resources:
- docs://mcp/guides/app-modeling
- docs://mcp/guides/data-bindings
- docs://mcp/guides/existing-app-maintenance
- docs://mcp/guides/page-schema-handlers
- docs://mcp/guides/page-schema-sdk-common
- docs://mcp/guides/page-schema-validators

Preferred guidance access:
- get-guidance {"name":"app-modeling"}
- get-guidance {"name":"data-bindings"}
- get-guidance {"name":"existing-app-maintenance"}
- get-guidance {"name":"page-schema-handlers"}
- get-guidance {"name":"page-schema-sdk-common"}
- get-guidance {"name":"page-schema-validators"}

External knowledge delivery is configured visibly under `knowledge` in Clio's `appsettings.json`.
The section contains `root-path`, a `sources` map, and optional `topic-pins`. Each trusted source
declares a stable `library-id`, `type` (`git` or `nuget`), credential-free `location`, `enabled`
kill switch, numeric `priority`, and `participation` (`isolated`, `supplement`, or `authoritative`).
NuGet sources also declare `package-id`, `trusted-key-id`, and an absolute local
`trusted-public-key-path`. Git sources use none of those fields; they may follow a branch/tag/commit
and Clio reads content directly from the repository checkout. When no Git reference is supplied, Clio discovers and persists the remote
default branch only after a successful install/update, then records the exact complete resolved
commit for every installed generation. Information and update-availability checks never mutate
source configuration.

Both MCP hosts bootstrap one built-in source while they begin serving requests: `creatio-curated`
(`com.creatio.clio`) follows the `master` branch of
`https://github.com/Advance-Technologies-Foundation/clio-knowledge.git` as an authoritative source
with priority `100`. When the source is absent, Clio adds it and installs its Git checkout. A valid
local checkout is reused without contacting Git, so ordinary MCP restarts remain local-only. A
missing checkout is cloned in the background so the MCP protocol handshake is never delayed by
Git; curated guidance becomes available to the running host as soon as installation completes. An
older alias for the same library is normalized to `creatio-curated` and its checkout is moved to
the canonical source cache instead of cloned again. The source cannot be removed;
set `enabled: false` or run `disable-knowledge-source --alias creatio-curated` to opt out. That
disabled state survives future Clio updates and MCP starts. A failed first clone is logged as a
warning and does not prevent MCP from starting; retry with
`install-knowledge --source creatio-curated` when connectivity returns.

Signing trust is scoped per source so independent publishers can use different keys. The configured
path references public ECDSA P-256 SubjectPublicKeyInfo PEM material; it is not a secret and must
never reference or contain a private signing key.

NuGet sources require signed version 1 bundles. Git sources instead trust the configured public
repository URL, resolve an exact commit, and validate the catalog contract directly from the
checkout; they do not use NuGet bundle-signing keys. The proof of concept supports credential-free
public HTTPS Git and NuGet sources only; authenticated private sources are not supported. Declared
`legacyUris` remain exact aliases for the item that declares them. No implicit version 0
compatibility source is registered; prototype caches must be reinstalled from configured version 1
sources.

The service-index URL must respond directly; redirects are not followed. Its advertised
`PackageBaseAddress/3.0.0` resource must use the same scheme, host, and port as the configured
service-index URL.

Each transport supplies content and immutable provenance. NuGet candidates are verified by package
version, bundle signature, compatibility, identity, monotonic sequence, catalog completeness,
paths, sizes, and digests. Git checkouts are bound to the configured repository and exact resolved
commit, then validated for compatibility, identity, catalog completeness, paths, and sizes before
activation under `knowledge.root-path`. The former top-level
`knowledge-root-path` is migrated once. When no root exists, Clio persists
`<clio-home>/knowledge`. Installed archives and extracted content remain available to users and
coding agents on disk.

Normal guidance lookup, resource reads, and reference-example discovery use only local installed
content and never contact a transport. Explicit lifecycle operations (`install-knowledge` and
`update-knowledge`) and `info-knowledge` with `--check-updates` / `checkUpdates: true` may contact the
configured Git or NuGet transport. These management commands are also non-resident MCP tools
discoverable with `get-tool-contract` and invoked through `clio-run`. An already-running MCP process
compares source activation/configuration on every lookup,
so an update, enable, or disable becomes visible without restarting MCP. A rejected update leaves
the last-known-good generation active for that source.
`info-knowledge` is local-only unless CLI `--check-updates` or MCP `checkUpdates: true` is supplied.
Deleting or invalidating the disk cache stops in-memory external serving on the next lookup. With
no verified active bundle, external guide lookups return typed `guidance-unavailable`; guidance that
remains embedded in the current Clio build is unaffected.

## Synopsis

```bash
clio mcp-server
clio mcp
```

## Examples

```bash
clio mcp-server
Start MCP server and wait for JSON-RPC requests on stdin.

clio mcp-server
Start the same MCP server using the short alias.

Use your MCP client to call get-tool-contract {}.
Discover what tools exist with a compact index (name + one-line purpose + safety flags) without paying for full schemas, then call get-tool-contract with specific tool-names to load only the contracts you need. Pass {"detail":"full"} to expand every tool's full contract at once (legacy behavior).

Use your MCP client to call get-tool-contract {"tool-names":["list-apps","get-app-info","get-page","sync-pages"]}.
Bootstrap an existing-app or page workflow from the authoritative contract before invoking discovery or mutation tools.

Use your MCP client to call get-tool-contract {"tool-names":["get-page","get-component-info","sync-pages"]}.
Bootstrap page inspection/editing and discover whether get-component-info is needed before mutating raw.body.

Use your MCP client to call get-guidance {"name":"page-schema-validators"}.
Read the canonical validator authoring guide through a tool call instead of relying on docs:// URI routing in the client.

Use your MCP client to call get-guidance {"name":"page-schema-handlers"}.
Read the canonical handler authoring guide through a tool call before editing SCHEMA_HANDLERS in raw page bodies.

Use your MCP client to call get-guidance {"name":"page-schema-sdk-common"}.
Read the canonical page-schema SDK guide through a tool call before adding or editing @creatio-devkit/common usage in raw page bodies.

Use your MCP client to call get-guidance {"name":"data-bindings"}.
Read the canonical lookup seeding and binding verification guide before choosing between sync-schemas, DB-first bindings, and local binding artifacts.
```

## Prerequisites

- clio version 8.0.2.35 or higher
- At least one registered clio environment (clio reg-web-app)
- Target Creatio instance must be running and accessible

## Notes

- The server uses stdio transport (stdin/stdout), not HTTP
- Environment-sensitive tools require either an "environment-name" or explicit connection args such as "uri", "login", and "password"
- "get-component-info" is local and read-only, so it does not require environment or connection args
- Start each MCP workflow with "get-tool-contract" so the client reads the authoritative clio MCP contract before the first discovery, inspection, or mutation call
- Preferred existing-app flow starts with get-tool-contract, then list-apps -> get-app-info, then page or schema inspection, then sync-pages / modify-entity-schema-column / sync-schemas as needed
- For Freedom UI page-body handler, validator, or `@creatio-devkit/common` page-schema work, prefer get-guidance instead of relying on client-specific docs:// resource routing
- For lookup seeding or binding artifact work, prefer get-guidance {"name":"data-bindings"} for workflow selection and keep get-tool-contract authoritative for exact field names
- This repository documents the MCP server surface; it does not ship a generic stdio helper client
- If you use an external MCP client wrapper, follow that wrapper's own parsing and transport guarantees
- Boolean parameters must be JSON booleans (true/false), not strings
- Entity tools work DB-first: schemas are created directly in PostgreSQL
- Guidance lookups use the persistent disk cache and hot reload only when its activation marker changes; network update checks happen through install-knowledge/update-knowledge, not every MCP session

## Return Values

    0       Server shut down normally
    1       Server failed to start

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#mcp-server)
