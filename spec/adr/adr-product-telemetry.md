# ADR: clio product-telemetry client (`send-telemetry` / `get-telemetry-consent` / `withdraw-telemetry-consent`)

- **Status:** Accepted
- **Date:** 2026-06-15
- **Feature:** `product-telemetry`
- **Jira:** [ENG-89424](https://creatio.atlassian.net/browse/ENG-89424) — Product Metrics for AI-Driven App Creation Agent
- **Related:** [ISEC-9898](https://creatio.atlassian.net/browse/ISEC-9898) (security review); CAADT-side
  contract in `creatio-ai-app-development-toolkit` (`context/product-telemetry.md`).

---

## Context

clio's MCP server exposes three product-telemetry tools used by the CAADT app-creation skill:
`get-telemetry-consent` (read-only consent check), `send-telemetry` (store one workflow event),
and `withdraw-telemetry-consent` (set the stored decision to denied and purge the not-yet-uploaded
local outbox; see decision 10). Events are persisted locally as OpenTelemetry-log-shaped JSON, and a background flusher
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
key (`X-Ingest-Key`) therefore never traverses the network in cleartext to a remote host.

The endpoint-default mechanism ships as the **lowest-precedence** source in the binary:
`CLIO_TELEMETRY_ENDPOINT` and the settings `telemetry.endpoint` override it, and a
configured-but-invalid endpoint disables uploading rather than silently falling back to the default.
The production collector is identified (`TelemetryFlushOptionsProvider.ProductionEndpoint`), but
because it is not live yet the **active default (`DefaultEndpoint`) ships empty**: a freshly
installed or in-place-updated clio therefore sends nothing anywhere until an endpoint is explicitly
configured (e.g. a developer pointing `CLIO_TELEMETRY_ENDPOINT` at the stage collector). When the
collector is provisioned, flipping `DefaultEndpoint` to `ProductionEndpoint` (a one-line change plus
the pin test) turns every install on without per-machine configuration — the binary is the only
delivery vehicle that reaches existing installs on update (clio neither ships `appsettings.json` nor
creates it with a telemetry default). This supersedes the original "no default endpoint ships"
decision: the mechanism is in place; only the production value is deferred until the endpoint is live.

Uploading is **opt-out** at the operator level: `CLIO_TELEMETRY_ENABLED=false` (environment, wins)
or `telemetry.enabled: false` (settings) resolves the endpoint to none and hard-disables uploads
regardless of the default and of granted consent — for centrally managed fleets that disallow
product telemetry. The environment variable wins over the settings flag, mirroring the endpoint
precedence. End-user opt-out remains the consent gate: nothing is uploaded unless consent is
`granted`.

### 5. Loss-tolerant delivery
Transient failures (5xx/408/429) retry with bounded exponential backoff + jitter, then keep the
spool for the next trigger. Non-transient 4xx are treated as permanent and the batch is dropped
(telemetry is loss-tolerant by classification) — but logged at **Warning** with a response-body
snippet so a wire/schema regression that would silently zero out metrics is detectable. The spool
has age (30d) + size (500) caps; the `sessions/` directory and crash-orphaned `events/*.json.tmp`
files are pruned on every flush.

Delivery is best-effort and **at-least-once-leaning**, not exactly-once. A given event can reach
the collector more than once: the single-flight guard is per-process, so two concurrent clio
processes can flush the same spooled batch, and a retry after a lost ACK re-sends an
already-delivered batch. Duplicates are acceptable by classification and are de-duplicated at query
time on `event_id` (a fresh GUID emitted on every event); downstream SQL/Grafana consumers must
group or dedup on `event_id`.

### 6. Telemetry must never disturb the caller
Both the store (`Send`) and the flusher swallow I/O failures and degrade to a soft result / log,
never throwing into the MCP tool call. Validation errors (caller-actionable) are still returned
as structured rejections.

The result `status` describes the **contract outcome, not the mechanism**, so the
buffering/sending strategy can change without altering the contract: `recorded` (clio accepted the
event and the caller is done — any upload to a collector is separate and not confirmed by the
call), `consent-denied`, `record-failed` (clio could not record it — an I/O fault, not the
caller's fault), and `rejected` (validation failure the caller can fix). Notably it does **not**
say `stored`/`spooled`/`sent`, none of which would survive a change of strategy or could be
misread as confirmed delivery.

### 7. Privacy / value-level guards
The event name is carried in the dedicated OTLP `event_name` field (decision 9), not as an
attribute. Beyond it, only an allow-listed set of scalar attributes is stored (`schema_version`,
`session_id`, `event_timestamp`, `platform`, `clio_version`, `coding_agent`, anonymous random
`installation_id`, `plugin_version`, `event_id`, optional `duration_ms` and
`duration_since_session_start_ms`). Unknown fields are rejected; agent-supplied free strings are
length-bounded and `session_id` is shape-checked, as defense in depth against oversized or
PII-shaped values. No prompts, secrets, tokens, customer data, or generated content are collected.

### 8. Scope narrowing vs the ENG-89424 acceptance criteria
The AC listed `result`, `entry_point`, `model`, and `model_reasoning` as "where applicable"
fields. v1 **descopes** these on both clio and CAADT: event outcome is encoded by the event name
(`implementation_completed` vs `implementation_failed`), and `entry_point`/`model`/
`model_reasoning` are deferred until there is a confirmed product question that needs them. The
required identity/timing/agent/version fields and the anonymized installation id are collected.

### 9. Event name is a single source of truth on the dedicated OTLP field
The event name is carried exactly once end-to-end. clio stores it in the dedicated `event_name`
field of the local OTel-log JSON (`OpenTelemetryLogEvent.EventName`) and emits
it only on the OTLP LogRecord `event_name` field (proto field 12) — never duplicated into the log
body or an `event_name` attribute. This replaces the earlier shape that sent the name in three
places (body, attribute, and dedicated field). The edge collector validates it via OTTL
`log.event_name`, clears the client-controlled body (`set(log.body, "")`, preserving the
threat-I-01 body cap), and the ClickHouse exporter maps the dedicated field to the typed
`EventName` column; queries read `EventName`, not `Body` or `LogAttributes['event_name']`. This is
a deliberate producer-and-collector change owned jointly with the CAADT ingestion stack
(`metrics-installation/helm/caadt-telemetry`): the two ends must move together because the collector
filter keys on the dedicated field.

### 10. Consent withdrawal (`withdraw-telemetry-consent`)
`withdraw-telemetry-consent` is the user-facing opt-out that satisfies the data-subject right to
withdraw consent as easily as it was given (GDPR Art. 7(3)), flagged as an open item in the CAADT
threat analysis. It sets the locally stored decision to `denied` and purges the not-yet-uploaded
outbox (`events/` + `sessions/`), keeping the anonymous `installation_id`. Withdrawal is
forward-looking, not retroactive: it stops further collection and upload but does **not** delete
events already uploaded to Creatio (those expire on the server-side 1-year TTL — erasure, GDPR
Art. 17, is a separate server-side concern, out of scope here). The consent flip is written first so
the flusher's per-run consent re-check (decision 6) blocks any upload even if the purge is
interrupted; the purge is best-effort (a momentarily locked file is skipped, not fatal). A withdrawn
(`denied`) decision is terminal through the MCP tools, exactly like a first-run denial — re-granting
is not offered. `send-telemetry`'s first-decision-wins guard is unchanged; `WithdrawConsent` is the
one path that transitions an existing `granted` decision to `denied`.

## Consequences

- Adding or renaming an event requires editing `AllowedEventNames` (clio enforces + announces) and
  the CAADT contract; the clio sync test guards the clio half.
- The edge-collector attribute allow-list must accept the stored attribute set in decision 7, and
  its event-name filter must key on the dedicated OTLP `event_name` field (decision 9), not an
  attribute.
- The two-model split (decision 2) means a new typed field must be carried in both shapes; the
  generic forwarder + round-trip test mitigate silent drops.
