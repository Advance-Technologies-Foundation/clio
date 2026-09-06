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
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Detects whether a registered Creatio environment is currently in FSM mode on or off. Use `set-fsm-mode` to activate or deactivate FSM mode when needed.")]
	public FsmModeStatusResult GetFsmMode(
		[Description(McpToolDescriptions.EnvironmentName)] [Required] string environmentName)
	{
		return fsmModeStatusService.GetStatus(environmentName);
	}

	/// <summary>
	/// Turns FSM mode on or off for a registered environment.
	/// </summary>
	[McpServerTool(Name = SetFsmModeToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Turns FSM mode on or off for a registered Creatio environment. The two directions fail differently: `on` writes the configuration first and then exports the packages, so a non-zero exit code can mean FSM is already enabled while the export did not happen - re-check with `get-fsm-mode` and finish with `pkg-to-file-system` instead of calling this tool again; `off` imports the packages first and only then writes the configuration, so a non-zero exit code means the configuration was NOT changed and the environment is still in FSM mode. An environment that already reports FSM as off is not an error for `off`. After changing FSM mode, run `compile-creatio` without `package-name` to perform a full compilation (`clio cc -e ENV_NAME --all`).")]
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
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	string EnvironmentName,

	[property: JsonPropertyName("mode")]
	[Description("Target FSM mode value: on or off")]
	[Required]
	string Mode);
