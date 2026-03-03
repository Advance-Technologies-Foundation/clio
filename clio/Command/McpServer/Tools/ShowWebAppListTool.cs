using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public class ShowWebAppListTool(ShowAppListCommand command, ILogger logger){

	[McpServerTool(Name = "ShowWebAppList"), Description("Show the list of web applications (Creatrio environments) and their settings")]
	public CommandExecutionResult ShowWebAppList() {
		AppListOptions options = new() {
			Format = "raw"
		};
		CommandExecutionResult result = InternalExecute(options);
		logger.ClearMessages();
		return result;
	}
	
	private CommandExecutionResult InternalExecute(AppListOptions options) {
		int result = -1;
		try {
			result = command.Execute(options);
			Thread.Sleep(500);
			return new CommandExecutionResult(result, [..logger.LogMessages.ToList()]);
		}
		catch (Exception e) {
			List<LogMessage> logMessages = [.. logger.LogMessages, new ErrorMessage(e.Message)];
			return new CommandExecutionResult(result, logMessages);
		}
	}
}
