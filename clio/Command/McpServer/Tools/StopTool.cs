using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using Clio.Common;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public class StopTool (StopCommand command, ILogger logger, ModelContextProtocol.Server.McpServer server){
	
	private RequestContext<CallToolRequestParams> _requestContext;
	
	[McpServerTool(Name = "StopCreatio"), Description("Stops Creatio instance by environment name")]
	public CommandExecutionResult StopCreatioByName(
		RequestContext<CallToolRequestParams> requestContext,
		[Description("Target Environment name")] [Required] string environmentName
	) {
		_requestContext = requestContext;
		StopOptions options = new() {
			Environment = environmentName,
			IsSilent = true
		};
		CommandExecutionResult result = InternalExecute(options);
		logger.ClearMessages();
		return result;
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
		CommandExecutionResult result = InternalExecute(options);
		logger.ClearMessages();
		return result;
	}
	
	private CommandExecutionResult InternalExecute(StopOptions options) {

		int result = -1;
		try {
			command.StatusChanged += OnStatusChanged;
			
			result = command.Execute(options);
			Thread.Sleep(500);
			return new CommandExecutionResult(result, logger.LogMessages.ToList());
		}
		catch (Exception e) {
			List<LogMessage> logMessages = [.. logger.LogMessages, new ErrorMessage(e.Message)];
			return new CommandExecutionResult(result, logMessages);
		}
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
