using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.Theming;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that refreshes the Creatio theme catalog cache on a target environment via the native <c>ThemeService</c>.
/// </summary>
[FeatureToggle("theming")]
public class ClearThemesCacheTool(
	ClearThemesCacheCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ClearThemesCacheOptions>(command, logger, commandResolver) {

	internal const string ClearThemesCacheByEnvironmentName = "clear-themes-cache-by-environment";
	internal const string ClearThemesCacheByCredentialsToolName = "clear-themes-cache-by-credentials";

	[McpServerTool(Name = ClearThemesCacheByEnvironmentName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Refresh the Creatio theme cache for a registered environment. For the theme workflow, read get-guidance theming first.")]
	public CommandExecutionResult ClearThemesCacheByName(
		[Description("Target Environment name")] [Required] string environmentName
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}
		ClearThemesCacheOptions options = new() {
			Environment = environmentName,
			TimeOut = 30_000
		};
		return InternalExecute<ClearThemesCacheCommand>(options);
	}

	[McpServerTool(Name = ClearThemesCacheByCredentialsToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Refresh the Creatio theme cache using explicit credentials. For the theme workflow, read get-guidance theming first.")]
	public CommandExecutionResult ClearThemesCacheByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio instance Username")] [Required] string userName,
		[Description("Creatio instance Password")] [Required] string password,
		[DefaultValue(false)][Description("Specifies if creatio runtime is a NET8 or NET472, default: false")] bool isNetCore = false
	) {
		CommandExecutionResult validationError = CommandExecutionResult.ValidateCredentials(url, userName, password);
		if (validationError != null) {
			return validationError;
		}
		ClearThemesCacheOptions options = new() {
			Login = userName,
			Password = password,
			Uri = url,
			IsNetCore = isNetCore,
			TimeOut = 30_000
		};
		return InternalExecute<ClearThemesCacheCommand>(options);
	}
}
