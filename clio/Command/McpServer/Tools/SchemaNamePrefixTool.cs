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
	IOperationCorrelationIdProvider correlationIds, ILogger logger) {

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
		} catch (Exception ex) {
			//One classifier, not two. These five hand-written arms disagreed with
			//SysSettingsCommand.CategorizeFailure on three counts: a TLS handshake failure arrives as an
			//AuthenticationException and was reported as rejected credentials (sending the operator to
			//repair a working login while the untrusted certificate stays untouched); an
			//AggregateException - which is how the Creatio client surfaces a transport fault through
			//Task.Result - matched nothing and fell to the generic label; and the correlation ID was
			//minted with no log line to find it in.
			return Failure(ex);
		}
	}

	/// <summary>
	/// Builds the failure envelope from the SHARED classifier, so this tool and the sys-setting tools
	/// cannot answer "was this a credential failure?" differently (issue #1329).
	/// </summary>
	private SchemaNamePrefixResult Failure(Exception ex) {
		SysSettingFailure failure = SysSettingsCommand.CategorizeAndLog(ex, ReadOperationLabel, logger,
			correlationIds);
		return new SchemaNamePrefixResult(false, string.Empty, DescribeError(failure), failure.Category,
			failure.Cause, failure.RecoveryAction, failure.CorrelationId);
	}

	/// <summary>The operation label used in this tool's classified diagnostics.</summary>
	private const string ReadOperationLabel = "reading SchemaNamePrefix";

	/// <summary>This tool's historic generic label, kept for the cases that must not promote a message.</summary>
	private const string GenericReadFailure = "Failed to read SchemaNamePrefix.";

	/// <summary>
	/// The <c>error</c> line: the shared classifier's message, EXCEPT where this tool deliberately refuses
	/// to promote one.
	/// </summary>
	/// <remarks>
	/// An unregistered environment name, or any other failure clio raised about its own state, must not
	/// have its text promoted into the headline field - that rule predates the shared classifier and is
	/// kept. The actionable text is not lost: it is the <c>cause</c>, next to a recovery action.
	/// A <c>DataProviderFailureException</c> is the opposite case and keeps its message, because that
	/// message IS the diagnosis (in particular the non-JSON-page answer naming both possible causes).
	/// </remarks>
	private static string DescribeError(SysSettingFailure failure) =>
		failure.Category is SysSettingErrorCategories.Unknown or SysSettingErrorCategories.Configuration
			or SysSettingErrorCategories.Validation
			? GenericReadFailure
			: failure.Error;
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
