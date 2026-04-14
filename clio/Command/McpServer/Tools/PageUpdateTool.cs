using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageUpdateTool(
	PageUpdateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<PageUpdateOptions>(command, logger, commandResolver) {

	internal const string ToolName = "update-page";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Update Freedom UI page schema body")]
	public PageUpdateResponse UpdatePage([Description("Parameters: schema-name, body (required); resources, dry-run, environment-name, uri, login, password (optional)")] [Required] PageUpdateArgs args) {
		PageUpdateOptions options = new() {
			SchemaName = args.SchemaName,
			Body = args.Body,
			DryRun = args.DryRun ?? false,
			Resources = args.Resources,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		lock (CommandExecutionSyncRoot) {
			PageUpdateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageUpdateCommand>(options);
			} catch (Exception ex) {
				return new PageUpdateResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryUpdatePage(options, out PageUpdateResponse response);
			return response;
		}
	}
}

public sealed record PageUpdateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Freedom UI page schema name, e.g. 'UsrMyApp_FormPage'")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("body")]
	[property: Description("Full JavaScript page body with markers. Reuse get-page raw.body.")]
	[property: Required]
	string Body,

	[property: JsonPropertyName("resources")]
	[property: Description("JSON object string of resource key-value pairs for #ResourceString(key)# macros in the body, e.g. '{\"UsrDetailsTab_caption\": \"Details\"}'. Unresolved macros are auto-registered with captions derived from key names.")]
	string? Resources,

	[property: JsonPropertyName("dry-run")]
	[property: Description("If true, validate without saving. Default: false")]
	bool? DryRun,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'")]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")] string? Uri,
	[property: JsonPropertyName("login")] string? Login,
	[property: JsonPropertyName("password")] string? Password
);
