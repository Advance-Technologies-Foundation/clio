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
	public PageListResponse ListPages([Description("Parameters: packageName, searchPattern, limit, environmentName (all optional)")] [Required] PageListArgs args) {
		PageListOptions options = new() {
			PackageName = args.PackageName,
			SearchPattern = args.SearchPattern,
			Limit = args.Limit ?? 50,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		PageListCommand resolvedCommand = ResolveCommand<PageListCommand>(options);
		resolvedCommand.TryListPages(options, out PageListResponse response);
		return response;
	}
}

public sealed record PageListArgs(
	[property: JsonPropertyName("packageName")] string? PackageName,
	[property: JsonPropertyName("searchPattern")] string? SearchPattern,
	[property: JsonPropertyName("limit")] int? Limit,
	[property: JsonPropertyName("environmentName")] string? EnvironmentName,
	[property: JsonPropertyName("uri")] string? Uri,
	[property: JsonPropertyName("login")] string? Login,
	[property: JsonPropertyName("password")] string? Password
);
