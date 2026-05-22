using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class ClearRedisTool(
	RedisCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ClearRedisOptions>(command, logger, commandResolver) {

	internal const string ClearRedisToolName = "clear-redis-db";
	internal const string ModeEnvironment = "environment";
	internal const string ModeCredentials = "credentials";

	[McpServerTool(Name = ClearRedisToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Empties the Redis database used by a Creatio instance. Use mode='environment' with environment-name to target a registered environment; use mode='credentials' with url+login+password.")]
	public CommandExecutionResult ClearRedis(
		[Description("Clear-redis parameters")] [Required] ClearRedisDbArgs args
	) {
		CommandExecutionResult modeError = CommandExecutionResult.ValidateExactlyOneMode(
			"mode", args.Mode, ModeEnvironment, ModeCredentials);
		if (modeError != null) {
			return modeError;
		}

		if (string.Equals(args.Mode, ModeEnvironment, StringComparison.OrdinalIgnoreCase)) {
			CommandExecutionResult missing = CommandExecutionResult.ValidateRequiredForMode(
				"environment-name", args.EnvironmentName, ModeEnvironment);
			if (missing != null) {
				return missing;
			}
			ClearRedisOptions options = new() {
				Environment = args.EnvironmentName,
				TimeOut = 30_000
			};
			return InternalExecute<RedisCommand>(options);
		}

		CommandExecutionResult credentialsError = CommandExecutionResult.ValidateCredentials(
			args.Url, args.Login, args.Password);
		if (credentialsError != null) {
			return credentialsError;
		}
		ClearRedisOptions credentialsOptions = new() {
			Login = args.Login,
			Password = args.Password,
			Uri = args.Url,
			IsNetCore = args.IsNetCore,
			TimeOut = 30_000
		};
		return InternalExecute<RedisCommand>(credentialsOptions);
	}
}

/// <summary>
/// MCP arguments for the consolidated <c>clear-redis-db</c> tool. Exactly one mode is active per call:
/// <c>environment</c> requires <c>environment-name</c>; <c>credentials</c> requires <c>url</c>, <c>login</c>,
/// and <c>password</c>.
/// </summary>
public sealed record ClearRedisDbArgs(
	[property: JsonPropertyName("mode")]
	[property: Description("Discriminator: 'environment' uses a registered clio environment name; 'credentials' uses explicit url+login+password.")]
	[property: Required]
	string Mode,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Required when mode='environment'. Registered clio environment name.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("url")]
	[property: Description("Required when mode='credentials'. Creatio instance URL.")]
	string? Url = null,

	[property: JsonPropertyName("login")]
	[property: Description("Required when mode='credentials'. Creatio user login.")]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description("Required when mode='credentials'. Creatio user password.")]
	string? Password = null,

	[property: JsonPropertyName("is-net-core")]
	[property: Description("Optional. Set true for NET8 runtime; default false for NET472.")]
	bool IsNetCore = false
);
