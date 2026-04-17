using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class RestartTool(
	RestartCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<RestartOptions>(command, logger, commandResolver) {

	[McpServerTool(Name = "restart-by-environment-name", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Restarts Creatio instance by environment name")]
	public CommandExecutionResult RestartInstanceByName(
		[Description("Target Environment name to restart")] [Required] string environmentName
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}
		RestartOptions options = new() {
			Environment = environmentName,
			TimeOut = 30_000
		};
		return InternalExecute<RestartCommand>(options);
	}

	[McpServerTool(Name = "restart-by-credentials", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Restarts Creatio instance by credentials")]
	public CommandExecutionResult RestartInstanceByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio instance Username")] [Required] string userName,
		[Description("Creatio instance Password")] [Required] string password,
		[DefaultValue(false)][Description("Specifies if creatio runtime is a NET8 or NET472, default: false")] bool isNetCore = false
	) {
		if (string.IsNullOrWhiteSpace(url)) {
			return CommandExecutionResult.FromError("url is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(userName)) {
			return CommandExecutionResult.FromError("userName is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(password)) {
			return CommandExecutionResult.FromError("password is required and cannot be empty.");
		}
		RestartOptions options = new() {
			Login = userName,
			Password = password,
			Uri = url,
			IsNetCore = isNetCore,
			TimeOut = 30_000
		};
		return InternalExecute<RestartCommand>(options);
	}
}
