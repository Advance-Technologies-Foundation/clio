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
	[Description("List Freedom UI pages in Creatio with package and parent schema context. Results are capped (default 50); the response always reports total (full match count) and truncated so an incomplete result is observable. A negative limit is rejected. Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
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
			// When limit is omitted, pass zero so the command applies its default cap; an
			// explicit zero means the same. The command rejects a negative limit instead of
			// treating it as "disable the cap".
			Limit = args.Limit ?? 0,
			UId = args.UId,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(options, () => {
			PageListCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageListCommand>(options);
			} catch (Exception ex) {
				return new PageListResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryListPages(options, out PageListResponse response);
			return response;
		});
	}

	private static string? GetLegacyAliasError(PageListArgs args) {
		return McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, string.Empty,
			"Valid: package-name, code, search-pattern, limit, environment-name, uri, login, password.");
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
	[property: Description("Maximum number of results. Omit or pass 0 to use the default of 50. A negative limit is rejected (it must not disable the cap). The response always carries total and truncated so a capped result is observable.")]
	int? Limit,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")]
	[property: Description(McpToolDescriptions.Uri)]
	string? Uri,
	[property: JsonPropertyName("login")]
	[property: Description(McpToolDescriptions.Login)]
	string? Login,
	[property: JsonPropertyName("password")]
	[property: Description(McpToolDescriptions.Password)]
	string? Password,

	[property: JsonPropertyName("uid")]
	[property: Description("Filter by schema UId (exact match). Use to locate a specific page directly from its UId in a Creatio designer URL (#/PageDesigner/<pageUId>).")]
	string? UId = null
) {
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
