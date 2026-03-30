# Entity Schema Default Values — Implementation Plan

## 1. Goal

Enable `clio` MCP entity tools to set schema-side default values for entity columns with the same semantic model that the Creatio Entity Schema Designer already uses.

This change must support entity-only defaults and must not rely on page handlers, page sync, or any other UI-side workaround.

## 2. Problem Statement

The current `clio` MCP entity mutation surface exposes only:

- `default-value-source: Const|None`
- `default-value: string`

That is narrower than the actual platform contract used by the Entity Schema Designer and backend service, which already support:

- `None`
- `Const`
- `Settings`
- `SystemValue`
- `Sequence`

Because of that mismatch:

- MCP callers cannot express schema defaults such as `CurrentDateTime` or system-setting-backed defaults
- `Sequence` defaults cannot be configured through the MCP entity tools
- constant defaults are lossy because MCP treats them as strings instead of typed JSON values
- readback is incomplete because non-constant defaults are flattened into `default-value-source` and `default-value`

## 3. Ground Truth From Product Code

### Frontend

The Entity Schema Designer writes default values into column metadata as `defValue`, not into any page-level configuration.

Key evidence:

- `feature/entity-schema-designer/.../base-entity-column-editor.service.ts`
  - `Default value` editor is bound to `name: 'defValue'`
- `feature/entity-schema-designer/.../entity-column-default-value-dialog.component.ts`
  - supports `None`, `Const`, `Settings`, `SystemValue`, `Sequence`
  - uses `GetSystemValues` for system variables
  - stores system setting code in `valueSource`
- `data-access/entity-schema-designer-api/.../base-entity-schema-column.ts`
  - column metadata contains `defValue`
- `data-access/entity-schema-designer-api/.../entity-schema-designer-api.service.ts`
  - save path sends the schema DTO through `SaveSchema`

### Backend

The backend contract already supports the full default-value model.

Key evidence:

- `Terrasoft.Core.ServiceModelContract/Designers/EntitySchemaColumnDto.cs`
  - `defValue` contains `valueSourceType`, `value`, `valueSource`, `sequencePrefix`, `sequenceNumberOfChars`
- `Terrasoft.Core.ServiceModel/Designers/Mappers/DtoToSchema/EntitySchemaDesignSchemaDtoToDesignItemMapper.cs`
  - maps `Const` from `Value`
  - maps non-const defaults from `ValueSource`
  - maps sequence fields
- `Terrasoft.Core/Entities/EntitySchemaColumnDef.cs`
  - runtime enum already supports `None`, `Const`, `Settings`, `SystemValue`, `Sequence`
  - system values include `CurrentDate`, `CurrentDateTime`, `CurrentUser`, `CurrentUserContact`, `CurrentUserAccount`, `GenerateUId`, `GenerateSequentialUId`
- `Terrasoft.Core/Entities/EntityColumnValue.cs`
  - runtime uses `Column.DefValue.Value` when applying entity defaults

Conclusion: the correct implementation path in `clio` is to expose the existing entity-schema default-value contract, not to invent a UI-side fallback.

## 4. Current `clio` Gap Analysis

### What is already present

- Internal DTO already contains the full backend shape:
  - `EntitySchemaColumnDefValueDto`
  - `ValueSourceType`
  - `Value`
  - `ValueSource`
  - `SequencePrefix`
  - `SequenceNumberOfChars`
- `application-get-info` already understands source names beyond `Const|None`
- non-constant readback in `ApplicationInfoService` already falls back to `ValueSource`

### What is currently missing

- MCP input models only advertise `default-value-source` and `default-value`
- accepted source values are limited to `Const` and `None`
- constant default input is typed as `string`, which is wrong for `Boolean`, `Integer`, and `DateTime`
- write path fills only `DefValue.Value`
- write path never fills `DefValue.ValueSource`, `SequencePrefix`, or `SequenceNumberOfChars`
- `get-entity-schema-column-properties` still flattens readback to string-style fields and loses structured non-const detail
- prompts, docs, and `tool-contract-get` still describe the old reduced contract

## 5. Proposed MCP Contract

Add a new structured field to the entity mutation surface:

```json
"default-value-config": {
  "source": "None|Const|Settings|SystemValue|Sequence",
  "value": true,
  "value-source": "CurrentDateTime",
  "sequence-prefix": "TSK-",
  "sequence-number-of-chars": 6
}
```

Rules:

- `source=None`
  - clears the stored schema default
- `source=Const`
  - uses `value`
  - `value` must be a typed JSON scalar, not forced to string
- `source=Settings`
  - uses `value-source`
  - `value-source` is `SysSettings.Code`
- `source=SystemValue`
  - uses `value-source`
  - `value-source` is a canonical system value name such as `CurrentDateTime`
- `source=Sequence`
  - uses `sequence-prefix` and `sequence-number-of-chars`

Recommended examples:

```json
{ "default-value-config": { "source": "Const", "value": true } }
{ "default-value-config": { "source": "Const", "value": 5 } }
{ "default-value-config": { "source": "SystemValue", "value-source": "CurrentDateTime" } }
{ "default-value-config": { "source": "Settings", "value-source": "UsrDefaultPriorityCode" } }
{ "default-value-config": { "source": "Sequence", "sequence-prefix": "TSK-", "sequence-number-of-chars": 6 } }
{ "default-value-config": { "source": "None" } }
```

## 6. Backward Compatibility Strategy

Keep the existing shorthand fields:

- `default-value-source`
- `default-value`

Compatibility behavior:

- shorthand remains supported only for `Const` and `None`
- shorthand continues to map to the old path for existing callers
- if both shorthand and `default-value-config` are passed, reject the request as ambiguous
- all new docs and MCP prompt guidance should prefer `default-value-config`

## 7. Detailed Implementation Plan

### Phase 1: Shared MCP Contract Model

Add a reusable record for structured default values under the MCP entity tool layer.

Target files:

- `clio/Command/McpServer/Tools/EntitySchemaTool.cs`

Changes:

- add `DefaultValueConfigArgs` record
- add `default-value-config` to:
  - `CreateEntitySchemaColumnArgs`
  - `ColumnModificationArgsBase`
- keep legacy shorthand fields for compatibility

Expected outcome:

- one canonical model feeds `create-entity-schema`, `modify-entity-schema-column`, `update-entity-schema`, and `schema-sync`

### Phase 2: Serialization and Tool Surface

Update MCP serialization so the new object is preserved end-to-end.

Target files:

- `clio/Command/McpServer/Tools/EntitySchemaTool.cs`
- `clio/Command/McpServer/Tools/SchemaSyncTool.cs`

Changes:

- serialize `default-value-config` in create-column payloads
- serialize `default-value-config` in update operations
- ensure `schema-sync` inherits the richer contract because it already reuses these args

Expected outcome:

- all MCP entity mutation tools accept the same structured default-value payload

### Phase 3: Parse and Validation Layer

Expand validation beyond `Const|None`.

Target files:

- `clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs`
- `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaCreator.cs`
- `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs`

Changes:

- extend default-source parsing to:
  - `None`
  - `Const`
  - `Settings`
  - `SystemValue`
  - `Sequence`
- add validation rules:
  - `Const` requires `value`
  - `Settings` requires `value-source`
  - `SystemValue` requires `value-source`
  - `Sequence` requires valid numeric length and must be limited to text-like columns
  - `Binary`, `Image`, `File` must reject constant defaults
  - `default-value-config` and shorthand fields cannot be mixed
- prefer canonical system value names instead of GUIDs for MCP callers

Expected outcome:

- the command layer validates the same semantics that the product designer supports

### Phase 4: DTO Mapping to Backend Contract

Populate the full backend DTO instead of only `Value`.

Target files:

- `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaCreator.cs`
- `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs`

Changes:

- `Const`
  - set `DefValue.ValueSourceType`
  - set `DefValue.Value`
- `Settings`
  - set `DefValue.ValueSourceType`
  - set `DefValue.ValueSource`
- `SystemValue`
  - set `DefValue.ValueSourceType`
  - set `DefValue.ValueSource`
- `Sequence`
  - set `DefValue.ValueSourceType`
  - set `DefValue.SequencePrefix`
  - set `DefValue.SequenceNumberOfChars`
- `None`
  - clear `DefValue`

Expected outcome:

- `clio` writes exactly the same backend contract that frontend designer save uses

### Phase 5: Read Models and Verification Surface

Expose structured defaults on readback so callers can verify what was saved.

Target files:

- `clio/Command/EntitySchemaDesigner/EntitySchemaReadModels.cs`
- `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs`
- `clio/Command/ApplicationInfoService.cs`
- `clio/Command/McpServer/Tools/EntitySchemaTool.cs`

Changes:

- add a structured `default-value-config` response object
- preserve old summary fields:
  - `default-value-source`
  - `default-value`
- for structured readback:
  - `Const` returns typed `value`
  - `Settings` returns `value-source`
  - `SystemValue` returns `value-source`
  - `Sequence` returns prefix and length
- stop flattening non-const defaults into incomplete string summaries only

Expected outcome:

- callers can do reliable machine verification after mutation

### Phase 6: Tool Contract, Prompts, and Docs

Align all advertised MCP and CLI-facing descriptions.

Target files:

- `clio/Command/McpServer/Tools/ToolContractGetTool.cs`
- `clio/Command/McpServer/Prompts/EntitySchemaPrompt.cs`
- `clio/Commands.md`
- `clio/docs/commands/create-entity-schema.md`
- `clio/docs/commands/modify-entity-schema-column.md`
- `clio/docs/commands/mcp-server.md`
- `clio/help/en/create-entity-schema.txt`
- `clio/help/en/modify-entity-schema-column.txt`

Changes:

- document `default-value-config` as the preferred contract
- mark shorthand fields as legacy compatibility input
- update examples for:
  - boolean constant
  - `CurrentDateTime`
  - system setting code
  - sequence

Expected outcome:

- MCP guidance matches real behavior and new capability is discoverable through `tool-contract-get`

## 8. Test Plan

### Unit tests

Add or update tests for:

- parsing `default-value-config.source`
- validation failure when config and shorthand are both provided
- constant typed values:
  - boolean
  - integer
  - text
- `SystemValue=CurrentDateTime`
- `Settings` with `value-source`
- `Sequence` with prefix and length
- `None` clears saved default
- readback returns structured default config

Target areas:

- `clio.tests/Command/*`
- `clio.tests/Command/McpServer/*`

### MCP E2E tests

Add real server coverage for:

- `modify-entity-schema-column` with `SystemValue`
- `modify-entity-schema-column` with `Sequence`
- `update-entity-schema` structured default-value-config path
- `schema-sync` update operation using the same structure
- `tool-contract-get` advertising the new field

Target area:

- `clio.mcp.e2e/*`

## 9. Recommended Delivery Order

1. Add shared MCP contract model
2. Update parsing and validation
3. Update DTO mapping
4. Update read models
5. Add unit tests
6. Add MCP E2E tests
7. Update docs, prompts, and `tool-contract-get`

## 10. Risks

### Risk 1: Typed constant compatibility

Current MCP surface treats defaults as strings in several places. Moving to typed JSON values may affect existing serializer assumptions.

Mitigation:

- keep shorthand path untouched for legacy callers
- limit typed values to the new `default-value-config`

### Risk 2: Readback mismatch

Different read surfaces currently summarize defaults differently.

Mitigation:

- define one reusable structured response model and use it in both entity-column readback and application readback

### Risk 3: System value naming ambiguity

Backend can ultimately work with system value GUIDs, but MCP callers should not need to know them.

Mitigation:

- accept canonical names such as `CurrentDateTime`
- document the supported names explicitly

## 11. Acceptance Criteria

- MCP entity mutation tools can set schema defaults using `default-value-config`
- supported sources are `None`, `Const`, `Settings`, `SystemValue`, `Sequence`
- `SystemValue=CurrentDateTime` can be saved without page-level changes
- `Sequence` can be configured through entity mutation tools
- constant defaults preserve typed JSON values in MCP input
- readback exposes structured default-value metadata
- legacy shorthand input continues to work for `Const` and `None`
- prompts, docs, and `tool-contract-get` all advertise the updated contract
- unit and MCP E2E coverage exist for the new capability

## 12. Out of Scope

- page-level default values
- automatic migration of callers from shorthand to structured config
- introducing new backend semantics beyond what the Creatio Entity Schema Designer already supports
