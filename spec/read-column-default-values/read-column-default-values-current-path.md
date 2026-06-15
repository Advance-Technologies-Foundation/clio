# Read Column Default Values — Current Read Path (FR-01 evidence)

**Feature**: read-column-default-values
**Story**: [story-read-column-default-values-1.md](../stories/story-read-column-default-values-1.md)
**FR**: FR-01 · **PRD AC**: AC-01
**Jira**: [ENG-91318](https://creatio.atlassian.net/browse/ENG-91318) (epic ENG-85256)
**Phase**: A — investigation (documents only)
**Verified against**: branch `claude/reverent-engelbart-a19b06` (base `master` @ `98e17db1`), clio 8.0.2.x line

> Scope: this document records **how clio reads a column's default value today**, end to end, with file/line references. It is the verified baseline for the FR-04 keep/adopt/hybrid comparison. No production code is changed by this story (SM-01 Phase A counter = empty code diff).

---

## 1. Executive summary

- Clio reads default values through Creatio's **design-time** service `EntitySchemaDesignerService.svc` (method `GetSchemaDesignItem`), the **same backend the platform Entity Designer UI uses** — **not** through OData and **not** through `$metadata`.
- The transport is always `IApplicationClient` (HTTP POST); there is no raw `HttpClient` anywhere on this path.
- The server returns a per-column `defValue` object (`EntitySchemaColumnDefValueDto`); clio maps it into the structured `default-value-config` (`EntitySchemaDefaultValueConfig`) plus the legacy flat `default-value-source` / `default-value` fields.
- For a **lookup-column `Const` default** (the ticket case) the readback returns the **raw GUID only** — no referenced-record display value, no existence check, and the write-side validation does not guard lookup columns at all.

---

## 2. End-to-end chain

| # | Layer | Symbol | File:line |
|---|-------|--------|-----------|
| 1 | CLI verb / MCP-backed options | `get-entity-schema-column-properties` → `GetEntitySchemaColumnPropertiesOptions` | [GetEntitySchemaColumnPropertiesCommand.cs:11-35](../../clio/Command/GetEntitySchemaColumnPropertiesCommand.cs) |
| 2 | Command | `GetEntitySchemaColumnPropertiesCommand.Execute` → `GetColumnProperties` | [GetEntitySchemaColumnPropertiesCommand.cs:40-64](../../clio/Command/GetEntitySchemaColumnPropertiesCommand.cs) |
| 3 | Service | `IRemoteEntitySchemaColumnManager.GetColumnProperties` | [RemoteEntitySchemaColumnManager.cs:158-191](../../clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs) |
| 4 | Schema load | `LoadSchema` → `IRemoteEntitySchemaDesignerClient.GetSchemaDesignItem` | [RemoteEntitySchemaColumnManager.cs:785-800](../../clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs) |
| 5 | HTTP client | `RemoteEntitySchemaDesignerClient.GetSchemaDesignItem` → POST | [RemoteEntitySchemaDesignerClient.cs:92-96](../../clio/Command/EntitySchemaDesigner/RemoteEntitySchemaDesignerClient.cs) |
| 6 | Transport | `PostToUrl` → `IApplicationClient.ExecutePostRequest` | [RemoteEntitySchemaDesignerClient.cs:194-202](../../clio/Command/EntitySchemaDesigner/RemoteEntitySchemaDesignerClient.cs) |
| 7 | Mapping | `EntitySchemaDesignerSupport.CreateDefaultValueConfig` | [EntitySchemaDesignerSupport.cs:503-536](../../clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs) |
| 8 | Read model | `EntitySchemaColumnPropertiesInfo` (incl. `default-value-config`) | [EntitySchemaReadModels.cs:65-89](../../clio/Command/EntitySchemaDesigner/EntitySchemaReadModels.cs) |

### 2.1 HTTP endpoint (the answer to "do we use OData?")

The designer service path is a constant in the client:

```csharp
// RemoteEntitySchemaDesignerClient.cs:44
private const string DesignerServicePath = "ServiceModel/EntitySchemaDesignerService.svc";
```

`GetSchemaDesignItem` posts to `{baseUrl}/GetSchemaDesignItem`, where the base URL is built by
`IServiceUrlBuilder.Build(DesignerServicePath)` and the method name is appended in
`BuildDesignerMethodUrl` ([RemoteEntitySchemaDesignerClient.cs:252-255](../../clio/Command/EntitySchemaDesigner/RemoteEntitySchemaDesignerClient.cs)).
`ServiceUrlBuilder.Build` auto-prepends `0/` on .NET Framework instances, so the effective URL is
`…/0/ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem` (Framework) or
`…/ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem` (.NET Core).

**No OData and no `$metadata` is involved in default-value readback.** Clio's OData tools
(`odata-read/create/update/delete`) target data endpoints `odata/{EntitySet}` only and never read
schema metadata — confirmed separately in the FR-02 probe doc.

### 2.2 Transport is `IApplicationClient` (hard rule satisfied)

Every designer call goes through the injected `IApplicationClient`:

```csharp
// RemoteEntitySchemaDesignerClient.cs:198-199
string requestBody = request == null ? "{}" : _jsonConverter.SerializeObject(request);
string rawResponse = _applicationClient.ExecutePostRequest(url, requestBody, timeoutMs, retryCount, retryDelay);
```

The client is constructor-injected ([RemoteEntitySchemaDesignerClient.cs:51-56](../../clio/Command/EntitySchemaDesigner/RemoteEntitySchemaDesignerClient.cs)); there is no `new HttpClient(...)` on this path.

---

## 3. DTO → `default-value-config` mapping

### 3.1 Server DTO: `EntitySchemaColumnDefValueDto`

The per-column `defValue` object deserialized from the `GetSchemaDesignItem` response
([EntitySchemaDesignerDtos.cs:159-175](../../clio/Command/EntitySchemaDesigner/EntitySchemaDesignerDtos.cs);
the column carries it at [EntitySchemaDesignerDtos.cs:320-321](../../clio/Command/EntitySchemaDesigner/EntitySchemaDesignerDtos.cs)):

| JSON field | CLR property | Type | Meaning |
|------------|--------------|------|---------|
| `valueSourceType` | `ValueSourceType` | `EntitySchemaColumnDefSource` enum | Which kind of default (None/Const/Settings/SystemValue/Sequence) |
| `value` | `Value` | `object` | Const scalar payload (for a lookup `Const` default → the **record GUID**) |
| `valueSource` | `ValueSource` | `string` | Selector for Settings (setting code) / SystemValue (GUID) |
| `sequencePrefix` | `SequencePrefix` | `string` | Sequence prefix |
| `sequenceNumberOfChars` | `SequenceNumberOfChars` | `int` | Sequence width |

### 3.2 Mapping function: `CreateDefaultValueConfig`

[EntitySchemaDesignerSupport.cs:503-536](../../clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs) switches on
`defValue.ValueSourceType`:

| Source | Fields populated in `EntitySchemaDefaultValueConfig` |
|--------|------------------------------------------------------|
| `Const` | `Source`, `Value` (normalized scalar — the raw GUID for a lookup) |
| `Settings` | `Source`, `ValueSource`, `ResolvedValueSource` (both = `defValue.ValueSource`) |
| `SystemValue` | `Source`, `ValueSource`, `ResolvedValueSource` (both = `defValue.ValueSource`) |
| `Sequence` | `Source`, `SequencePrefix`, `SequenceNumberOfChars` (`>0` → value, else null) |
| `None` / default | `Source` only |

The structured config is the consumer-facing shape ([EntitySchemaDefaultValueConfig.cs:9-52](../../clio/Command/EntitySchemaDesigner/EntitySchemaDefaultValueConfig.cs)), all kebab-case JSON names: `source`, `value`, `value-source`, `resolved-value-source`, `sequence-prefix`, `sequence-number-of-chars`.

### 3.3 The legacy flat fields are derived from the same DTO

`GetColumnProperties` also fills the legacy flat readback fields
([RemoteEntitySchemaColumnManager.cs:178-179](../../clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs)):

- `default-value-source` ← `GetFriendlyDefaultValueSource(column.DefValue)`
- `default-value` ← `GetFriendlyDefaultValue(column.DefValue)`

For `Const`, `GetFriendlyDefaultValue` returns `config.Value?.ToString()`
([EntitySchemaDesignerSupport.cs:538-553](../../clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs)) — i.e. the GUID string verbatim.

The full read model `EntitySchemaColumnPropertiesInfo` carries **both** the legacy flat fields and the structured
`default-value-config` ([EntitySchemaReadModels.cs:77-89](../../clio/Command/EntitySchemaDesigner/EntitySchemaReadModels.cs)).

---

## 4. Lookup-`Const` weak spot (AC-03)

The ticket case — a lookup column whose default is a record of a previously created lookup — exposes three gaps,
all on the **current** path:

1. **Readback returns the raw GUID only, no display value.**
   `CreateDefaultValueConfig` for `Const` sets `Value = NormalizeScalarDefaultValue(defValue.Value, …)`
   ([EntitySchemaDesignerSupport.cs:510-513](../../clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs)) — a scalar passthrough.
   There is no second call to resolve the referenced record's display column, so an agent reading the column
   cannot tell *which* lookup record the GUID points to without issuing its own follow-up query. The
   `EntitySchemaDefaultValueConfig` shape has **no** `display-value` or `record-resolution` field today
   ([EntitySchemaDefaultValueConfig.cs:9-52](../../clio/Command/EntitySchemaDesigner/EntitySchemaDefaultValueConfig.cs)).

2. **No existence validation of the GUID on write.**
   The write-side resolver `EntitySchemaDefaultValueSourceResolver.Resolve` only resolves `SystemValue` and
   `Settings`; for `Const` it returns the config unchanged
   ([EntitySchemaDefaultValueSourceResolver.cs:45-57](../../clio/Command/EntitySchemaDesigner/EntitySchemaDefaultValueSourceResolver.cs)):

   ```csharp
   return source switch {
       EntitySchemaColumnDefSource.SystemValue => ResolveSystemValue(config, dataValueType, context, options),
       EntitySchemaColumnDefSource.Settings    => ResolveSettings(config, dataValueType, context, options),
       _ => config            // Const (and others) pass through verbatim — GUID never validated
   };
   ```

   So a `Const` lookup default accepts any GUID, including one that does not exist in the referenced lookup table.

3. **`ValidateDefaultValueConfig` blocks `Const` only for binary-like types.**
   [EntitySchemaDesignerSupport.cs:592-622](../../clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs):
   the only `Const` rejection is when the column is a binary-like data value type
   ([line 601-605](../../clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs)).
   Lookup columns are **not** binary-like, so a lookup `Const` default passes validation unconditionally
   (then `CreateDefaultValueDto` requires only that `value` is present, [line 561-566](../../clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs)).

**Net:** the current path can *store and return* a lookup `Const` default, but the readback is GUID-only and the
write path does no referential validation. This is exactly the gap the FR-04 decision must weigh and that Phase B
DRAFT-AC-05/DRAFT-AC-06 would close (display-value enrichment + Const-GUID existence validation).

---

## 5. Prior art

This path is the **read** half of the structured default-value contract shipped in clio **8.0.2.47** (write path:
`create-entity-schema` / `update-entity-schema` / `modify-entity-schema-column`). See
[entity-schema-default-values-plan.md](../entity-schema-default-values/entity-schema-default-values-plan.md) for the
original design. This feature (ENG-91318) extends the **read** half with referenced-record context, conditional on the
FR-04 decision.

---

## 6. AC traceability

| AC | Where satisfied in this doc |
|----|-----------------------------|
| AC-01 (endpoint + DTO→config mapping with refs) | §2.1, §3 |
| AC-02 (full chain + DefValue fields + sources) | §2 table, §3.1, §3.2 |
| AC-03 (lookup-`Const` weak spot recorded) | §4 |
| AC-ERR (empty code diff) | This story changes only files under `spec/` — verified by `git diff --name-only` showing `spec/**` only |
