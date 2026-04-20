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

	[McpServerTool(Name = "stop-creatio", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
	 Description("Stops Creatio instance by environment name")]
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

	[McpServerTool(Name = "stop-all-creatio", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
	 Description("Stops all Creatio instances")]
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

	/// <summary>
	/// Deprecated PascalCase alias preserved for backwards compatibility with clients
	/// that were configured against the original tool name. New clients should use
	/// <c>stop-all-creatio</c>.
	/// </summary>
	[McpServerTool(Name = "StopAllCreatio", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
	 Description("[Deprecated: use stop-all-creatio] Stops all Creatio instances")]
	public CommandExecutionResult StopAllCreatioLegacy(
		RequestContext<CallToolRequestParams> requestContext
	) => StopAllCreatio(requestContext);

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
