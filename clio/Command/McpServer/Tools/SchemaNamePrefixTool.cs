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
			SysSettingsManager sysSettings = commandResolver.Resolve<SysSettingsManager>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
			string prefix = SysSettingCodes.ReadSchemaNamePrefix(sysSettings);
			return new SchemaNamePrefixResult(true, prefix);
		} catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Net.WebException or System.Net.Sockets.SocketException) {
			return new SchemaNamePrefixResult(false, string.Empty, "Network error reading SchemaNamePrefix.");
		} catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.Authentication.AuthenticationException) {
			return new SchemaNamePrefixResult(false, string.Empty, "Authentication error reading SchemaNamePrefix.");
		} catch (Exception) {
			return new SchemaNamePrefixResult(false, string.Empty, "Failed to read SchemaNamePrefix.");
		}
	}
}

/// <summary>
/// MCP arguments for the <c>get-schema-name-prefix</c> tool.
/// </summary>
public sealed record GetSchemaNamePrefixArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName);

/// <summary>
/// MCP response for the <c>get-schema-name-prefix</c> tool.
/// </summary>
public sealed record SchemaNamePrefixResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("schema-name-prefix")] string SchemaNamePrefix,
	[property: JsonPropertyName("error")] string? Error = null);
