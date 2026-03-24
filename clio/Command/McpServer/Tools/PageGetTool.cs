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

	internal const string ToolName = "page-get";

	/// <summary>
	/// Reads a Freedom UI page as a merged bundle plus raw editable body.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Get a Freedom UI page bundle plus raw schema body")]
	public PageGetResponse GetPage([Description("Parameters: schema-name (required); environment-name, uri, login, password (optional)")] [Required] PageGetArgs args) {
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
/// Arguments for the <c>page-get</c> MCP tool.
/// </summary>
public sealed record PageGetArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Freedom UI page schema name, e.g. 'UsrMyApp_FormPage'")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'")]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")] string? Uri,
	[property: JsonPropertyName("login")] string? Login,
	[property: JsonPropertyName("password")] string? Password
);
