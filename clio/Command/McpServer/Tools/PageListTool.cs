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

	internal const string ToolName = "list-pages";
	private const string SearchPatternParam = "search-pattern";

	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["app-code"] = "code",
		["appCode"] = "code",
		["packageName"] = "package-name",
		["searchPattern"] = SearchPatternParam,
		["search_pattern"] = SearchPatternParam,
		["nameFilter"] = SearchPatternParam,
		["name-filter"] = SearchPatternParam,
		["name_filter"] = SearchPatternParam,
		["pattern"] = SearchPatternParam,
		["name"] = SearchPatternParam,
		["pageName"] = SearchPatternParam,
		["page-name"] = SearchPatternParam,
		["schemaName"] = SearchPatternParam,
		["schema-name"] = SearchPatternParam,
		["environmentName"] = "environment-name"
	};

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("List Freedom UI pages in Creatio with package and parent schema context. Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public PageListResponse ListPages([Description("Parameters: package-name, code, search-pattern, limit (optional); environment-name preferred; uri/login/password emergency fallback only.")] [Required] PageListArgs args) {
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
		if (args.ExtensionData is null || args.ExtensionData.Count == 0) {
			return null;
		}
		List<string> mapped = [];
		List<string> unknown = [];
		foreach (string key in args.ExtensionData.Keys) {
			if (LegacyAliases.TryGetValue(key, out string? canonical)) {
				mapped.Add($"'{key}' -> '{canonical}'");
			} else {
				unknown.Add($"'{key}'");
			}
		}
		if (mapped.Count == 0 && unknown.Count == 0) {
			return null;
		}
		List<string> parts = [];
		if (mapped.Count > 0) {
			parts.Add("Rename: " + string.Join(", ", mapped));
		}
		if (unknown.Count > 0) {
			parts.Add("Unknown args: " + string.Join(", ", unknown)
				+ ". Valid: package-name, code, search-pattern, limit, environment-name, uri, login, password.");
		}
		return string.Join(" ", parts);
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
	[property: Description("Registered clio environment name, e.g. 'local'. Preferred for normal MCP work.")]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")]
	[property: Description("Direct Creatio URL. Use only when bootstrap is broken or before the environment can be registered through reg-web-app.")]
	string? Uri,
	[property: JsonPropertyName("login")]
	[property: Description("Direct Creatio login paired with `uri`. Emergency fallback only.")]
	string? Login,
	[property: JsonPropertyName("password")]
	[property: Description("Direct Creatio password paired with `uri`. Emergency fallback only.")]
	string? Password
) {
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
