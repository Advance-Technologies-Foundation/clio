using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageListTool(
	PageListCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<PageListOptions>(command, logger, commandResolver) {

	internal const string ToolName = "page-list";
	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["app-code"] = "code",
		["appCode"] = "code",
		["packageName"] = "package-name",
		["searchPattern"] = "search-pattern",
		["environmentName"] = "environment-name"
	};

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("List Freedom UI pages in Creatio with package and parent schema context")]
	public PageListResponse ListPages([Description("Parameters: package-name, code, search-pattern, limit, environment-name (all optional)")] [Required] PageListArgs args) {
		string? legacyAliasError = GetLegacyAliasError(args);
		if (!string.IsNullOrWhiteSpace(legacyAliasError)) {
			return new PageListResponse { Success = false, Error = legacyAliasError };
		}
		if (!string.IsNullOrWhiteSpace(args.PackageName) && !string.IsNullOrWhiteSpace(args.Code)) {
			return new PageListResponse { Success = false, Error = "Provide either package-name or code, not both." };
		}
		PageListOptions options = new() {
			PackageName = args.PackageName,
			AppCode = args.Code,
			SearchPattern = args.SearchPattern,
			Limit = args.Limit ?? 50,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		lock (CommandExecutionSyncRoot) {
			PageListCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageListCommand>(options);
			} catch (Exception ex) {
				return new PageListResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryListPages(options, out PageListResponse response);
			return response;
		}
	}

	private static string? GetLegacyAliasError(PageListArgs args) {
		if (args.ExtensionData is null) {
			return null;
		}
		return LegacyAliases
			.Where(legacyAlias => args.ExtensionData.ContainsKey(legacyAlias.Key))
			.Select(legacyAlias => $"Use '{legacyAlias.Value}' instead of '{legacyAlias.Key}'.")
			.FirstOrDefault();
	}
}

public sealed record PageListArgs(
	[property: JsonPropertyName("package-name")]
	[property: Description("Filter by package name")]
	string? PackageName,

	[property: JsonPropertyName("code")]
	[property: Description("Filter by installed application code using its primary package")]
	string? Code,

	[property: JsonPropertyName("search-pattern")]
	[property: Description("Filter by schema name pattern, e.g. 'UsrMyApp*'")]
	string? SearchPattern,

	[property: JsonPropertyName("limit")]
	[property: Description("Maximum number of results. Default: 50")]
	int? Limit,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'")]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")] string? Uri,
	[property: JsonPropertyName("login")] string? Login,
	[property: JsonPropertyName("password")] string? Password
) {
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
