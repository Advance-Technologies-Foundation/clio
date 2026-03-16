using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>generate-process-model</c> command.
/// </summary>
[McpServerToolType]
public sealed class GenerateProcessModelTool(
	GenerateProcessModelCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GenerateProcessModelCommandOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for process-model generation.
	/// </summary>
	internal const string GenerateProcessModelToolName = "generate-process-model";

	/// <summary>
	/// Generates a process model file for the requested Creatio process code.
	/// </summary>
	[McpServerTool(Name = GenerateProcessModelToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Generates a C# process model file for a process from the specified Creatio environment.")]
	public CommandExecutionResult GenerateProcessModel(
		[Description("generate-process-model parameters")]
		[Required]
		GenerateProcessModelArgs args) {
		GenerateProcessModelCommandOptions options = new() {
			Code = args.Code,
			DestinationPath = args.DestinationPath ?? ".",
			Namespace = args.Namespace ?? "AtfTIDE.ProcessModels",
			Culture = args.Culture ?? "en-US",
			Environment = args.EnvironmentName
		};
		try {
			return InternalExecute<GenerateProcessModelCommand>(options);
		}
		catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(exception.Message)]);
		}
	}
}

/// <summary>
/// MCP arguments for the <c>generate-process-model</c> tool.
/// </summary>
public sealed record GenerateProcessModelArgs(
	[property: JsonPropertyName("code")]
	[property: Description("Process code as it appears in the Creatio process designer")]
	[property: Required]
	string Code,

	[property: JsonPropertyName("destination-path")]
	[property: Description("Optional destination folder or explicit .cs file path for the generated process model. Preserves current command behavior when omitted.")]
	string? DestinationPath,

	[property: JsonPropertyName("namespace")]
	[property: Description("Optional namespace for the generated process model class")]
	string? Namespace,

	[property: JsonPropertyName("culture")]
	[property: Description("Optional culture used to resolve localized descriptions")]
	string? Culture,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name")]
	[property: Required]
	string EnvironmentName
);
