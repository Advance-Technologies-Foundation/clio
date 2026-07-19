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

External knowledge delivery is configured with environment variables:

- `CLIO_KNOWLEDGE_NUGET_SOURCE` — absolute HTTPS URL of a NuGet v3 service index (HTTP is accepted only for loopback development feeds)
- `CLIO_KNOWLEDGE_NUGET_PACKAGE_ID` — package ID to discover through the feed's flat container
- `CLIO_KNOWLEDGE_TRUSTED_KEY_ID` — trusted signing-key ID expected by the bundle manifest
- `CLIO_KNOWLEDGE_TRUSTED_PUBLIC_KEY_PATH` — absolute path to one ECDSA P-256 SubjectPublicKeyInfo PEM

The service-index URL must respond directly; redirects are not followed. Its advertised
`PackageBaseAddress/3.0.0` resource must use the same scheme, host, and port as the configured
service-index URL.

Clio selects the highest stable three-part package version, extracts
`content/knowledge-bundle.zip`, and verifies its signature, compatibility, complete stable resource
catalog, and resource digests before atomically installing it under the `knowledge-root-path` stored
in Clio's visible `appsettings.json`. When the setting is absent, Clio creates and persists
`<clio-home>/knowledge`. The installed archive and extracted content remain available to users and
coding agents on disk.

Externally delivered ESQ guidance reads only the persisted cache during lookup, and MCP never
contacts NuGet. Use `install-knowledge`, `update-knowledge`, `info-knowledge`, and
`delete-knowledge` to manage the cache explicitly. An already-running MCP process compares the
small activation marker on every external knowledge lookup, so a successful `update-knowledge`
becomes visible without restarting MCP. A rejected update leaves the last-known-good bundle active.
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
