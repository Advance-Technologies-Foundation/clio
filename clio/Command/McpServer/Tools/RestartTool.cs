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

	[McpServerTool(Name = RestartToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Restarts a Creatio instance. Use mode='environment' with environment-name to restart a registered environment; use mode='credentials' with url+login+password to restart by explicit credentials.")]
	public CommandExecutionResult Restart(
		[Description("Restart parameters")] [Required] RestartCreatioArgs args
	) {
		CommandExecutionResult modeError = CommandExecutionResult.ValidateExactlyOneMode(
			"mode", args.Mode, ModeEnvironment, ModeCredentials);
		if (modeError != null) {
			return modeError;
		}

		if (string.Equals(args.Mode, ModeEnvironment, System.StringComparison.OrdinalIgnoreCase)) {
			CommandExecutionResult missing = CommandExecutionResult.ValidateRequiredForMode(
				"environment-name", args.EnvironmentName, ModeEnvironment);
			if (missing != null) {
				return missing;
			}
			RestartOptions options = new() {
				Environment = args.EnvironmentName,
				TimeOut = 30_000
			};
			return InternalExecute<RestartCommand>(options);
		}

		CommandExecutionResult credentialsError = CommandExecutionResult.ValidateCredentials(
			args.Url, args.Login, args.Password);
		if (credentialsError != null) {
			return credentialsError;
		}
		RestartOptions credentialsOptions = new() {
			Login = args.Login,
			Password = args.Password,
			Uri = args.Url,
			IsNetCore = args.IsNetCore,
			TimeOut = 30_000
		};
		return InternalExecute<RestartCommand>(credentialsOptions);
	}
}

/// <summary>
/// MCP arguments for the consolidated <c>restart-creatio</c> tool. Exactly one mode is active per call:
/// <c>environment</c> requires <c>environment-name</c>; <c>credentials</c> requires <c>url</c>, <c>login</c>,
/// and <c>password</c>.
/// </summary>
public sealed record RestartCreatioArgs(
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
