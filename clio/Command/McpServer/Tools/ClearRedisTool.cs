using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class ClearRedisTool(
	RedisCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ClearRedisOptions>(command, logger, commandResolver) {

	[McpServerTool(Name = "ClearRedisByEnvironmentName"), Description("Empties redis database used by creatio instance")]
	public CommandExecutionResult ClearRedisByName(
		[Description("Target Environment name")] [Required] string environmentName
	) {
		ClearRedisOptions options = new() {
			Environment = environmentName,
			TimeOut = 30_000
		};
		return InternalExecute<RedisCommand>(options);
	}

	[McpServerTool(Name = "clear-redis"), Description("Empties redis database used by creatio instance")]
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
			TimeOut = 30_000
		};
		return InternalExecute<RedisCommand>(options);
	}
}
