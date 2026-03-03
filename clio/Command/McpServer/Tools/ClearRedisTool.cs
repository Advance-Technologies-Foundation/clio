using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public class ClearRedisTool(RedisCommand command, ILogger logger){

	[McpServerTool(Name = "ClearRedisByEnvironmentName"), Description("Empties redis database used by creatio instance")]
	public CommandExecutionResult ClearRedisByName(
		[Description("Target Environment name")] [Required] string environmentName
	) {
		ClearRedisOptions options = new() {
			Environment = environmentName,
			TimeOut = 30_000 //Timeout in millisecond
		};
		CommandExecutionResult result = InternalExecute(options);
		logger.ClearMessages();
		return result;
	}
	
	[McpServerTool(Name = "ClearRedisByCredentials"), Description("Empties redis database used by creatio instance")]
	public CommandExecutionResult ClearRedisByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio instance Username")] [Required] string userName,
		[Description("Creatio instance Password")] [Required] string password,
		[DefaultValue(false)][Description("Specifies if creatio runtime is a NET8 or NET472, default: false")] bool isNetCore = false
	) {
		ClearRedisOptions options = new() {
			Login = userName,
			Password = password,
			Uri = url,
			IsNetCore = isNetCore,
			TimeOut = 30_000 //Timeout in millisecond
		};
		CommandExecutionResult result = InternalExecute(options);
		logger.ClearMessages();
		return result;
	}

	private CommandExecutionResult InternalExecute(ClearRedisOptions options) {

		int result = -1;
		try {
			result = command.Execute(options);
			Thread.Sleep(500);
			return new CommandExecutionResult(result, logger.LogMessages.ToList());
		}
		catch (Exception e) {
			List<LogMessage> logMessages = [.. logger.LogMessages, new ErrorMessage(e.Message)];
			return new CommandExecutionResult(result, logMessages);
		}
	}
}
