using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("List Freedom UI pages in Creatio")]
	public PageListResponse ListPages([Description("Parameters: package-name, search-pattern, limit, environment-name (all optional)")] [Required] PageListArgs args) {
		PageListOptions options = new() {
			PackageName = args.PackageName,
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
}

public sealed record PageListArgs(
	[property: JsonPropertyName("package-name")]
	[property: Description("Filter by package name")]
	string? PackageName,

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
);
