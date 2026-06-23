using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that overwrites an existing custom Creatio theme on a target environment via the native
/// <c>ThemeService</c> (a full overwrite by theme id).
/// </summary>
public class UpdateThemeTool(
	UpdateThemeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<UpdateThemeOptions>(command, logger, commandResolver) {

	internal const string UpdateThemeByEnvironmentName = "update-theme-by-environment";
	internal const string UpdateThemeByCredentialsToolName = "update-theme-by-credentials";

	[McpServerTool(Name = UpdateThemeByEnvironmentName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Overwrite an existing custom Creatio theme on a registered environment via the native ThemeService " +
		"(full overwrite by id; the package cannot be changed). For the theme workflow, read get-guidance theming first.")]
	public CommandExecutionResult UpdateThemeByName(
		[Description("Target Environment name")] [Required] string environmentName,
		[Description("Id of the existing theme to overwrite")] [Required] string id,
		[Description("Human-readable theme caption (max 250)")] [Required] string caption,
		[Description("CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100)")] [Required] string cssClassName,
		[Description("Inline theme CSS content (max 1 MiB)")] [Required] string cssContent
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(id)) {
			return CommandExecutionResult.FromError("id is required and cannot be empty.");
		}
		UpdateThemeOptions options = new() {
			Environment = environmentName,
			Id = id,
			Caption = caption,
			CssClassName = cssClassName,
			CssContent = cssContent,
			TimeOut = 30_000
		};
		return InternalExecute<UpdateThemeCommand>(options);
	}

	[McpServerTool(Name = UpdateThemeByCredentialsToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Overwrite an existing custom Creatio theme using explicit credentials (full overwrite by id). " +
		"For the theme workflow, read get-guidance theming first.")]
	public CommandExecutionResult UpdateThemeByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio instance Username")] [Required] string userName,
		[Description("Creatio instance Password")] [Required] string password,
		[Description("Id of the existing theme to overwrite")] [Required] string id,
		[Description("Human-readable theme caption (max 250)")] [Required] string caption,
		[Description("CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100)")] [Required] string cssClassName,
		[Description("Inline theme CSS content (max 1 MiB)")] [Required] string cssContent,
		[DefaultValue(false)][Description("Specifies if creatio runtime is a NET8 or NET472, default: false")] bool isNetCore = false
	) {
		CommandExecutionResult validationError = CommandExecutionResult.ValidateCredentials(url, userName, password);
		if (validationError != null) {
			return validationError;
		}
		if (string.IsNullOrWhiteSpace(id)) {
			return CommandExecutionResult.FromError("id is required and cannot be empty.");
		}
		UpdateThemeOptions options = new() {
			Login = userName,
			Password = password,
			Uri = url,
			IsNetCore = isNetCore,
			Id = id,
			Caption = caption,
			CssClassName = cssClassName,
			CssContent = cssContent,
			TimeOut = 30_000
		};
		return InternalExecute<UpdateThemeCommand>(options);
	}
}
