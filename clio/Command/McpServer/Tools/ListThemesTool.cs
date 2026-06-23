using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Read-only MCP tool that lists the custom Creatio themes available on a target environment, returning them
/// as a structured result.
/// </summary>
public class ListThemesTool(
	ListThemesCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ListThemesOptions>(command, logger, commandResolver) {

	internal const string ListThemesByEnvironmentName = "list-themes-by-environment";
	internal const string ListThemesByCredentialsToolName = "list-themes-by-credentials";

	[McpServerTool(Name = ListThemesByEnvironmentName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("List the custom Creatio themes available on a registered environment. " +
		"Returns { success, themes:[{ id, caption, cssClassName, cssFilePath }] }. " +
		"An empty themes array means the catalog is empty or the caller lacks the CanCustomizeBranding license. " +
		"For the theme workflow, read get-guidance theming first.")]
	public ListThemesResult ListThemesByName(
		[Description("Target Environment name")] [Required] string environmentName
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return ListThemesResult.Failure("environment-name is required and cannot be empty.");
		}
		ListThemesOptions options = new() {
			Environment = environmentName,
			TimeOut = 30_000
		};
		return Execute(options);
	}

	[McpServerTool(Name = ListThemesByCredentialsToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("List the custom Creatio themes available using explicit credentials. " +
		"Returns { success, themes:[{ id, caption, cssClassName, cssFilePath }] }. For the theme workflow, read get-guidance theming first.")]
	public ListThemesResult ListThemesByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio instance Username")] [Required] string userName,
		[Description("Creatio instance Password")] [Required] string password,
		[DefaultValue(false)][Description("Specifies if creatio runtime is a NET8 or NET472, default: false")] bool isNetCore = false
	) {
		if (string.IsNullOrWhiteSpace(url)) {
			return ListThemesResult.Failure("url is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(userName)) {
			return ListThemesResult.Failure("userName is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(password)) {
			return ListThemesResult.Failure("password is required and cannot be empty.");
		}
		ListThemesOptions options = new() {
			Login = userName,
			Password = password,
			Uri = url,
			IsNetCore = isNetCore,
			TimeOut = 30_000
		};
		return Execute(options);
	}

	private ListThemesResult Execute(ListThemesOptions options) {
		return ExecuteWithCleanLog(() => {
			ListThemesCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<ListThemesCommand>(options);
			}
			catch (Exception ex) {
				return ListThemesResult.Failure(ex.Message);
			}
			try {
				if (!resolvedCommand.TryGetAvailableThemes(options,
						out IReadOnlyList<ThemeDescriptor> themes, out string errorMessage)) {
					return ListThemesResult.Failure(string.IsNullOrWhiteSpace(errorMessage)
						? "GetAvailableThemes returned success=false."
						: errorMessage);
				}
				return ListThemesResult.Successful(themes);
			}
			catch (Exception ex) {
				return ListThemesResult.Failure(ex.Message);
			}
		});
	}
}

/// <summary>
/// Structured result of the <c>list-themes</c> MCP tools.
/// </summary>
public sealed record ListThemesResult {
	/// <summary>Whether the theme catalog was read successfully.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The available themes; empty when none exist or the caller is unlicensed. Omitted on failure.</summary>
	[JsonPropertyName("themes")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<ThemeDescriptorResult> Themes { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result carrying the resolved theme catalog.</summary>
	public static ListThemesResult Successful(IReadOnlyList<ThemeDescriptor> themes) => new() {
		Success = true,
		Themes = (themes ?? Array.Empty<ThemeDescriptor>())
			.Select(theme => new ThemeDescriptorResult(theme.Id, theme.Caption, theme.CssClassName, theme.CssFilePath))
			.ToList()
	};

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static ListThemesResult Failure(string error) => new() {
		Success = false,
		Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
	};
}

/// <summary>
/// A single theme entry returned by the <c>list-themes</c> MCP tools.
/// </summary>
public sealed record ThemeDescriptorResult(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("cssClassName")] string CssClassName,
	[property: JsonPropertyName("cssFilePath")] string CssFilePath);
