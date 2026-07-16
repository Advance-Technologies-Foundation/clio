using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Clio.Command;

/// <summary>
/// Request payload for the get-sys-setting MCP tool: identifies the environment and the sys-setting code to read.
/// </summary>
public sealed record GetSysSettingArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,
	[property: JsonPropertyName("code")]
	[property: Description("Sys-setting code (e.g., 'SchemaNamePrefix').")]
	[property: Required]
	string Code);

/// <summary>
/// Structured response of the get-sys-setting MCP tool: echoes the requested code, returns the All-Users
/// default value (empty when the setting is unknown or has no All-Users row), and carries an optional
/// error message on failure.
/// </summary>
public sealed record SysSettingGetResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("value")] string Value,
	[property: JsonPropertyName("error")] string? Error = null);

/// <summary>
/// Request payload for the list-sys-settings MCP tool: identifies the environment whose sys-setting catalog should be returned.
/// </summary>
public sealed record ListSysSettingsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName);

/// <summary>
/// Structured response of the list-sys-settings MCP tool: catalog of sys-settings filtered to the supported surface
/// (Binary entries are excluded; SecureText values are masked). Carries an optional error message on failure.
/// </summary>
public sealed record SysSettingsListResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("settings")] SysSettingItem[] Settings,
	[property: JsonPropertyName("error")] string? Error = null);

/// <summary>
/// Per-setting row returned by list-sys-settings. The <c>Value</c> field carries the All-Users default formatted by type;
/// for SecureText settings the value is masked to <c>"***"</c> so the catalog cannot be used to harvest secrets.
/// </summary>
public sealed record SysSettingItem(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("value-type-name")] string ValueTypeName,
	[property: JsonPropertyName("value")] string Value,
	[property: JsonPropertyName("is-cacheable")] bool IsCacheable,
	[property: JsonPropertyName("is-personal")] bool IsPersonal);

/// <summary>
/// Request payload for the create-sys-setting MCP tool. Requires environment, code, display name, and value-type-name;
/// for <c>Lookup</c> settings <see cref="ReferenceSchemaName"/> is also required. Optional <see cref="Value"/> is applied
/// via the underlying update path after the row is created.
/// </summary>
public sealed record CreateSysSettingArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,
	[property: JsonPropertyName("code")]
	[property: Description("Sys-setting code (unique).")]
	[property: Required]
	string Code,
	[property: JsonPropertyName("name")]
	[property: Description("Display name of the sys-setting.")]
	[property: Required]
	string Name,
	[property: JsonPropertyName("value-type-name")]
	[property: Description("Value type. Creatio internal name: Text, ShortText, MediumText, LongText, SecureText, MaxSizeText, Boolean, DateTime, Date, Time, Integer, Money, Float, Lookup, Binary. Aliases: Currency = Money, Decimal = Float. Binary settings (a value stored as blob data, such as the logo) are write-only through MCP: set the value with update-sys-setting using value-file-path; MCP does not read the blob back (the CLI get-syssetting returns the raw Base64).")]
	[property: Required]
	string ValueTypeName,
	[property: JsonPropertyName("value")]
	[property: Description("Optional initial All-Users default value. When provided, applied via update-sys-setting after creation.")]
	string? Value = null,
	[property: JsonPropertyName("description")]
	[property: Description("Optional description text.")]
	string? Description = null,
	[property: JsonPropertyName("is-cacheable")]
	[property: Description("Whether the setting is cacheable. Defaults to true.")]
	bool? IsCacheable = null,
	[property: JsonPropertyName("is-personal")]
	[property: Description("Whether the setting stores per-user values. Defaults to false.")]
	bool? IsPersonal = null,
	[property: JsonPropertyName("reference-schema-name")]
	[property: Description("Entity schema name for the lookup target. Required when value-type-name is 'Lookup' (e.g., 'Contact', 'UsrPhoneFormat').")]
	string? ReferenceSchemaName = null);

/// <summary>
/// Structured response of the create-sys-setting MCP tool: echoes the resulting code and value-type-name,
/// reports the assigned value, and surfaces an optional error on failure or an optional partial-success
/// warning when the row was created but the initial value could not be applied.
/// </summary>
public sealed record SysSettingCreateResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("value-type-name")] string ValueTypeName,
	[property: JsonPropertyName("value")] string? Value = null,
	[property: JsonPropertyName("error")] string? Error = null,
	[property: JsonPropertyName("warning")] string? Warning = null);

/// <summary>
/// Request payload for the update-sys-setting MCP tool. <see cref="ValueTypeName"/> is a fallback used only when
/// the setting cannot be located on the target environment so the platform can still receive the value with a typed shape.
/// </summary>
public sealed record UpdateSysSettingArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,
	[property: JsonPropertyName("code")]
	[property: Description("Existing sys-setting code.")]
	[property: Required]
	string Code,
	[property: JsonPropertyName("value")]
	[property: Description("New value. Must match the setting's value-type-name (booleans, decimals, integers, ISO date/time, or a Guid/display-name for Lookup). For a Binary setting this is the raw Base64 payload. Provide either 'value' or 'value-file-path', not both. Optional only because 'value-file-path' is an alternative source.")]
	string? Value = null,
	[property: JsonPropertyName("value-type-name")]
	[property: Description("Optional explicit value-type-name. Used as a fallback when the setting cannot be located on the target environment.")]
	string? ValueTypeName = null,
	[property: JsonPropertyName("value-file-path")]
	[property: Description("Local file path whose bytes clio reads and Base64-encodes into the value. Use this (instead of inline 'value') for Binary settings (blob data, such as the logo) so the blob stays on disk and never travels through the tool-call arguments. Mutually exclusive with 'value'.")]
	string? ValueFilePath = null);

/// <summary>
/// Structured response of the update-sys-setting MCP tool: echoes the requested code and returns the assigned
/// value (read back from the All-Users default after a successful write) or an error message on failure.
/// </summary>
public sealed record SysSettingUpdateResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("value")] string? Value = null,
	[property: JsonPropertyName("error")] string? Error = null);
