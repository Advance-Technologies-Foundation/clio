using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
		GenerateSourceCodeOptions options = new() {
			Environment = args.EnvironmentName,
			Modified = args.Modified ?? false,
			Required = args.Required ?? false,
			Background = args.Background ?? false
		};
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
	bool? Background
);
