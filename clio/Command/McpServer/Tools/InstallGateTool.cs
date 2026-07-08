using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>install-gate</c> command.
/// </summary>
public sealed class InstallGateTool(
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<InstallGateOptions>(null, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for installing the cliogate package.
	/// </summary>
	internal const string InstallGateToolName = "install-gate";

	/// <summary>
	/// Installs (or updates) the bundled cliogate package into a registered Creatio environment.
	/// </summary>
	[McpServerTool(Name = InstallGateToolName, ReadOnly = false, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("""
				 Installs (or updates) the bundled cliogate package into a registered Creatio environment.

				 Run this when a gate-dependent tool (for example `restore-workspace`, `unlock-package`, or
				 `lock-package`) fails with "you need to install the cliogate package version ... or higher".
				 cliogate exposes the server-side API that workspace and package tooling depends on, so install
				 it once per freshly deployed instance before using those flows.
				 """)]
	public CommandExecutionResult InstallGate(
		[Description("Install-gate parameters")] [Required] InstallGateArgs args) {
		InstallGateOptions options = new() {
			Environment = args.EnvironmentName
		};
		return InternalExecute<InstallGateCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the <c>install-gate</c> tool.
/// </summary>
public sealed record InstallGateArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName
);
