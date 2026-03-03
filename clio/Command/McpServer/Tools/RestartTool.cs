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
public class RestartTool(RestartCommand restartCommand, ILogger logger){

	[McpServerTool(Name = "RestartByEnvironmentName"), Description("Restarts Creatio instance by environment name")]
	public CommandExecutionResult RestartInstanceByName(
		[Description("Environment name to restart")] [Required] string environmentName
	) {
		RestartOptions options = new() {
			Environment = environmentName,
			TimeOut = 30_000 //Timeout in millisecond
		};
		return InternalExecute(options);
	}
	
	[McpServerTool(Name = "RestartByCredentials"), Description("Restarts Creatio instance by credentials")]
	public CommandExecutionResult RestartInstanceByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio instance Username")] [Required] string userName,
		[Description("Creatio instance Password")] [Required] string password,
		[DefaultValue(false)][Description("Specifies if creatio runtime is a NET8 or NET472, default: false")] bool isNetCore = false
	) {
		RestartOptions options = new() {
			Login = userName,
			Password = password,
			Uri = url,
			IsNetCore = isNetCore,
			TimeOut = 30_000 //Timeout in millisecond
		};
		return InternalExecute(options);
	}

	private CommandExecutionResult InternalExecute(RestartOptions options) {

		int result = -1;
		try {
			result = restartCommand.Execute(options);
			Thread.Sleep(500);
			return new CommandExecutionResult(result, logger.LogMessages.ToList());
		}
		catch (Exception e) {
			List<LogMessage> logMessages = [.. logger.LogMessages, new ErrorMessage(e.Message)];
			return new CommandExecutionResult(result, logMessages);
		}
		
	}
}
