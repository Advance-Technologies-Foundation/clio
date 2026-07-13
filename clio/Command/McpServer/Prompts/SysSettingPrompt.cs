using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for Creatio system setting MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for creating, reading, listing, and updating Creatio system settings")]
public static class SysSettingPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to create a Creatio system setting through MCP.
	/// </summary>
	[McpServerPrompt(Name = SysSettingCreateTool.CreateSysSettingToolName),
		Description("Prompt to create a Creatio system setting")]
	public static string CreateSysSetting(
		[Required]
		[Description("Creatio environment name")]
		string environmentName,
		[Required]
		[Description("Sys-setting code (unique)")]
		string code,
		[Required]
		[Description("Display name of the sys-setting")]
		string name,
		[Required]
		[Description("Value type. Creatio internal name: Text, ShortText, MediumText, LongText, SecureText, MaxSizeText, Boolean, DateTime, Date, Time, Integer, Money, Float, Lookup. Aliases: Currency=Money, Decimal=Float. Binary (blob data, such as the logo) is write-only: set the value via update-sys-setting with value-file-path.")]
		string valueTypeName,
		[Description("Optional initial All-Users default value")]
		string value = null,
		[Description("Optional description text")]
		string description = null,
		[Description("Entity schema name for the lookup target. Required when value-type-name is 'Lookup'")]
		string referenceSchemaName = null) =>
		$"""
		 Use clio mcp server `{SysSettingCreateTool.CreateSysSettingToolName}` to create sys-setting `{code}`
		 on environment `{environmentName}` with display name `{name}` and value type `{valueTypeName}`.
		 Pass `environment-name`, `code`, `name`, and `value-type-name` exactly as provided. The `value-type-name`
		 must be a Creatio internal name: `Text`, `ShortText`, `MediumText`, `LongText`, `SecureText`,
		 `MaxSizeText`, `Boolean`, `DateTime`, `Date`, `Time`, `Integer`, `Money`, `Float`, `Lookup`.
		 Aliases `Currency` and `Decimal` map to `Money` and `Float` respectively. `Binary` settings (a value stored
		 as blob data, such as the logo) are write-only through clio: create the setting, then assign the value with
		 `{SysSettingUpdateTool.UpdateSysSettingToolName}` using `value-file-path`. Reading a `Binary` value back is not exposed through MCP (the legacy CLI `get-syssetting` returns the raw Base64).
		 For `Lookup` settings, `reference-schema-name` is required and must reference an entity schema that exists
		 on the target environment (e.g. `Contact`, `UsrPhoneFormat`). For non-Lookup settings, omit it.
		 When the caller supplies an initial value, pass it via `value`; clio then invokes
		 `{SysSettingUpdateTool.UpdateSysSettingToolName}` internally to assign the All-Users default. For Lookup,
		 the value can be a GUID or the display name of a lookup record. For DateTime / Date / Time, use ISO 8601.
		 Read back the saved value via `{SysSettingGetTool.GetSysSettingToolName}` when explicit verification is
		 needed. Note that platform-side read can apply local-TZ conversion for Date and Time values on some
		 environments — treat any single-day or single-hour delta on round-trip as a platform quirk rather than
		 a tool error.
		 Current initial value: `{value ?? "<not provided>"}`. Current reference schema:
		 `{referenceSchemaName ?? "<not provided>"}`.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to read a Creatio system setting value through MCP.
	/// </summary>
	[McpServerPrompt(Name = SysSettingGetTool.GetSysSettingToolName),
		Description("Prompt to read a Creatio system setting value by code")]
	public static string GetSysSetting(
		[Required]
		[Description("Creatio environment name")]
		string environmentName,
		[Required]
		[Description("Sys-setting code (e.g., 'SchemaNamePrefix')")]
		string code) =>
		$"""
		 Use clio mcp server `{SysSettingGetTool.GetSysSettingToolName}` to read the All-Users default value of
		 sys-setting `{code}` on environment `{environmentName}`. Pass `environment-name` and `code` exactly as
		 provided. The tool returns the raw string value; for Lookup settings the value is the GUID of the
		 selected lookup record. When the setting is not configured, the response contains an empty `value`.
		 Use `{SysSettingsListTool.ListSysSettingsToolName}` first when the exact code is unknown.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to list Creatio system settings through MCP.
	/// </summary>
	[McpServerPrompt(Name = SysSettingsListTool.ListSysSettingsToolName),
		Description("Prompt to list Creatio system settings with their default values")]
	public static string ListSysSettings(
		[Required]
		[Description("Creatio environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `{SysSettingsListTool.ListSysSettingsToolName}` to discover sys-settings on
		 environment `{environmentName}`. The response includes code, display name, value-type-name, default
		 value, and the cacheable/personal flags for every setting. Binary-type settings (whose value is stored as blob
		 data, e.g. the logo) are listed too, with their value shown as `<binary>` because MCP does not surface the blob value;
		 write them with `{SysSettingUpdateTool.UpdateSysSettingToolName}` using `value-file-path`.
		 Use this catalog before `{SysSettingGetTool.GetSysSettingToolName}` or
		 `{SysSettingUpdateTool.UpdateSysSettingToolName}` when the exact setting code is unknown.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to update a Creatio system setting through MCP.
	/// </summary>
	[McpServerPrompt(Name = SysSettingUpdateTool.UpdateSysSettingToolName),
		Description("Prompt to update the All-Users default value of a Creatio system setting")]
	public static string UpdateSysSetting(
		[Required]
		[Description("Creatio environment name")]
		string environmentName,
		[Required]
		[Description("Existing sys-setting code")]
		string code,
		[Required]
		[Description("New value. Must match the setting's value-type-name (booleans, decimals, integers, ISO date/time, or a Guid/display-name for Lookup)")]
		string value,
		[Description("Optional explicit value-type-name. Used as a fallback when the setting cannot be located on the target environment")]
		string valueTypeName = null) =>
		$"""
		 Use clio mcp server `{SysSettingUpdateTool.UpdateSysSettingToolName}` to set the All-Users default value
		 of sys-setting `{code}` to `{value}` on environment `{environmentName}`. Pass `environment-name`,
		 `code`, and `value` exactly as provided. The setting must already exist — call
		 `{SysSettingCreateTool.CreateSysSettingToolName}` first when registering a new setting. Pass
		 `value-type-name` only as a fallback when the setting type cannot be resolved from the platform (it is
		 unused otherwise). For a `Binary` setting (blob data, such as the logo), omit `value` and pass `value-file-path`
		 with a local file path instead; clio reads the file and Base64-encodes it, so the blob never travels through the
		 tool-call arguments. For Lookup settings, `value` may be a GUID or the display name of an existing lookup
		 record; clio resolves display names to GUIDs before save. For DateTime / Date / Time, use ISO 8601.
		 Verify the assigned value with `{SysSettingGetTool.GetSysSettingToolName}` when explicit confirmation is
		 needed. Date and Time values may round-trip with a local-TZ offset on some platform deployments — treat
		 single-day or single-hour deltas as a platform quirk rather than a tool error.
		 """;
}
