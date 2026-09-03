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
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Returns the active SchemaNamePrefix system setting for the environment. " +
	             "Returns empty string when no prefix is configured (use no prefix in that case); an empty prefix always arrives with success:true, while a rejected session is reported as success:false with an authentication error. " +
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
		} catch (DataProviderFailureException ex) {
			//Surfaces the message rather than collapsing to "Failed to read SchemaNamePrefix.", which is the
			//same rule SysSettingsCommand.CategorizeError applies. This type - and ONLY this type - means
			//the message IS the diagnosis the caller cannot reconstruct, in particular the non-JSON-page
			//answer that names both possible causes (rejected session, or a URL that does not reach
			//Creatio). A plain InvalidOperationException keeps the generic label below: an unregistered
			//environment name must not have its resolver text promoted into this field.
			//
			//REDACTED before it is returned. This tool RETURNS a result record instead of throwing, so
			//McpToolErrorFilter - which is where SensitiveErrorTextRedactor.Redact normally runs - never sees
			//this text, and it goes straight into the MCP client transcript. The message is composed by
			//ClassifyingDataProvider and embeds up to 300 characters of server-controlled provider text, which
			//is exactly where environment URLs, host:port pairs and file paths surface.
			return new SchemaNamePrefixResult(false, string.Empty, SensitiveErrorTextRedactor.Redact(ex.Message));
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
