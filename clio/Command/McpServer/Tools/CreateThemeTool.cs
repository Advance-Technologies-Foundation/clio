using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that creates a custom Creatio theme on a target environment via the native <c>ThemeService</c>,
/// returning a structured result with the theme id.
/// </summary>
public class CreateThemeTool(
	CreateThemeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<CreateThemeOptions>(command, logger, commandResolver) {

	internal const string CreateThemeByEnvironmentName = "create-theme-by-environment";
	internal const string CreateThemeByCredentialsToolName = "create-theme-by-credentials";

	[McpServerTool(Name = CreateThemeByEnvironmentName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
	 Description("Create a custom Creatio theme on a registered environment via the native ThemeService. " +
		"Returns { success, id, error? } where id is the created theme's id, auto-generated when omitted. " +
		"For the theme workflow, read get-guidance theming first.")]
	public CreateThemeResult CreateThemeByName(
		[Description("Target Environment name")] [Required] string environmentName,
		[Description("CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100)")] [Required] string cssClassName,
		[Description("Inline theme CSS content (max 1 MiB)")] [Required] string cssContent,
		[Description("Human-readable theme caption (max 250); derived from cssClassName when omitted")] string caption = null,
		[Description("Theme id (^[A-Za-z0-9_-]+$, max 100); an auto-generated UUID is used and returned when omitted")] string id = null,
		[Description("Owning package name; the environment's CurrentPackageId system setting is used when omitted")] string packageName = null
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CreateThemeResult.Failure("environment-name is required and cannot be empty.");
		}
		CreateThemeOptions options = new() {
			Environment = environmentName,
			Caption = caption,
			CssClassName = cssClassName,
			CssContent = cssContent,
			Id = id,
			PackageName = packageName,
			TimeOut = 30_000
		};
		return Execute(options);
	}

	[McpServerTool(Name = CreateThemeByCredentialsToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
	 Description("Create a custom Creatio theme using explicit credentials. Returns { success, id, error? }. " +
		"For the theme workflow, read get-guidance theming first.")]
	public CreateThemeResult CreateThemeByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio instance Username")] [Required] string userName,
		[Description("Creatio instance Password")] [Required] string password,
		[Description("CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100)")] [Required] string cssClassName,
		[Description("Inline theme CSS content (max 1 MiB)")] [Required] string cssContent,
		[Description("Human-readable theme caption (max 250); derived from cssClassName when omitted")] string caption = null,
		[Description("Theme id (^[A-Za-z0-9_-]+$, max 100); an auto-generated UUID is used and returned when omitted")] string id = null,
		[Description("Owning package name; the environment's CurrentPackageId system setting is used when omitted")] string packageName = null,
		[DefaultValue(false)][Description("Specifies if creatio runtime is a NET8 or NET472, default: false")] bool isNetCore = false
	) {
		if (string.IsNullOrWhiteSpace(url)) {
			return CreateThemeResult.Failure("url is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(userName)) {
			return CreateThemeResult.Failure("userName is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(password)) {
			return CreateThemeResult.Failure("password is required and cannot be empty.");
		}
		CreateThemeOptions options = new() {
			Login = userName,
			Password = password,
			Uri = url,
			IsNetCore = isNetCore,
			Caption = caption,
			CssClassName = cssClassName,
			CssContent = cssContent,
			Id = id,
			PackageName = packageName,
			TimeOut = 30_000
		};
		return Execute(options);
	}

	private CreateThemeResult Execute(CreateThemeOptions options) {
		return ExecuteWithCleanLog(() => {
			CreateThemeCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<CreateThemeCommand>(options);
			}
			catch (Exception ex) {
				return CreateThemeResult.Failure(ex.Message);
			}
			try {
				return resolvedCommand.TryCreateTheme(options, out string createdId, out string errorMessage)
					? CreateThemeResult.Successful(createdId)
					: CreateThemeResult.Failure(string.IsNullOrWhiteSpace(errorMessage)
						? "CreateTheme returned success=false."
						: errorMessage);
			}
			catch (Exception ex) {
				return CreateThemeResult.Failure(ex.Message);
			}
		});
	}
}

/// <summary>
/// Structured result of the <c>create-theme</c> MCP tools.
/// </summary>
public sealed record CreateThemeResult {
	/// <summary>Whether the theme was created.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The effective theme id (supplied or auto-generated); omitted on failure.</summary>
	[JsonPropertyName("id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Id { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result carrying the effective theme id.</summary>
	public static CreateThemeResult Successful(string id) => new() {
		Success = true,
		Id = id
	};

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static CreateThemeResult Failure(string error) => new() {
		Success = false,
		Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
	};
}
