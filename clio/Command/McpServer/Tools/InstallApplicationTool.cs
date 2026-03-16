using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>install-application</c> command.
/// </summary>
[McpServerToolType]
public sealed class InstallApplicationTool(
	InstallApplicationCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<InstallApplicationOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for application installation.
	/// </summary>
	internal const string InstallApplicationToolName = "install-application";

	/// <summary>
	/// Installs an application package into the requested Creatio environment.
	/// </summary>
	[McpServerTool(Name = InstallApplicationToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Installs an application package into the specified Creatio environment.")]
	public CommandExecutionResult InstallApplication(
		[Description("install-application parameters")]
		[Required]
		InstallApplicationArgs args) {
		InstallApplicationOptions options = new() {
			Name = args.Name,
			ReportPath = args.ReportPath,
			CheckCompilationErrors = args.CheckCompilationErrors,
			Environment = args.EnvironmentName
		};
		try {
			return InternalExecute<InstallApplicationCommand>(options);
		}
		catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(exception.Message)]);
		}
	}
}

/// <summary>
/// MCP arguments for the <c>install-application</c> tool.
/// </summary>
public sealed record InstallApplicationArgs(
	[property: JsonPropertyName("name")]
	[property: Description("Absolute path to the package to install as application")]
	[property: Required]
	string Name,

	[property: JsonPropertyName("report-path")]
	[property: Description("Optional local path where the install report should be written")]
	string? ReportPath,

	[property: JsonPropertyName("check-compilation-errors")]
	[property: Description("Optional flag that enables compilation-error checking during installation")]
	bool? CheckCompilationErrors,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name")]
	[property: Required]
	string EnvironmentName
);
