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
public sealed class SchemaNamePrefixTool(IToolCommandResolver commandResolver,
	IOperationCorrelationIdProvider correlationIds) {

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
			return Failure("Network error reading SchemaNamePrefix.", SysSettingErrorCategories.Network,
				SysSettingFailureTexts.NetworkCause, SysSettingFailureTexts.NetworkRecovery);
		} catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.Authentication.AuthenticationException) {
			return Failure("Authentication error reading SchemaNamePrefix.",
				SysSettingErrorCategories.Authentication, SysSettingFailureTexts.AuthenticationCause,
				SysSettingFailureTexts.AuthenticationRecovery);
		} catch (DataProviderFailureException ex) {
			//Surfaces the message rather than collapsing to "Failed to read SchemaNamePrefix.", which is the
			//same rule SysSettingsCommand.CategorizeError applies. This type - and ONLY this type - means
			//the message IS the diagnosis the caller cannot reconstruct, in particular the non-JSON-page
			//answer that names both possible causes (rejected session, or a URL that does not reach
			//Creatio). A plain InvalidOperationException keeps the generic label below: an unregistered
			//environment name must not have its resolver text promoted into this field.
			return Failure(ex.Message, SysSettingErrorCategories.ProviderFailure, ex.Message,
				SysSettingFailureTexts.ProviderFailureRecovery);
		} catch (Exception) {
			return Failure("Failed to read SchemaNamePrefix.", SysSettingErrorCategories.Unknown,
				SysSettingFailureTexts.UnknownCause, SysSettingFailureTexts.UnknownRecovery);
		}
	}

	/// <summary>
	/// Builds the failure envelope: the legacy <c>error</c> text unchanged, plus the classified cause,
	/// the recovery action, and the correlation ID that ties the envelope to the log line (issue #1329).
	/// </summary>
	private SchemaNamePrefixResult Failure(string error, string category, string cause,
		string recoveryAction) =>
		new(false, string.Empty, error, category, cause, recoveryAction, correlationIds.New());
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
/// On failure the envelope also carries <c>error-category</c>, <c>cause</c>, <c>recovery-action</c>
/// and <c>correlation-id</c> (issue #1329); <c>error</c> keeps its historic single-line text.
/// </summary>
public sealed record SchemaNamePrefixResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("schema-name-prefix")] string SchemaNamePrefix,
	[property: JsonPropertyName("error")] string? Error = null,
	[property: JsonPropertyName("error-category")] string? ErrorCategory = null,
	[property: JsonPropertyName("cause")] string? Cause = null,
	[property: JsonPropertyName("recovery-action")] string? RecoveryAction = null,
	[property: JsonPropertyName("correlation-id")] string? CorrelationId = null);
