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
	
	// This tool and ClearRedisByCredentials below do the same thing and differ ONLY in how the target is
	// identified, so each description must name its own selector: the first sentence is what the
	// get-tool-contract compact index shows, and two identical one-liners leave an agent unable to pick.
	// See docs/knowledge/McpServer/first-sentence-of-a-description-becomes-the-compact-index-purpose.md
	[McpServerTool(Name = ClearRedisByEnvironmentName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Empties the redis database used by a creatio instance identified by its REGISTERED ENVIRONMENT NAME. Use clear-redis-db-by-credentials when the instance is not registered and you have its url and login instead.")]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	public CommandExecutionResult ClearRedisByName(
		[Description("Target Environment name")] [Required] string environmentName
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromValidationError("environment-name is required and cannot be empty.");
		}
		ClearRedisOptions options = new() {
			Environment = environmentName,
			TimeOut = 30_000
		};
		return InternalExecute<RedisCommand>(options);
	}

	[McpServerTool(Name = ClearRedisByCredentialsToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Empties the redis database used by a creatio instance identified by RAW CREDENTIALS - url, username and password. Use clear-redis-db-by-environment when the instance is already registered as a clio environment.")]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	public CommandExecutionResult ClearRedisByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio instance Username")] [Required] string userName,
		[Description("Creatio instance Password")] [Required] string password,
		[DefaultValue(false)][Description("Specifies if creatio runtime is a NET8 or NET472, default: false")] bool isNetCore = false
	) {
		CommandExecutionResult validationError = CommandExecutionResult.ValidateCredentials(url, userName, password);
		if (validationError != null) {
			return validationError;
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
