using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for reading the active SchemaNamePrefix system setting.
/// </summary>
[McpServerToolType]
public sealed class SchemaNamePrefixTool(IToolCommandResolver commandResolver) {

	internal const string GetSchemaNamePrefixToolName = "get-schema-name-prefix";
	private const string SchemaNamePrefixSettingCode = "SchemaNamePrefix";

	/// <summary>
	/// Returns the active SchemaNamePrefix system setting for the environment.
	/// </summary>
	[McpServerTool(Name = GetSchemaNamePrefixToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Returns the active SchemaNamePrefix system setting for the environment. " +
	             "Returns empty string when no prefix is configured (use no prefix in that case). " +
	             "Default Creatio environments return 'Usr'. " +
	             "Note: create-app and get-app-info both read this setting automatically and return schema-name-prefix " +
	             "in their responses — you only need this tool when you require the prefix before calling either of those.")]
	public SchemaNamePrefixResult GetSchemaNamePrefix(
		[Description("Parameters: environment-name (required)")]
		[Required]
		GetSchemaNamePrefixArgs args) {
		try {
			ISysSettingsManager sysSettings = commandResolver.Resolve<ISysSettingsManager>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
			string rawValue = sysSettings.GetSysSettingValueByCode(SchemaNamePrefixSettingCode);
			string prefix = rawValue?.Trim().Trim('"') ?? string.Empty;
			return new SchemaNamePrefixResult(true, prefix);
		} catch (Exception ex) {
			return new SchemaNamePrefixResult(false, string.Empty, ex.Message);
		}
	}
}

/// <summary>
/// MCP arguments for the <c>get-schema-name-prefix</c> tool.
/// </summary>
public sealed record GetSchemaNamePrefixArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'")]
	[property: Required]
	string EnvironmentName);

/// <summary>
/// MCP response for the <c>get-schema-name-prefix</c> tool.
/// </summary>
public sealed record SchemaNamePrefixResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("schema-name-prefix")] string SchemaNamePrefix,
	[property: JsonPropertyName("error")] string? Error = null);
