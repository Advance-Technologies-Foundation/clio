using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for reading a single Creatio system setting by code.
/// </summary>
[McpServerToolType]
public sealed class SysSettingGetTool(IToolCommandResolver commandResolver) {

	internal const string GetSysSettingToolName = "get-sys-setting";

	[McpServerTool(Name = GetSysSettingToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Reads the value of a Creatio system setting by code. Returns the raw string value for the All Users default. " +
	             "Returns empty value when the setting is not configured. Use list-sys-settings to discover available codes.")]
	public SysSettingGetResult GetSysSetting(
		[Description("Parameters: environment-name (required), code (required)")]
		[Required]
		GetSysSettingArgs args) {
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
/// MCP tool surface for listing all Creatio system settings with their default values.
/// </summary>
[McpServerToolType]
public sealed class SysSettingsListTool(IToolCommandResolver commandResolver) {

	internal const string ListSysSettingsToolName = "list-sys-settings";

	[McpServerTool(Name = ListSysSettingsToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Lists Creatio system settings with their All-Users default values. " +
	             "Binary-type settings (whose value is stored as blob data, e.g. the logo) are listed too, with their value shown as <binary> because MCP does not surface the blob value; write them with update-sys-setting using value-file-path. " +
	             "Useful to discover settings before calling get-sys-setting or update-sys-setting.")]
	public SysSettingsListResult ListSysSettings(
		[Description("Parameters: environment-name (required)")]
		[Required]
		ListSysSettingsArgs args) {
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
/// MCP tool surface for creating a Creatio system setting.
/// </summary>
[McpServerToolType]
public sealed class SysSettingCreateTool(IToolCommandResolver commandResolver) {

	internal const string CreateSysSettingToolName = "create-sys-setting";

	[McpServerTool(Name = CreateSysSettingToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates a new Creatio system setting and optionally assigns an initial value. " +
	             "Allowed value-type-name values match Creatio internal names: Text, ShortText (Text 50), " +
	             "MediumText (Text 250), LongText (Text 500), SecureText (Encrypted string), MaxSizeText (Unlimited), " +
	             "Boolean, DateTime, Date, Time, Integer, Money (Currency), Float (Decimal), Lookup, Binary. " +
	             "Aliases accepted: Currency = Money, Decimal = Float. " +
	             "For a Binary setting (blob data, such as the logo), assign the initial value via update-sys-setting using value-file-path so clio encodes the file locally; reading a Binary value back is not exposed through MCP. " +
	             "For Lookup type, reference-schema-name is required.")]
	public SysSettingCreateResult CreateSysSetting(
		[Description("Parameters: environment-name, code, name, value-type-name (required); value, description, is-cacheable, is-personal (optional)")]
		[Required]
		CreateSysSettingArgs args) {
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
/// MCP tool surface for updating the value of an existing Creatio system setting.
/// </summary>
[McpServerToolType]
public sealed class SysSettingUpdateTool(IToolCommandResolver commandResolver) {

	internal const string UpdateSysSettingToolName = "update-sys-setting";

	[McpServerTool(Name = UpdateSysSettingToolName, ReadOnly = false, Destructive = true, Idempotent = true,
		OpenWorld = false)]
	[Description("Updates the All-Users default value of an existing Creatio system setting. " +
	             "The setting must already exist; use create-sys-setting to register a new one first. " +
	             "For a Binary setting (blob data, such as the logo) pass value-file-path (a local file path) instead of value — clio reads and Base64-encodes the file locally, keeping the blob out of the tool-call arguments.")]
	public SysSettingUpdateResult UpdateSysSetting(
		[Description("Parameters: environment-name, code, and exactly one of value or value-file-path (required); value-type-name (optional, used as a fallback when the setting type cannot be resolved on the environment)")]
		[Required]
		UpdateSysSettingArgs args) {
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
