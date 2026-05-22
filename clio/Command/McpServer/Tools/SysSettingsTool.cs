using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Consolidated MCP tool that reads one Creatio system setting (when <c>code</c> is provided)
/// or lists all settings (when <c>code</c> is empty). Folds the legacy get-sys-setting and
/// list-sys-settings tools.
/// </summary>
[McpServerToolType]
public sealed class SysSettingTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "sys-setting";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Reads a Creatio system setting when `code` is provided; lists all sys-settings (excluding binary) when `code` is empty. " +
		"Useful to discover settings before calling upsert-sys-setting.")]
	public object Read(
		[Description("Parameters: environment-name (required); code (optional — when omitted, returns the list of settings).")]
		[Required]
		SysSettingArgs args) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return new SysSettingsListResult(false, Array.Empty<SysSettingItem>(),
				"environment-name is required.");
		}
		SysSettingsCommand command;
		try {
			command = commandResolver.Resolve<SysSettingsCommand>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
		} catch (Exception ex) {
			return string.IsNullOrWhiteSpace(args.Code)
				? new SysSettingsListResult(false, Array.Empty<SysSettingItem>(),
					SysSettingsCommand.CategorizeError(ex, "listing sys-settings"))
				: new SysSettingGetResult(false, args.Code, string.Empty,
					SysSettingsCommand.CategorizeError(ex, "reading sys-setting"));
		}
		if (string.IsNullOrWhiteSpace(args.Code)) {
			return command.TryListSysSettings(new ListSysSettingsArgs(args.EnvironmentName));
		}
		return command.TryGetSysSetting(new GetSysSettingArgs(args.EnvironmentName, args.Code));
	}
}

/// <summary>
/// Consolidated MCP tool that creates a Creatio system setting or updates its value if the setting
/// already exists. Folds the legacy create-sys-setting and update-sys-setting tools.
/// </summary>
[McpServerToolType]
public sealed class SysSettingUpsertTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "upsert-sys-setting";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Creates a Creatio system setting if it does not exist; otherwise updates its All-Users default value. " +
		"value-type-name is required when the setting must be created and is used as a fallback when the setting type cannot be resolved on the environment. " +
		"For Lookup type, reference-schema-name is required.")]
	public object Upsert(
		[Description("Parameters: environment-name, code (required); value-type-name (required when creating); value, name, description, is-cacheable, is-personal (optional).")]
		[Required]
		UpsertSysSettingArgs args) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return new SysSettingUpdateResult(false, args.Code ?? string.Empty, null,
				"environment-name is required.");
		}
		if (string.IsNullOrWhiteSpace(args.Code)) {
			return new SysSettingUpdateResult(false, string.Empty, null,
				"code is required.");
		}
		SysSettingsCommand command;
		try {
			command = commandResolver.Resolve<SysSettingsCommand>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
		} catch (Exception ex) {
			return new SysSettingUpdateResult(false, args.Code, null,
				SysSettingsCommand.CategorizeError(ex, "upserting sys-setting"));
		}

		// Probe existing setting: if reachable, prefer update; otherwise create.
		SysSettingGetResult existing = command.TryGetSysSetting(new GetSysSettingArgs(args.EnvironmentName, args.Code));
		if (existing.Success) {
			return command.TryUpdateSysSetting(new UpdateSysSettingArgs(
				args.EnvironmentName,
				args.Code,
				args.Value ?? string.Empty) {
				ValueTypeName = args.ValueTypeName
			});
		}
		if (string.IsNullOrWhiteSpace(args.ValueTypeName)) {
			return new SysSettingCreateResult(false, args.Code, string.Empty, null,
				$"Setting '{args.Code}' does not exist and value-type-name was not supplied; cannot create.");
		}
		return command.TryCreateSysSetting(new CreateSysSettingArgs(
			args.EnvironmentName,
			args.Code,
			args.Name ?? args.Code,
			args.ValueTypeName) {
			Value = args.Value,
			Description = args.Description,
			IsCacheable = args.IsCacheable,
			IsPersonal = args.IsPersonal,
			ReferenceSchemaName = args.ReferenceSchemaName
		});
	}
}

/// <summary>
/// Legacy MCP tool surface retained so ToolContractGetTool documentation and prompt helpers
/// still resolve. The MCP entry point now lives on <see cref="SysSettingTool"/>.
/// </summary>
public sealed class SysSettingGetTool(IToolCommandResolver commandResolver) {

	/// <summary>Legacy MCP tool name retained for documentation.</summary>
	internal const string GetSysSettingToolName = "get-sys-setting";

	public SysSettingGetResult GetSysSetting(GetSysSettingArgs args) {
		SysSettingsCommand command;
		try {
			command = commandResolver.Resolve<SysSettingsCommand>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
		} catch (Exception ex) {
			return new SysSettingGetResult(false, args.Code ?? string.Empty, string.Empty,
				SysSettingsCommand.CategorizeError(ex, "reading sys-setting"));
		}
		return command.TryGetSysSetting(args);
	}
}

/// <summary>
/// Legacy MCP tool surface retained so ToolContractGetTool documentation and prompt helpers
/// still resolve. The MCP entry point now lives on <see cref="SysSettingTool"/>.
/// </summary>
public sealed class SysSettingsListTool(IToolCommandResolver commandResolver) {

	/// <summary>Legacy MCP tool name retained for documentation.</summary>
	internal const string ListSysSettingsToolName = "list-sys-settings";

	public SysSettingsListResult ListSysSettings(ListSysSettingsArgs args) {
		SysSettingsCommand command;
		try {
			command = commandResolver.Resolve<SysSettingsCommand>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
		} catch (Exception ex) {
			return new SysSettingsListResult(false, Array.Empty<SysSettingItem>(),
				SysSettingsCommand.CategorizeError(ex, "listing sys-settings"));
		}
		return command.TryListSysSettings(args);
	}
}

/// <summary>
/// Legacy MCP tool surface retained so ToolContractGetTool documentation and prompt helpers
/// still resolve. The MCP entry point now lives on <see cref="SysSettingUpsertTool"/>.
/// </summary>
public sealed class SysSettingCreateTool(IToolCommandResolver commandResolver) {

	/// <summary>Legacy MCP tool name retained for documentation.</summary>
	internal const string CreateSysSettingToolName = "create-sys-setting";

	public SysSettingCreateResult CreateSysSetting(CreateSysSettingArgs args) {
		SysSettingsCommand command;
		try {
			command = commandResolver.Resolve<SysSettingsCommand>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
		} catch (Exception ex) {
			return new SysSettingCreateResult(false, args.Code ?? string.Empty, args.ValueTypeName ?? string.Empty,
				null, SysSettingsCommand.CategorizeError(ex, "creating sys-setting"));
		}
		return command.TryCreateSysSetting(args);
	}
}

/// <summary>
/// Legacy MCP tool surface retained so ToolContractGetTool documentation and prompt helpers
/// still resolve. The MCP entry point now lives on <see cref="SysSettingUpsertTool"/>.
/// </summary>
public sealed class SysSettingUpdateTool(IToolCommandResolver commandResolver) {

	/// <summary>Legacy MCP tool name retained for documentation.</summary>
	internal const string UpdateSysSettingToolName = "update-sys-setting";

	public SysSettingUpdateResult UpdateSysSetting(UpdateSysSettingArgs args) {
		SysSettingsCommand command;
		try {
			command = commandResolver.Resolve<SysSettingsCommand>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
		} catch (Exception ex) {
			return new SysSettingUpdateResult(false, args.Code ?? string.Empty, null,
				SysSettingsCommand.CategorizeError(ex, "updating sys-setting"));
		}
		return command.TryUpdateSysSetting(args);
	}
}

/// <summary>
/// Arguments for the consolidated <c>sys-setting</c> read tool.
/// </summary>
public sealed record SysSettingArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("code")]
	[property: Description("Optional. When provided, returns the setting value; when empty, returns the full list of sys-settings.")]
	string? Code = null
);

/// <summary>
/// Arguments for the consolidated <c>upsert-sys-setting</c> write tool.
/// </summary>
public sealed record UpsertSysSettingArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("code")]
	[property: Description("Setting code.")]
	[property: Required]
	string Code,

	[property: JsonPropertyName("value-type-name")]
	[property: Description("Required when the setting must be created (i.e. the setting does not yet exist). Used as a fallback when the setting type cannot be resolved on the environment. " +
		"Allowed values: Text, ShortText, MediumText, LongText, SecureText, MaxSizeText, Boolean, DateTime, Date, Time, Integer, Money, Float, Lookup. " +
		"Aliases: Currency=Money, Decimal=Float.")]
	string? ValueTypeName = null,

	[property: JsonPropertyName("value")]
	[property: Description("Optional setting value (treated as the All-Users default).")]
	string? Value = null,

	[property: JsonPropertyName("name")]
	[property: Description("Optional display name. Defaults to code when creating.")]
	string? Name = null,

	[property: JsonPropertyName("description")]
	[property: Description("Optional description.")]
	string? Description = null,

	[property: JsonPropertyName("is-cacheable")]
	[property: Description("Optional cacheable flag (defaults vary by setting type).")]
	bool? IsCacheable = null,

	[property: JsonPropertyName("is-personal")]
	[property: Description("Optional personal flag (defaults vary by setting type).")]
	bool? IsPersonal = null,

	[property: JsonPropertyName("reference-schema-name")]
	[property: Description("Required when value-type-name=Lookup. Reference entity schema for lookup-typed settings.")]
	string? ReferenceSchemaName = null
);
