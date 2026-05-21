using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Clio.Command;

public sealed record GetSysSettingArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,
	[property: JsonPropertyName("code")]
	[property: Description("Sys-setting code (e.g., 'SchemaNamePrefix').")]
	[property: Required]
	string Code);

public sealed record SysSettingGetResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("value")] string Value,
	[property: JsonPropertyName("error")] string? Error = null);

public sealed record ListSysSettingsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName);

public sealed record SysSettingsListResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("settings")] SysSettingItem[] Settings,
	[property: JsonPropertyName("error")] string? Error = null);

public sealed record SysSettingItem(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("value-type-name")] string ValueTypeName,
	[property: JsonPropertyName("value")] string Value,
	[property: JsonPropertyName("is-cacheable")] bool IsCacheable,
	[property: JsonPropertyName("is-personal")] bool IsPersonal);

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
	[property: Description("Value type. Creatio internal name: Text, ShortText, MediumText, LongText, SecureText, MaxSizeText, Boolean, DateTime, Date, Time, Integer, Money, Float, Lookup. Aliases: Currency = Money, Decimal = Float. Binary sys-settings are not exposed by this tool set — they need a dedicated upload flow.")]
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

public sealed record SysSettingCreateResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("value-type-name")] string ValueTypeName,
	[property: JsonPropertyName("value")] string? Value = null,
	[property: JsonPropertyName("error")] string? Error = null,
	[property: JsonPropertyName("warning")] string? Warning = null);

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
	[property: Description("New value. Must match the setting's value-type-name (booleans, decimals, integers, ISO date/time, or a Guid/display-name for Lookup).")]
	[property: Required]
	string Value,
	[property: JsonPropertyName("value-type-name")]
	[property: Description("Optional explicit value-type-name. Used as a fallback when the setting cannot be located on the target environment.")]
	string? ValueTypeName = null);

public sealed record SysSettingUpdateResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("value")] string? Value = null,
	[property: JsonPropertyName("error")] string? Error = null);
