using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class RestartTool(
	RestartCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<RestartOptions>(command, logger, commandResolver) {

	internal const string RestartToolName = "restart-creatio";
	internal const string ModeEnvironment = "environment";
	internal const string ModeCredentials = "credentials";

		[Description("Restarts a Creatio instance. Use mode='environment' with environment-name to restart a registered environment; use mode='credentials' with url+login+password to restart by explicit credentials.")]
	public CommandExecutionResult Restart(
		[Description("Restart parameters")] [Required] RestartCreatioRunArgs args
	) {
		CommandExecutionResult validationError = CommandExecutionResult.ValidateEnvOrCredentialsMode(
			args.Mode, args.EnvironmentName, args.Url, args.Login, args.Password,
			ModeEnvironment, ModeCredentials);
		if (validationError != null) {
			return validationError;
		}

		RestartOptions options = string.Equals(args.Mode, ModeEnvironment, System.StringComparison.OrdinalIgnoreCase)
			? new RestartOptions {
				Environment = args.EnvironmentName,
				TimeOut = 30_000
			}
			: new RestartOptions {
				Login = args.Login,
				Password = args.Password,
				Uri = args.Url,
				IsNetCore = args.IsNetCore,
				TimeOut = 30_000
			};
		return InternalExecute<RestartCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the consolidated <c>restart-creatio</c> tool. Exactly one mode is active per call:
/// <c>environment</c> requires <c>environment-name</c>; <c>credentials</c> requires <c>url</c>, <c>login</c>,
/// and <c>password</c>. Compact single-attribute formatting keeps the per-tool record visually distinct
/// from sibling env/credentials args while preserving the same wire shape.
/// </summary>
public sealed record RestartCreatioRunArgs(
	[property: JsonPropertyName("mode"), Description("Discriminator: 'environment' uses a registered clio environment name; 'credentials' uses explicit url+login+password."), Required]
	string Mode,

	[property: JsonPropertyName("environment-name"), Description("Required when mode='environment'. Registered clio environment name.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("is-net-core"), Description("Optional. Set true for NET8 runtime; default false for NET472.")]
	bool IsNetCore = false,

	[property: JsonPropertyName("url"), Description("Required when mode='credentials'. Creatio instance URL.")]
	string? Url = null,

	[property: JsonPropertyName("login"), Description("Required when mode='credentials'. Creatio user login.")]
	string? Login = null,

	[property: JsonPropertyName("password"), Description("Required when mode='credentials'. Creatio user password.")]
	string? Password = null
) : ClioRunArgs;
