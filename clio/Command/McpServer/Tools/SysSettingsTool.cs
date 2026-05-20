using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json.Serialization;
using Clio.Common;
using CreatioModel;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

internal static class SysSettingsToolSupport {

	internal const string LookupTypeName = "Lookup";

	internal static readonly string[] SupportedValueTypeNames = [
		"Text", "ShortText", "MediumText", "LongText", "SecureText", "MaxSizeText",
		"Boolean", "DateTime", "Date", "Time", "Integer",
		"Money", "Float", "Binary", LookupTypeName,
		"Currency", "Decimal"
	];

	internal static T ResolveManager<T>(IToolCommandResolver commandResolver, string? environmentName) {
		return commandResolver.Resolve<T>(new EnvironmentOptions { Environment = environmentName });
	}

	internal static SysSettingsErrorResponse<TPayload> CategorizeError<TPayload>(Exception ex, string operationLabel,
		TPayload emptyPayload) {
		string message = ex switch {
			HttpRequestException => $"Network error {operationLabel}.",
			WebException => $"Network error {operationLabel}.",
			SocketException => $"Network error {operationLabel}.",
			UnauthorizedAccessException => $"Authentication error {operationLabel}.",
			AuthenticationException => $"Authentication error {operationLabel}.",
			ArgumentException argEx => argEx.Message,
			InvalidOperationException invEx => invEx.Message,
			_ => $"Failed {operationLabel}."
		};
		return new SysSettingsErrorResponse<TPayload>(emptyPayload, message);
	}

	internal sealed record SysSettingsErrorResponse<TPayload>(TPayload Payload, string Message);
}

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
		try {
			if (string.IsNullOrWhiteSpace(args.Code)) {
				throw new ArgumentException("code is required.");
			}
			SysSettingsManager manager = SysSettingsToolSupport.ResolveManager<SysSettingsManager>(
				commandResolver, args.EnvironmentName);
			string value = manager.GetSysSettingValueByCode(args.Code);
			return new SysSettingGetResult(true, args.Code, value ?? string.Empty);
		} catch (Exception ex) {
			SysSettingsToolSupport.SysSettingsErrorResponse<string> error =
				SysSettingsToolSupport.CategorizeError(ex, "reading sys-setting", string.Empty);
			return new SysSettingGetResult(false, args.Code, error.Payload, error.Message);
		}
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
	             "Binary-type settings are omitted from the list to keep responses small — use get-sys-setting by code to read one. " +
	             "Useful to discover settings before calling get-sys-setting or update-sys-setting.")]
	public SysSettingsListResult ListSysSettings(
		[Description("Parameters: environment-name (required)")]
		[Required]
		ListSysSettingsArgs args) {
		try {
			SysSettingsManager manager = SysSettingsToolSupport.ResolveManager<SysSettingsManager>(
				commandResolver, args.EnvironmentName);
			List<SysSettings> settings = manager.GetAllSysSettingsWithValues();
			SysSettingItem[] items = settings.Select(setting => new SysSettingItem(
					setting.Code,
					setting.Name,
					setting.ValueTypeName,
					setting.DefValue,
					setting.IsCacheable,
					setting.IsPersonal))
				.ToArray();
			return new SysSettingsListResult(true, items);
		} catch (Exception ex) {
			SysSettingsToolSupport.SysSettingsErrorResponse<SysSettingItem[]> error =
				SysSettingsToolSupport.CategorizeError(ex, "listing sys-settings", Array.Empty<SysSettingItem>());
			return new SysSettingsListResult(false, error.Payload, error.Message);
		}
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
	             "Boolean, DateTime, Date, Time, Integer, Money (Currency), Float (Decimal), Binary (BLOB), Lookup. " +
	             "Aliases accepted: Currency = Money, Decimal = Float. " +
	             "For Lookup type, reference-schema-name is required.")]
	public SysSettingCreateResult CreateSysSetting(
		[Description("Parameters: environment-name, code, name, value-type-name (required); value, description, is-cacheable, is-personal (optional)")]
		[Required]
		CreateSysSettingArgs args) {
		try {
			ValidateCreateArgs(args);
			SysSettingsManager manager = SysSettingsToolSupport.ResolveManager<SysSettingsManager>(
				commandResolver, args.EnvironmentName);
			Guid? referenceSchemaUId = ResolveReferenceSchemaUId(manager, args);
			SysSettingsManager.InsertSysSettingResponse response = manager.InsertSysSetting(
				args.Name,
				args.Code,
				args.ValueTypeName,
				args.IsCacheable ?? true,
				args.Description ?? string.Empty,
				args.IsPersonal ?? false,
				referenceSchemaUId);
			if (!response.Success) {
				string message = response.ResponseStatus?.Message;
				return new SysSettingCreateResult(false, args.Code, args.ValueTypeName, null,
					string.IsNullOrWhiteSpace(message) ? "Failed creating sys-setting." : message);
			}
			return ApplyInitialValue(manager, args);
		} catch (Exception ex) {
			SysSettingsToolSupport.SysSettingsErrorResponse<string> error =
				SysSettingsToolSupport.CategorizeError<string>(ex, "creating sys-setting", null);
			return new SysSettingCreateResult(false, args.Code, args.ValueTypeName, error.Payload, error.Message);
		}
	}

	private static void ValidateCreateArgs(CreateSysSettingArgs args) {
		if (string.IsNullOrWhiteSpace(args.Code)) {
			throw new ArgumentException("code is required.");
		}
		if (string.IsNullOrWhiteSpace(args.Name)) {
			throw new ArgumentException("name is required.");
		}
		if (string.IsNullOrWhiteSpace(args.ValueTypeName)) {
			throw new ArgumentException("value-type-name is required.");
		}
		if (!SysSettingsToolSupport.SupportedValueTypeNames.Contains(args.ValueTypeName, StringComparer.Ordinal)) {
			throw new ArgumentException(
				$"Unsupported value-type-name '{args.ValueTypeName}'. Allowed values: " +
				string.Join(", ", SysSettingsToolSupport.SupportedValueTypeNames) + ".");
		}
		if (args.ValueTypeName == SysSettingsToolSupport.LookupTypeName
			&& string.IsNullOrWhiteSpace(args.ReferenceSchemaName)) {
			throw new ArgumentException(
				"reference-schema-name is required when value-type-name is 'Lookup'.");
		}
	}

	private static Guid? ResolveReferenceSchemaUId(SysSettingsManager manager, CreateSysSettingArgs args) {
		if (args.ValueTypeName != SysSettingsToolSupport.LookupTypeName
			|| string.IsNullOrWhiteSpace(args.ReferenceSchemaName)) {
			return null;
		}
		Guid? uId = manager.FindSchemaUIdByName(args.ReferenceSchemaName);
		if (uId is null) {
			throw new ArgumentException(
				$"Entity schema '{args.ReferenceSchemaName}' was not found on the target environment.");
		}
		return uId;
	}

	private static SysSettingCreateResult ApplyInitialValue(SysSettingsManager manager, CreateSysSettingArgs args) {
		if (args.Value is null) {
			return new SysSettingCreateResult(true, args.Code, args.ValueTypeName);
		}
		bool updated = manager.UpdateSysSetting(args.Code, args.Value, args.ValueTypeName);
		if (!updated) {
			return new SysSettingCreateResult(true, args.Code, args.ValueTypeName, null,
				Error: null,
				Warning: "Sys-setting was created, but the initial value could not be applied.");
		}
		string assignedValue = manager.GetSysSettingValueByCode(args.Code);
		return new SysSettingCreateResult(true, args.Code, args.ValueTypeName, assignedValue);
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
	             "The setting must already exist — use create-sys-setting to register a new one first.")]
	public SysSettingUpdateResult UpdateSysSetting(
		[Description("Parameters: environment-name, code, value (required); value-type-name (optional, used as a fallback when the setting type cannot be resolved on the environment)")]
		[Required]
		UpdateSysSettingArgs args) {
		try {
			if (string.IsNullOrWhiteSpace(args.Code)) {
				throw new ArgumentException("code is required.");
			}
			if (args.Value is null) {
				throw new ArgumentException("value is required.");
			}
			SysSettingsManager manager = SysSettingsToolSupport.ResolveManager<SysSettingsManager>(
				commandResolver, args.EnvironmentName);
			string valueTypeName = string.IsNullOrWhiteSpace(args.ValueTypeName) ? "Text" : args.ValueTypeName;
			bool updated = manager.UpdateSysSetting(args.Code, args.Value, valueTypeName);
			if (!updated) {
				return new SysSettingUpdateResult(false, args.Code, null,
					"Failed to update sys-setting. The setting may not exist, or the value did not match the expected type.");
			}
			string readback = manager.GetSysSettingValueByCode(args.Code);
			return new SysSettingUpdateResult(true, args.Code, readback);
		} catch (Exception ex) {
			SysSettingsToolSupport.SysSettingsErrorResponse<string> error =
				SysSettingsToolSupport.CategorizeError<string>(ex, "updating sys-setting", null);
			return new SysSettingUpdateResult(false, args.Code, error.Payload, error.Message);
		}
	}
}

#region Args & Results

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
	[property: Description("Value type. Creatio internal name: Text, ShortText, MediumText, LongText, SecureText, MaxSizeText, Boolean, DateTime, Date, Time, Integer, Money, Float, Binary, Lookup. Aliases: Currency = Money, Decimal = Float.")]
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

#endregion
