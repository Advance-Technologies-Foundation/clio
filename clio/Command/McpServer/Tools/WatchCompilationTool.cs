using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>watch-compilation</c> command.
/// </summary>
[McpServerToolType]
[FeatureToggle("watch-compilation")]
public sealed class WatchCompilationTool(
	WatchCompilationCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<WatchCompilationOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for observing Creatio compilation status.
	/// </summary>
	internal const string WatchCompilationToolName = "watch-compilation";

	/// <summary>
	/// Observes an already-running or about-to-run Creatio compilation started outside clio and
	/// reports when it settles. Never triggers a compile itself.
	/// </summary>
	[McpServerTool(Name = WatchCompilationToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Long-running, may block for several minutes (bounded by give-up-after-seconds); observes Creatio's compilation history to report when a compilation started OUTSIDE clio (Studio UI, another user/process, or an IIS recycle after a package install) has finished. Never triggers a compile - use compile-creatio for that. Exit codes: 0 settled successfully (or already idle), 1 finished with errors or an unconfirmed partial finish, 2 gave up waiting, 3 could not read compilation history at all.")]
	public CommandExecutionResult WatchCompilation(
		[Description("watch-compilation parameters")] [Required] WatchCompilationArgs args) {
		WatchCompilationOptions options = new() {
			Environment = args.EnvironmentName,
			GiveUpAfterSeconds = args.GiveUpAfterSeconds ?? 300
		};
		return InternalExecute<WatchCompilationCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the <c>watch-compilation</c> tool.
/// </summary>
public sealed record WatchCompilationArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("give-up-after-seconds")]
	[property: Description("Optional seconds to wait for the compilation to settle before giving up (exit code 2). Default: 300 (5 minutes).")]
	int? GiveUpAfterSeconds
);
