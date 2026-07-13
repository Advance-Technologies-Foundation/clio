using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for entity schema MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for creating, reading, and modifying remote entity schemas")]
public static class EntitySchemaPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to create a remote entity schema through MCP.
	/// </summary>
	[McpServerPrompt(Name = CreateEntitySchemaTool.CreateEntitySchemaToolName),
		Description("Prompt to create a remote entity schema")]
	public static string CreateEntitySchema(
		[Required]
		[Description("Target package name")]
		string packageName,
		[Required]
		[Description("Entity schema name")]
		string schemaName,
		[Required]
		[Description("Entity schema title")]
		string title,
		[Required]
		[Description("Creatio environment name")]
		string environmentName,
		[Description("Optional parent schema name")]
		string parentSchemaName = null,
		[Description("Whether to create a replacement schema")]
		bool extendParent = false) =>
		$"""
		 Use clio mcp server `{CreateEntitySchemaTool.CreateEntitySchemaToolName}` to create entity schema
		 `{schemaName}` in package `{packageName}` for environment `{environmentName}` with title `{title}`.
		 Send schema captions only through `title-localizations`, for example
		 `title-localizations.en-US = <title>`. Do not send legacy scalar `title`.
		 Set `parent-schema-name` only when inheritance or replacement behavior was explicitly requested.
		 Set `extend-parent` to `true` only when the request is specifically for a replacement schema, and only
		 together with `parent-schema-name`.
		 Set `is-virtual` to `true` only when the entity must not have a physical database table; it defaults to `false`.
		 Include `columns` only when the request explicitly describes initial fields. `title-localizations` is
		 OPTIONAL for a column add; when omitted, `en-US` is auto-derived from a scalar title/caption or the
		 column name. Provide `title-localizations` for a proper caption; the `en-US` value must be English.
		 Supported column types include
		 `Binary`, `Image`, `ImageLookup`, `File`, `SecureText`, and `Email`. `Blob` can be used as an alias for
		 `Binary`, `ImageLink` for `ImageLookup`, `Encrypted` / `Password` can be used as aliases for `SecureText`,
		 and `EmailAddress` can be used as an alias for `Email`. For an image/photo field shown with the
		 `crt.ImageInput` component, use `ImageLookup` ("Image link"), NOT the binary `Image` type. `ImageLookup` references the `SysImage` schema automatically,
		 so do not pass `reference-schema-name` for it. For `Lookup` columns,
		 provide `reference-schema-name`. Current clio entity-schema tools are part of the canonical clio MCP
		 contract, so keep using `create-entity-schema` instead of frontend-only names like `entity.create`.
		 For broader app-modeling guardrails, call `{GuidanceGetTool.ToolName}` with `name` set to `app-modeling`.
		 When the caller needs richer metadata, each `columns` item can also include `required`,
		 `default-value-config`, legacy shorthand `default-value-source` / `default-value`, and frontend-style
		 type aliases such as `ShortText` or `Date`. Prefer `default-value-config` with `source` set to
		 `None`, `Const`, `Settings`, `SystemValue`, or `Sequence`. Keep legacy `default-value-source` and
		 `default-value` only for shorthand `Const` and `None`. Do not send `default-value` or
		 `default-value-source=Const` for `Binary`, `Image`, or `File` columns, and use
		 `default-value-config` source `Sequence` only for text columns. For `Settings`, `value-source`
		 accepts setting code, display name, or id and clio normalizes it to setting code before save.
		 For `SystemValue`, `value-source` accepts GUID, enum alias, or display caption and clio
		 normalizes it to GUID before save.
		 For a lookup column, a `Const` default is the GUID of a record in the referenced schema — obtain it
		 by inserting/reading that record first (e.g. `odata-create` returns the new record `id`), then set
		 `default-value-config` `source=Const`, `value=<that GUID>`. clio validates the record exists before
		 save and rejects an unknown GUID. On readback, `get-entity-schema-column-properties` enriches the
		 lookup `Const` default-value-config with `display-value` (and a `record-resolution` marker when it
		 cannot be resolved) so you can verify which record the default points to without a second query.
		 Current parent request: `{parentSchemaName ?? "<not provided>"}`. Current replacement request:
		 `{extendParent}`.
		 Detect the connected user's profile language ONCE per session via `get-user-culture` and reuse it for the schema and column captions; if it returns `success:false`, ASK the user which language to use — do NOT silently use the host locale or `en-US`. Override per call with `caption-culture`.
		 The detected culture is the LANGUAGE of the caption text, not just the map key: author each localization value in its own language and keep the mandatory `en-US` value in ENGLISH. clio rejects non-English text (e.g. Cyrillic) under `en-US`; put localized text under its own culture key such as `uk-UA`.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to create a remote lookup schema through MCP.
	/// </summary>
[McpServerPrompt(Name = CreateLookupTool.CreateLookupToolName),
		Description("Prompt to create a remote lookup schema")]
	public static string CreateLookup(
		[Required]
		[Description("Target package name")]
		string packageName,
		[Required]
		[Description("Lookup schema name")]
		string schemaName,
		[Required]
		[Description("Lookup schema title")]
		string title,
		[Required]
		[Description("Creatio environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `{CreateLookupTool.CreateLookupToolName}` to create lookup schema
		 `{schemaName}` in package `{packageName}` for environment `{environmentName}` with title `{title}`.
		 Send schema captions only through `title-localizations`, for example
		 `title-localizations.en-US = <title>`. Do not send legacy scalar `title`.
		 Use this tool when the caller explicitly requested a lookup entity. The tool always creates the schema
		 with parent `BaseLookup`, so do not pass parent override arguments. Successful execution also registers
		 the lookup in the standard `Lookups` section, so treat the tool result as failed when the schema exists
		 but the lookup is not available in `Lookups`. `BaseLookup` already provides `Name` and `Description`;
		 keep `Name` as the display field and do not add duplicate title-like columns just to mirror the lookup
		 caption. Include `columns` only when the request explicitly
		 describes initial fields. After creation, verify the result with
		 `{GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName}` as the canonical post-create read
		 path. For broader app-modeling guardrails, call `{GuidanceGetTool.ToolName}` with `name` set to `app-modeling`.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to apply batch schema mutations through MCP.
	/// </summary>
	[McpServerPrompt(Name = UpdateEntitySchemaTool.UpdateEntitySchemaToolName),
		Description("Prompt to apply batch entity schema mutations")]
	public static string UpdateEntitySchema(
		[Required]
		[Description("Target package name")]
		string packageName,
		[Required]
		[Description("Entity schema name")]
		string schemaName,
		[Required]
		[Description("Creatio environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `{UpdateEntitySchemaTool.UpdateEntitySchemaToolName}` to apply a batch of column
		 mutations to entity schema `{schemaName}` in package `{packageName}` on environment `{environmentName}`.
		 Pass `package-name`, `schema-name`, and `environment-name` exactly as provided. Encode all column
		 changes in the ordered `operations` array. Each operation uses clio-native fields such as `action`,
		 `column-name`, `type`, `title-localizations`, `description-localizations`,
		 `reference-schema-name`, and `default-value-config`; keep legacy `default-value-source` and
		 `default-value` only for shorthand `Const` and `None`. The `get-app-info` read-shape aliases are also
		 accepted so a column read from `get-app-info` can be sent back without translation: `name` for
		 `column-name`, `data-value-type` for `type`, `reference-schema` for `reference-schema-name`, and
		 `is-required` for `required`.
		 Do not send legacy scalar `description`, and do not translate the payload into frontend
		 `entity.update.operationsJson`.
		 `title-localizations` is OPTIONAL for an `add` operation; when omitted, `en-US` is auto-derived from a
		 scalar title/caption or the column name. Provide `title-localizations` for a proper caption; the `en-US`
		 value must be ENGLISH text — author each localization in its own language (non-English text under
		 `en-US`, e.g. Cyrillic, is rejected; use a key such as `uk-UA`). Supported types include
		 `Binary`, `Image`, `ImageLookup`, `File`, `SecureText`, and `Email`. `Blob` can be used as an alias for
		 `Binary`, `ImageLink` for `ImageLookup`, `Encrypted` / `Password` can be used as aliases for `SecureText`,
		 and `EmailAddress` can be used as an alias for `Email`. For image/photo fields bound to `crt.ImageInput`,
		 add an `ImageLookup` ("Image link") column instead of the binary `Image` type; `ImageLookup` references
		 `SysImage` automatically, so do not pass `reference-schema-name` for it. Prefer `default-value-config`
		 sources `None`, `Const`, `Settings`, `SystemValue`, or `Sequence`. Do not send `default-value` or
		 `default-value-source=Const` for `Binary`, `Image`, or `File` operations, and use
		 `default-value-config` source `Sequence` only for text columns. For `Settings`, `value-source`
		 accepts setting code, display name, or id and clio normalizes it to setting code before save.
		 For `SystemValue`, `value-source` accepts GUID, enum alias, or display caption and clio
		 normalizes it to GUID before save. For create + seed + update workflows,
		 prefer `sync-schemas`. Seed rows create data only; model default requirements separately as
		 `schema default` or `ui default`. For existing-app maintenance guidance, call
		 `{GuidanceGetTool.ToolName}` with `name` set to `existing-app-maintenance`.
		 Inspect current schema metadata with `get-entity-schema-properties` first. For one-column changes, prefer `modify-entity-schema-column`. `update-entity-schema` performs internal DataForge enrichment and returns an optional `dataforge` section — inspect `similar-tables` before proceeding with the batch. When adding a Lookup column (`type = Lookup`) via `modify-entity-schema-column` (which does not enrich internally) and the correct `reference-schema-name` is not certain, call `dataforge-find-tables` (Layer 3 pre-flight) first. For the full DataForge orchestration protocol, call `{GuidanceGetTool.ToolName}` with `name` set to `dataforge-orchestration`.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to read structured schema properties through MCP.
	/// </summary>
	[McpServerPrompt(Name = GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName),
		Description("Prompt to read structured remote entity schema properties")]
	public static string GetEntitySchemaProperties(
		[Required]
		[Description("Entity schema name")]
		string schemaName,
		[Required]
		[Description("Creatio environment name")]
		string environmentName,
		[Description("Optional target package name; omit for the merged all-packages view")]
		string packageName = null) =>
		$"""
		 Use clio mcp server `{GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName}` to read structured
		 properties for entity schema `{schemaName}` from environment `{environmentName}`. The result is a schema
		 summary object with a `virtual` flag and a nested `columns` list for machine-readable column inspection.
		 Pass `schema-name` and `environment-name` exactly as provided. Leave `package-name` empty to get the
		 MERGED/EFFECTIVE schema with columns from ALL packages (this is what you want for column discovery,
		 because custom columns are frequently added in a package other than the one that defines the schema).
		 Set `package-name` only when you intentionally want to inspect a single package layer's slice.
		 IMPORTANT: an empty `columns` list (or `own-column-count: 0`) from a single-package read does NOT prove a
		 column is absent. Before concluding a field is missing, re-read without `package-name`, or use
		 `{FindEntitySchemaTool.FindEntitySchemaToolName}` to find the package that customizes the schema.
		 Current package request: `{packageName ?? "<merged: all packages>"}`.
		 For the canonical discover -> inspect -> mutate flow, call `{GuidanceGetTool.ToolName}` with `name` set to `existing-app-maintenance`.
		 Use this read step before `modify-entity-schema-column` or `sync-schemas`, and read the schema again after mutation when explicit verification is needed.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to read structured column properties through MCP.
	/// </summary>
	[McpServerPrompt(Name = GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName),
		Description("Prompt to read structured remote entity schema column properties")]
	public static string GetEntitySchemaColumnProperties(
		[Required]
		[Description("Target package name")]
		string packageName,
		[Required]
		[Description("Entity schema name")]
		string schemaName,
		[Required]
		[Description("Column name")]
		string columnName,
		[Required]
		[Description("Creatio environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `{GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName}` to read
		 structured properties for column `{columnName}` in entity schema `{schemaName}` from package
		 `{packageName}` on environment `{environmentName}`.
		 Pass `package-name`, `schema-name`, `column-name`, and `environment-name` exactly as provided.
		 For the canonical discover -> inspect -> mutate flow, call `{GuidanceGetTool.ToolName}` with `name` set to `existing-app-maintenance`.
		 Use this read step before and after `modify-entity-schema-column` when the requested change is scoped to one column.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to mutate a remote entity schema column through MCP.
	/// </summary>
	[McpServerPrompt(Name = ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName),
		Description("Prompt to add, modify, or remove a remote entity schema column")]
	public static string ModifyEntitySchemaColumn(
		[Required]
		[Description("Target package name")]
		string packageName,
		[Required]
		[Description("Entity schema name")]
		string schemaName,
		[Required]
		[Description("Column action: add, modify, or remove")]
		string action,
		[Required]
		[Description("Column name")]
		string columnName,
		[Required]
		[Description("Creatio environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `{ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName}` to perform action
		 `{action}` on column `{columnName}` in entity schema `{schemaName}` from package `{packageName}` on
		 environment `{environmentName}`.
		 Pass only the option fields required for the requested action. For `add`, supply `type`;
		 `title-localizations` is OPTIONAL — when omitted, `en-US` is auto-derived from a scalar title/caption or
		 the column name. Provide `title-localizations` for a proper caption (the `en-US` value must be ENGLISH
		 text — author each localization in its own language; non-English text under `en-US` such as Cyrillic is
		 rejected, use a key like `uk-UA`); for `Lookup`, also supply `reference-schema-name`. For
		 `modify`, include only the fields that should change, using `title-localizations` and
		 `description-localizations` instead of legacy scalar `title` or `description`. For `remove`, do not pass property-change options. Use this tool for a single-column mutation. For ordered
		 multi-column updates, prefer `{UpdateEntitySchemaTool.UpdateEntitySchemaToolName}`. The tool accepts
		 frontend-style type aliases such as `ShortText`, `Float`, `Date`, and `Time`. For default values,
		 prefer `default-value-config` with `source` set to `None`, `Const`, `Settings`, `SystemValue`, or
		 `Sequence`. Keep legacy `default-value-source` and `default-value` only for shorthand `Const` and
		 `None`. Supported types include `Binary`, `Image`, `ImageLookup`, `File`, `SecureText`, and `Email`.
		 `Blob` can be used as an alias for `Binary`, `ImageLink` for `ImageLookup`, `Encrypted` / `Password`
		 can be used as aliases for `SecureText`, and `EmailAddress` can be used as an alias for `Email`.
		 For image/photo fields bound to `crt.ImageInput`, use `ImageLookup` ("Image link"), not the binary
		 `Image` type; `ImageLookup` references `SysImage` automatically, so do not pass `reference-schema-name`. Do not
		 send `default-value` or `default-value-source=Const` for `Binary`, `Image`, or `File`, and use
		 `default-value-config` source `Sequence` only for text columns. For `Settings`, `value-source`
		 accepts setting code, display name, or id and clio normalizes it to setting code before save.
		 For `SystemValue`, `value-source` accepts GUID, enum alias, or display caption and clio
		 normalizes it to GUID before save.
		 For the canonical discover -> inspect -> mutate flow, call `{GuidanceGetTool.ToolName}` with `name` set to `existing-app-maintenance`.
		 Prefer reading current metadata with `{GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName}` first and reading it back after the mutation when explicit verification is needed.
		 """;
}
