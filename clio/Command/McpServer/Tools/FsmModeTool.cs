using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for querying and changing Creatio file system mode.
/// </summary>
[McpServerToolType]
public sealed class FsmModeTool(
	TurnFsmCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	IFsmModeStatusService fsmModeStatusService)
	: BaseTool<TurnFsmCommandOptions>(command, logger, commandResolver)
{
	/// <summary>
	/// Stable MCP tool name for querying the current FSM mode.
	/// </summary>
	internal const string GetFsmModeToolName = "get-fsm-mode";

	/// <summary>
	/// Stable MCP tool name for turning FSM mode on or off.
	/// </summary>
	internal const string SetFsmModeToolName = "set-fsm-mode";

	/// <summary>
	/// Gets the current FSM mode from the Creatio GetApplicationInfo endpoint.
	/// </summary>
	[McpServerTool(Name = GetFsmModeToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Detects whether a registered Creatio environment is currently in FSM mode on or off. Use `set-fsm-mode` to activate or deactivate FSM mode when needed.")]
	public FsmModeStatusResult GetFsmMode(
		[Description("Registered clio environment name")] [Required] string environmentName)
	{
		return fsmModeStatusService.GetStatus(environmentName);
	}

	/// <summary>
	/// Turns FSM mode on or off for a registered environment.
	/// </summary>
	[McpServerTool(Name = SetFsmModeToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Turns FSM mode on or off for a registered Creatio environment. After changing FSM mode, run `compile-creatio` without `package-name` to perform a full compilation (`clio cc -e ENV_NAME --all`).")]
	public CommandExecutionResult SetFsmMode(
		[Description("FSM mode parameters")] [Required] SetFsmModeArgs args)
	{
		TurnFsmCommandOptions options = new()
		{
			Environment = args.EnvironmentName,
			IsFsm = args.Mode
		};
		return InternalExecute<TurnFsmCommand>(options);
	}
}

/// <summary>
/// MCP arguments for turning FSM mode on or off.
/// </summary>
public sealed record SetFsmModeArgs(
	[property: JsonPropertyName("environment-name")]
	[Description("Registered clio environment name")]
	[Required]
	string EnvironmentName,

	[property: JsonPropertyName("mode")]
	[Description("Target FSM mode value: on or off")]
	[Required]
	string Mode);
