using System.ComponentModel;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that lists clio feature flags and toggles them on or off. The underlying
/// <see cref="ExperimentalCommand"/> operates on local clio settings only, so the tool is not
/// environment-sensitive and executes the injected command directly.
/// </summary>
[McpServerToolType]
public sealed class ExperimentalTool : BaseTool<ExperimentalOptions> {
	internal const string ToolName = "experimental";

	/// <summary>
	/// Initializes a new instance of the <see cref="ExperimentalTool"/> class.
	/// </summary>
	/// <param name="command">The experimental command that performs the list/toggle work.</param>
	/// <param name="logger">The logger used to capture command output for the MCP envelope.</param>
	public ExperimentalTool(ExperimentalCommand command, ILogger logger)
		: base(command, logger) {
	}

	/// <summary>
	/// Lists clio feature flags or enables/disables a single feature flag in local clio settings.
	/// </summary>
	/// <param name="name">The feature key to toggle. When omitted, all known feature flags are listed.</param>
	/// <param name="enable">When <c>true</c>, enables the feature named by <paramref name="name"/>.</param>
	/// <param name="disable">When <c>true</c>, disables the feature named by <paramref name="name"/>.</param>
	/// <returns>The structured command execution result.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Lists clio experimental feature flags, or enables/disables a single feature flag in local clio settings. Omit 'name' to list; pass 'name' plus exactly one of 'enable'/'disable' to toggle.")]
	public CommandExecutionResult Experimental(
		[Description("The feature key to enable or disable. Omit to list all known feature flags.")]
		string name = null,
		[Description("Set to true to enable the feature named by 'name'.")]
		bool enable = false,
		[Description("Set to true to disable the feature named by 'name'.")]
		bool disable = false) {
		ExperimentalOptions options = new() {
			Name = name,
			Enable = enable,
			Disable = disable,
		};
		return InternalExecute(options);
	}
}
