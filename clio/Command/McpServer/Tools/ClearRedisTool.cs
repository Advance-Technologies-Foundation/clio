using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class ClearRedisTool(
	RedisCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ClearRedisOptions>(command, logger, commandResolver) {

	internal const string ClearRedisByCredentialsToolName = "clear-redis-db-by-credentials";
	internal const string ClearRedisByEnvironmentName = "clear-redis-db-by-environment";
	
	[McpServerTool(Name = ClearRedisByEnvironmentName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Empties redis database used by creatio instance")]
	public CommandExecutionResult ClearRedisByName(
		[Description("Target Environment name")] [Required] string environmentName
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}
		ClearRedisOptions options = new() {
			Environment = environmentName,
			TimeOut = 30_000
		};
		return InternalExecute<RedisCommand>(options);
	}

	[McpServerTool(Name = ClearRedisByCredentialsToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Empties redis database used by creatio instance")]
	public CommandExecutionResult ClearRedisByCredentials(
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
		ClearRedisOptions options = new() {
			Login = userName,
			Password = password,
			Uri = url,
			IsNetCore = isNetCore,
			TimeOut = 30_000
		};
		return InternalExecute<RedisCommand>(options);
	}
}
