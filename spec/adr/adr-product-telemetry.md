# ADR: clio product-telemetry client (`send-telemetry` / `get-telemetry-consent`)

- **Status:** Accepted
- **Date:** 2026-06-15
- **Feature:** `product-telemetry`
- **Jira:** [ENG-89424](https://creatio.atlassian.net/browse/ENG-89424) — Product Metrics for AI-Driven App Creation Agent
- **Related:** [ISEC-9898](https://creatio.atlassian.net/browse/ISEC-9898) (security review); CAADT-side
  contract + ingestion ADR in `creatio-ai-app-development-toolkit`
  (`context/product-telemetry.md`, `.architecture/adrs/ADR-caadt-product-telemetry-ingestion.md`).

---

## Context

clio's MCP server exposes two product-telemetry tools used by the CAADT app-creation skill:
`get-telemetry-consent` (read-only consent check) and `send-telemetry` (store one workflow
event). Events are persisted locally as OpenTelemetry-log-shaped JSON, and a background flusher
uploads them OTLP/HTTP to a collector when one is configured and consent is granted. Per
ENG-89424, local-file collection is the agreed interim mode until the remote endpoint is live.

This ADR records the clio **client-side** decisions. The server-side ingestion (edge OTel
collector → ClickHouse) is owned by the CAADT ingestion ADR.

## Decisions

### 1. Event allow-list is a single source of truth in clio
`TelemetryService.AllowedEventNames` (14 ordered names) is the authority. clio rejects any
other `event_name` (`unknown-event-name`). The `get-tool-contract` `event_name` description is
**generated from** this list, and a unit test asserts they match, so what clio announces can
never drift from what it enforces. CAADT's copy (`product-telemetry.md`) is a downstream mirror;
clio—not CAADT—is the enforcer, and `send-telemetry` is a general clio MCP tool usable by any
agent, so clio announces the list rather than relying on CAADT alone.

### 2. Storage shape is an internal OTel-log JSON; the wire shape is OTLP/HTTP — deliberate two-model split
On disk, events use a compact snake_case OTel-log shape (`time_unix_nano`, `severity_text`,
`string_value`, numeric `int_value`). At flush time they are mapped to spec-valid OTLP/HTTP JSON
(`OtlpModels`: camelCase, int64-as-string, `resourceLogs/scopeLogs/logRecords`, `service.name`,
`severityNumber`). This is a deliberate storage-vs-wire split, **not** a single end-to-end model;
the mapping is generic (every attribute forwarded) and locked by a round-trip test. (The CAADT
ingestion ADR's "one data model end-to-end" wording is corrected to reflect this client mapping.)
Collapsing to one model (store OTLP JSON verbatim) was considered and rejected for v1: the
internal shape keeps the on-disk store small and decoupled from OTLP JSON quirks, at the cost of
one translation hop.

### 3. Storage location under clio's consolidated home
The spool lives at `<clio-home>/telemetry` via `ClioRuntimePaths.Home` (honoring `CLIO_HOME`),
with `CLIO_TELEMETRY_HOME` as an explicit override. This keeps telemetry inside the single clio
home so relocation and cleanup cover it.

### 4. Transport is HTTPS-only (loopback HTTP allowed for local testing)
`IsValidEndpoint` accepts `https`, or `http` only for a loopback host. The optional public ingest
key (`X-Ingest-Key`) therefore never traverses the network in cleartext to a remote host. No
default endpoint ships; absent configuration, events are spooled and pruned, never sent.

### 5. Loss-tolerant delivery
Transient failures (5xx/408/429) retry with bounded exponential backoff + jitter, then keep the
spool for the next trigger. Non-transient 4xx are treated as permanent and the batch is dropped
(telemetry is loss-tolerant by classification) — but logged at **Warning** with a response-body
snippet so a wire/schema regression that would silently zero out metrics is detectable. The spool
has age (30d) + size (500) caps; the `sessions/` directory and crash-orphaned `*.json.tmp` files
are pruned on every flush.

### 6. Telemetry must never disturb the caller
Both the store (`Send`) and the flusher swallow I/O failures and degrade to a soft result / log,
never throwing into the MCP tool call. Validation errors (caller-actionable) are still returned
as structured rejections.

### 7. Privacy / value-level guards
Only an allow-listed set of scalar attributes is stored (`schema_version`, `session_id`,
`event_name`, `event_timestamp`, `platform`, `clio_version`, `coding_agent`, anonymous random
`installation_id`, `skill_version`, `plugin_version`, `event_id`, optional `duration_ms` and
`duration_since_session_start_ms`). Unknown fields are rejected; agent-supplied free strings are
length-bounded and `session_id` is shape-checked, as defense in depth against oversized or
PII-shaped values. No prompts, secrets, tokens, customer data, or generated content are collected.

### 8. Scope narrowing vs the ENG-89424 acceptance criteria
The AC listed `result`, `entry_point`, `model`, and `model_reasoning` as "where applicable"
fields. v1 **descopes** these on both clio and CAADT: event outcome is encoded by the event name
(`implementation_completed` vs `implementation_failed`), and `entry_point`/`model`/
`model_reasoning` are deferred until there is a confirmed product question that needs them. The
required identity/timing/agent/version fields and the anonymized installation id are collected.

## Consequences

- Adding or renaming an event requires editing `AllowedEventNames` (clio enforces + announces) and
  the CAADT contract; the clio sync test guards the clio half.
- The edge-collector attribute allow-list must accept the stored attribute set in decision 7.
- The two-model split (decision 2) means a new typed field must be carried in both shapes; the
  generic forwarder + round-trip test mitigate silent drops.
