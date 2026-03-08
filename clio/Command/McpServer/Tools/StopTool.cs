using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class StopTool(
	StopCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	ModelContextProtocol.Server.McpServer server) : BaseTool<StopOptions>(command, logger, commandResolver) {

	private RequestContext<CallToolRequestParams> _requestContext;

	[McpServerTool(Name = "stop-creatio"), Description("Stops Creatio instance by environment name")]
	public CommandExecutionResult StopCreatioByName(
		RequestContext<CallToolRequestParams> requestContext,
		[Description("Target Environment name")] [Required] string environmentName
	) {
		_requestContext = requestContext;
		StopOptions options = new() {
			Environment = environmentName,
			IsSilent = true
		};
		return InternalExecute<StopCommand>(options, resolvedCommand => {
			resolvedCommand.StatusChanged += OnStatusChanged;
		});
	}

	[McpServerTool(Name = "StopAllCreatio"), Description("Stops all Creatio instances")]
	public CommandExecutionResult StopAllCreatio(
		RequestContext<CallToolRequestParams> requestContext
	) {
		_requestContext = requestContext;
		StopOptions options = new() {
			All = true,
			IsSilent = true
		};
		return InternalExecute(options);
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
