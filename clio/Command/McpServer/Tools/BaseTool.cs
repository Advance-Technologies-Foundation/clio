using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clio.Common;
using ModelContextProtocol.Server;


namespace Clio.Command.McpServer.Tools;


[McpServerToolType]
public abstract class BaseTool<T>(Command<T> command, ILogger logger){
	private protected virtual CommandExecutionResult InternalExecute(Command<T> command, T options) {
		int result = -1;
		try {
			result = command.Execute(options);
			Thread.Sleep(500);
			CommandExecutionResult returnResult = new(result, [..logger.LogMessages.ToList()]);
			logger.ClearMessages();
			return returnResult;
		}
		catch (Exception e) {
			List<LogMessage> logMessages = [.. logger.LogMessages, new ErrorMessage(e.Message)];
			CommandExecutionResult returnResult =  new(result, logMessages);
			logger.ClearMessages();
			return returnResult;
		}
	}
}
