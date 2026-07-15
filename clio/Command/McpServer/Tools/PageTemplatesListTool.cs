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
	[Description("List Freedom UI page templates advertised by the target Creatio environment. Call this before create-page to discover valid `template` values. The web catalog always includes `BaseDashboardTemplate` (title `Dashboard`, groupName `DashboardPage`) — use it as the `template` for a dashboard — and `CentralAreaDesktopTemplate` (title `Desktop`, groupName `Desktop`) — use it as the `template` for a desktop (see get-guidance `desktop-page`). Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public PageTemplateListResponse ListPageTemplates(
		[Description("Optional schema-type filter ('web', 'mobile' or 'desktop'); environment-name preferred; uri/login/password emergency fallback only.")]
		PageTemplatesListArgs args) {
		args ??= new PageTemplatesListArgs(null, null, null, null, null);
		PageTemplatesListOptions options = new() {
			SchemaType = args.SchemaType,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(() => {
			// Validate the schema-type filter (a pure-input check) BEFORE resolving the environment so a
			// bad schema-type is reported as a schema-type error instead of being masked by an
			// environment-resolution failure (ENG-91825 env-validation order).
			if (!string.IsNullOrWhiteSpace(options.SchemaType) &&
				!PageTemplatesListCommand.TryParseTemplateFilter(options.SchemaType, out _, out _, out string schemaTypeError)) {
				return new PageTemplateListResponse { Success = false, Error = schemaTypeError };
			}

			PageTemplatesListCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageTemplatesListCommand>(options);
			} catch (Exception ex) {
				return new PageTemplateListResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryListTemplates(options, out PageTemplateListResponse response);
			return response;
		});
	}
}

public sealed record PageTemplatesListArgs(
	[property: JsonPropertyName("schema-type")]
	[property: Description("Optional schema-type filter: 'web' (Freedom UI page), 'mobile' (mobile page) or 'desktop' (web templates with group Desktop). Defaults to all.")]
	string? SchemaType,

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
	string? Password
);
