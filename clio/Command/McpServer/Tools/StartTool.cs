using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class StartTool(
	StartCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	ModelContextProtocol.Server.McpServer server) : BaseTool<StartOptions>(command, logger, commandResolver) {

	private RequestContext<CallToolRequestParams> _requestContext;

	// RequiresClientRequests = Progress, not None: OnStatusChanged below forwards StartCommand.StatusChanged
	// as notifications/progress on the caller's progress token, so a half-duplex relay would drop them.
	// (The Stage 0 inventory listed this tool as none; it counted the McpProgressHeartbeat callers and missed
	// the tools that call server.SendNotificationAsync directly.)
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.Progress,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = "start-creatio", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Starts Creatio instance by environment name")]
	public CommandExecutionResult StartCreatioByName(
		RequestContext<CallToolRequestParams> requestContext,
		[Description("Target Environment name")] [Required] string environmentName
	) {
		_requestContext = requestContext;
		StartOptions options = new() {
			Environment = environmentName,
			IsSilent = true
		};
		return InternalExecute<StartCommand>(options, resolvedCommand => {
			resolvedCommand.StatusChanged += OnStatusChanged;
		});
	}

	private void OnStatusChanged(object sender, ProgressNotificationValue args) {
		ProgressToken? progressToken = _requestContext.Params?.ProgressToken;
		if(progressToken is null) {
			return;
		}
		server.SendNotificationAsync("notifications/progress", new ProgressNotificationParams {
			ProgressToken = progressToken.Value,
			Progress = args,
		}).GetAwaiter().GetResult();
	}
}
