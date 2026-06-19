using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for fast installed-application discovery — returns matching applications
/// together with their sections in a single call.
/// </summary>
public sealed class FindAppTool(
	FindAppCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<FindAppOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for finding installed applications and their sections.
	/// </summary>
	internal const string FindAppToolName = "find-app";

	/// <summary>
	/// Finds installed applications (and their sections) by name, code, or substring pattern.
	/// </summary>
	[McpServerTool(Name = FindAppToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description(
		"Finds installed Creatio applications AND their sections in one call, matching a case-insensitive substring " +
		"against application name/code/description and section captions/codes — use it to resolve an imprecise app name to its real code. " +
		"Omit both filters to enumerate every application with its sections. Returned codes feed get-app-info, list-app-sections, create-app-section.")]
	public FindAppResponse FindApp(
		[Description("environment-name (required); search-pattern (optional substring), code (optional exact app code). Omit both to list all apps with sections.")]
		[Required]
		FindAppArgs args) {
		try {
			FindAppOptions options = new() {
				Environment = args.EnvironmentName,
				SearchPattern = args.SearchPattern,
				Code = args.Code
			};
			FindAppCommand resolvedCommand = ResolveCommand<FindAppCommand>(options);
			IReadOnlyList<AppSearchResult> results = resolvedCommand.FindApplications(options);
			return new FindAppResponse(true, results, null);
		} catch (Exception exception) {
			return new FindAppResponse(false, null, exception.Message);
		}
	}
}

/// <summary>
/// Arguments for the <c>find-app</c> MCP tool.
/// </summary>
public sealed record FindAppArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("search-pattern")]
	[property: Description("Case-insensitive substring matched against application name, code, description, and section captions/codes. Omit to return all applications.")]
	string? SearchPattern = null,

	[property: JsonPropertyName("code")]
	[property: Description("Exact installed application code to match. Optional.")]
	string? Code = null);

/// <summary>
/// Structured envelope returned by the <c>find-app</c> MCP tool.
/// </summary>
public sealed record FindAppResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("applications")] IReadOnlyList<AppSearchResult>? Applications = null,
	[property: JsonPropertyName("error")] string? Error = null);
