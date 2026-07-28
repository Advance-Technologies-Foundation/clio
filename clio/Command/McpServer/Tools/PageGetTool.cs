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
	IToolCommandResolver commandResolver,
	IPageFileWriter pageFileWriter)
	: BaseTool<PageGetOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-page";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Get a Freedom UI page. Writes body.js (editable own-body for update-page), bundle.json (full merged view; minified JSON — parse with jq/json, not grep), and meta.json to .clio-pages/{schema-name}/ and returns file paths. " +
		"Pass output-directory to anchor output at your project root. " +
		"BEFORE editing the body call get-guidance `page-modification` (or `mobile-page-modification` when meta.json shows schema-type == \"mobile\") — its pre-edit checklist routes visibility/lookup-filter work to business rules and other changes to the correct page-authoring guide.")]
	public PageGetResponse GetPage(
		[Description("schema-name (required); environment-name preferred; uri/login/password fallback only.")]
		[Required] PageGetArgs args) {
		PageGetOptions options = new() {
			SchemaName = args.SchemaName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(options, () => {
			PageGetCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageGetCommand>(options);
			} catch (Exception ex) {
				return new PageGetResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryGetPage(options, out PageGetResponse response);
			if (!response.Success) {
				return response;
			}
			PageGetResponse written = pageFileWriter.WritePageFiles(
				response, args.SchemaName, args.EnvironmentName, args.Uri, args.OutputDirectory);
			if (!written.Success) {
				return written;
			}
			// Compact the MCP envelope: the heavy bundle/raw payloads now live on disk
			// (bundle.json/body.js), so the tool returns metadata + file paths only — mirroring
			// the prior WriteFilesAndCompact behavior.
			return new PageGetResponse {
				Success = true,
				Page = written.Page,
				Editable = written.Editable,
				Files = written.Files
			};
		});
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

	[property: JsonPropertyName("output-directory")]
	[property: Description("Optional. Directory to anchor .clio-pages output under (typically your project root). Defaults to the auto-detected workspace root.")]
	string? OutputDirectory = null
);
