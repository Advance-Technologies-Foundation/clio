using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>generate-source-code</c> command.
/// </summary>
[McpServerToolType]
public sealed class GenerateSourceCodeTool(
	GenerateSourceCodeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GenerateSourceCodeOptions>(command, logger, commandResolver)
{

	/// <summary>
	/// Stable MCP tool name for source code generation.
	/// </summary>
	internal const string GenerateSourceCodeToolName = "generate-source-code";

	/// <summary>
	/// Mis-spellings of this tool's own fields, mapped to their canonical kebab-case names. <c>timeOut</c> is
	/// the likeliest one because it matches the C# property (<c>RemoteCommandOptions.TimeOut</c>), so it earns a
	/// rename hint rather than being lumped into the generic unknown-argument list.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string> ArgsFieldAliases =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["timeOut"] = "timeout",
			["time_out"] = "timeout",
			["timeoutMs"] = "timeout",
			["timeout-ms"] = "timeout"
		}.Concat(McpToolArgumentSupport.EnvironmentNameAliases)
		.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

	/// <summary>
	/// The largest <c>timeout</c> a caller may set, in milliseconds (3 hours - three times the 60-minute
	/// default). Before <c>timeout</c> became an MCP argument every call was capped by that default; without an
	/// upper bound a mis-scaled or guessed value (<c>86400000</c>, <c>int.MaxValue</c>) would hold the HTTP
	/// request and the MCP tool call open for weeks, and <c>generate-source-code</c> is a server write, so
	/// <c>McpReadDeadlineGate.IsRetrySafe</c> excludes it from the pipeline read-response deadline - nothing
	/// else would cut the call short (PR #1354 review).
	/// </summary>
	internal const int MaxTimeoutMilliseconds = 3 * 60 * 60 * 1000;

	/// <summary>
	/// Triggers source code generation for schemas in a registered Creatio environment.
	/// </summary>
	[McpServerTool(Name = GenerateSourceCodeToolName, ReadOnly = false, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description(
		"Generates source code for schemas in the specified Creatio environment. " +
		"Equivalent to the 'Generate source code' button in the Creatio Configuration section. " +
		"By default generates source code for all schemas (synchronous). " +
		"Use `modified` to regenerate only modified schemas, `required` for schemas that need it, " +
		"or `background` to fire-and-forget (matching the UI behaviour).")]
	public CommandExecutionResult GenerateSourceCode(
		[Description("generate-source-code parameters")]
		[Required]
		GenerateSourceCodeArgs args) {
		string? argsError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData,
			ArgsFieldAliases,
			".",
			"Valid fields: environment-name, modified, required, background, timeout.");
		if (argsError is not null) {
			return CommandExecutionResult.FromValidationError(
				$"generate-source-code arguments are invalid: {argsError} Nothing was generated.");
		}
		GenerateSourceCodeOptions options = new() {
			Environment = args.EnvironmentName,
			Modified = args.Modified ?? false,
			Required = args.Required ?? false,
			Background = args.Background ?? false
		};
		if (args.Timeout is { } requestedTimeout) {
			if (requestedTimeout <= 0 || requestedTimeout > MaxTimeoutMilliseconds) {
				return CommandExecutionResult.FromValidationError(
					$"generate-source-code 'timeout' must be between 1 and {MaxTimeoutMilliseconds} milliseconds "
					+ $"({MaxTimeoutMilliseconds / 60_000} minutes).");
			}
			options.TimeOut = requestedTimeout;
		}
		try {
			return InternalExecute<GenerateSourceCodeCommand>(options);
		}
		catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(SensitiveErrorTextRedactor.Redact(exception.Message))]);
		}
	}

}

/// <summary>
/// MCP arguments for the <c>generate-source-code</c> tool.
/// </summary>
public sealed record GenerateSourceCodeArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("modified")]
	[property: Description("When true, regenerates source code only for modified schemas (GenerateModifiedSchemasSources)")]
	bool? Modified,

	[property: JsonPropertyName("required")]
	[property: Description("When true, regenerates source code only for schemas that require it (GenerateRequiredSchemasSources)")]
	bool? Required,

	[property: JsonPropertyName("background")]
	[property: Description("When true, runs generation in background and returns immediately — matches the UI 'Generate all' behaviour (GenerateAllSchemasSourcesInBackground)")]
	bool? Background,

	[property: JsonPropertyName("timeout")]
	[property: Description("Request timeout in milliseconds. Defaults to 60 minutes, matching the CLI --timeout option; " +
		"the maximum accepted value is 10800000 (3 hours). " +
		"A cancelled or timed-out generation FAILS the call with a non-zero exit code (never 0) and an error naming the timeout — it is never reported as a successful generation.")]
	int? Timeout = null
) {
	/// <summary>
	/// Overflow bag for fields that did not bind, so a mis-keyed argument is reported instead of being dropped
	/// by System.Text.Json (issue #1303).
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
