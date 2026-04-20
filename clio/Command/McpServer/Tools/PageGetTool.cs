using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageGetTool(
	PageGetCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<PageGetOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-page";

	/// <summary>
	/// Reads a Freedom UI page as a merged bundle plus raw editable body.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Get a Freedom UI page bundle plus raw schema body. Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows. " +
		"Before editing the returned raw.body: " +
		"if the task targets SCHEMA_VALIDATORS read docs://mcp/guides/page-schema-validators first; " +
		"if the task targets SCHEMA_CONVERTERS read docs://mcp/guides/page-schema-converters; " +
		"if the task targets SCHEMA_HANDLERS read docs://mcp/guides/page-schema-handlers.")]
	public PageGetResponse GetPage([Description("Parameters: schema-name (required); environment-name preferred; uri/login/password emergency fallback only.")] [Required] PageGetArgs args) {
		PageGetOptions options = new() {
			SchemaName = args.SchemaName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		lock (CommandExecutionSyncRoot) {
			PageGetCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageGetCommand>(options);
			} catch (Exception ex) {
				return new PageGetResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryGetPage(options, out PageGetResponse response);
			return response;
		}
	}
}

/// <summary>
/// Arguments for the <c>get-page</c> MCP tool.
/// </summary>
public sealed record PageGetArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Freedom UI page schema name, e.g. 'UsrMyApp_FormPage'")]
	[property: Required]
	string SchemaName,

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
