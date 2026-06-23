using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that deletes a custom Creatio theme from a target environment via the native <c>ThemeService</c>.
/// Destructive and not idempotent: deleting an unknown id is reported as a failure.
/// </summary>
public class DeleteThemeTool(
	DeleteThemeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<DeleteThemeOptions>(command, logger, commandResolver) {

	internal const string DeleteThemeByEnvironmentName = "delete-theme-by-environment";
	internal const string DeleteThemeByCredentialsToolName = "delete-theme-by-credentials";

	[McpServerTool(Name = DeleteThemeByEnvironmentName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Delete a custom Creatio theme from a registered environment via the native ThemeService. " +
		"Deleting an unknown id is an error (not idempotent). For the theme workflow, read get-guidance theming first.")]
	public CommandExecutionResult DeleteThemeByName(
		[Description("Target Environment name")] [Required] string environmentName,
		[Description("Id of the theme to delete")] [Required] string id
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(id)) {
			return CommandExecutionResult.FromError("id is required and cannot be empty.");
		}
		DeleteThemeOptions options = new() {
			Environment = environmentName,
			Id = id,
			TimeOut = 30_000
		};
		return InternalExecute<DeleteThemeCommand>(options);
	}

	[McpServerTool(Name = DeleteThemeByCredentialsToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Delete a custom Creatio theme using explicit credentials. Deleting an unknown id is an error (not idempotent). " +
		"For the theme workflow, read get-guidance theming first.")]
	public CommandExecutionResult DeleteThemeByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio instance Username")] [Required] string userName,
		[Description("Creatio instance Password")] [Required] string password,
		[Description("Id of the theme to delete")] [Required] string id,
		[DefaultValue(false)][Description("Specifies if creatio runtime is a NET8 or NET472, default: false")] bool isNetCore = false
	) {
		CommandExecutionResult validationError = CommandExecutionResult.ValidateCredentials(url, userName, password);
		if (validationError != null) {
			return validationError;
		}
		if (string.IsNullOrWhiteSpace(id)) {
			return CommandExecutionResult.FromError("id is required and cannot be empty.");
		}
		DeleteThemeOptions options = new() {
			Login = userName,
			Password = password,
			Uri = url,
			IsNetCore = isNetCore,
			Id = id,
			TimeOut = 30_000
		};
		return InternalExecute<DeleteThemeCommand>(options);
	}
}
