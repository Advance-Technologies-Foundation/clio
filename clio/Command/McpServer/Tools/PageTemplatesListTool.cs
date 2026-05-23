using System;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageTemplatesListTool(
	PageTemplatesListCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<PageTemplatesListOptions>(command, logger, commandResolver) {

	internal const string ToolName = "list-page-templates";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("List Freedom UI page templates advertised by the target Creatio environment. Call this before create-page to discover valid `template` values. Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public PageTemplateListResponse ListPageTemplates(
		[Description("Optional schema-type filter ('web' or 'mobile'); environment-name preferred; uri/login/password emergency fallback only.")]
		PageTemplatesListArgs args) {
		args ??= new PageTemplatesListArgs(null, null, null, null, null);
		PageTemplatesListOptions options = new() {
			SchemaType = args.SchemaType,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		PageTemplateListResponse response;
		lock (CommandExecutionSyncRoot) {
			PageTemplatesListCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageTemplatesListCommand>(options);
			} catch (Exception ex) {
				return new PageTemplateListResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryListTemplates(options, out response);
		}
		return response;
	}
}

public sealed record PageTemplatesListArgs(
	[property: JsonPropertyName("schema-type")]
	[property: Description("Optional schema-type filter: 'web' (Freedom UI page) or 'mobile' (mobile page). Defaults to all.")]
	string? SchemaType,

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
);
