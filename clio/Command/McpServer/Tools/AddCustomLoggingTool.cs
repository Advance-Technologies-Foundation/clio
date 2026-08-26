using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>MCP adapter for package-specific local NLog configuration.</summary>
[McpServerToolType]
public sealed class AddCustomLoggingTool(
	AddCustomLoggingCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<AddCustomLoggingOptions>(command, logger, commandResolver) {

	/// <summary>Stable MCP tool name for package-specific NLog configuration.</summary>
	internal const string ToolName = "add-custom-logging";

	/// <summary>Adds an idempotent logger rule and file target to a registered local Creatio installation.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Adds package-specific NLog routing to a registered local Creatio installation. "
		+ "The environment must have EnvironmentPath configured. The command reads the package's generated "
		+ "Constants.LoggerName, validates both NLog files, preserves their encoding and unrelated content, "
		+ "and rolls back if the two-file commit fails. Configuration-only reruns are no-ops; setting "
		+ "restart-environment=true also restarts Creatio and therefore is not idempotent.")]
	public CommandExecutionResult AddCustomLogging(
		[Description("add-custom-logging parameters")] [Required] AddCustomLoggingArgs args) {
		AddCustomLoggingOptions options = new() {
			Environment = args.EnvironmentName,
			PackageName = args.PackageName,
			MinLevel = args.MinLevel ?? "Info",
			FileName = args.FileName,
			RestartEnvironment = args.RestartEnvironment
		};
		return InternalExecute<AddCustomLoggingCommand>(options);
	}
}

/// <summary>Arguments for the <c>add-custom-logging</c> MCP tool.</summary>
public sealed record AddCustomLoggingArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered local Creatio environment with EnvironmentPath configured")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Package whose generated Constants.LoggerName is routed")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("min-level")]
	[property: Description("Minimum NLog level; defaults to Info")]
	string MinLevel = null,

	[property: JsonPropertyName("file-name")]
	[property: Description("Optional simple file name beneath TodayLogPath; .log is appended when omitted")]
	string FileName = null,

	[property: JsonPropertyName("restart-environment")]
	[property: Description("Restart Creatio after configuring logging; defaults to false")]
	bool RestartEnvironment = false);
