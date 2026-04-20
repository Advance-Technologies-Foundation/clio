using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageGetTool(
	PageGetCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	IFileSystem fileSystem)
	: BaseTool<PageGetOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-page";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Get a Freedom UI page. Writes body.js / bundle.json / meta.json to .clio-pages/{schema-name}/ in the working directory and returns file paths. Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public PageGetResponse GetPage(
		[Description("Parameters: schema-name (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] PageGetArgs args) {
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
			if (response.Success) {
				return WriteFilesAndCompact(response, args.SchemaName);
			}
			return response;
		}
	}

	private PageGetResponse WriteFilesAndCompact(PageGetResponse response, string schemaName) {
		string schemaDir = fileSystem.Path.Combine(
			fileSystem.Directory.GetCurrentDirectory(), ".clio-pages", schemaName);
		try {
			fileSystem.Directory.CreateDirectory(schemaDir);
		} catch (Exception ex) {
			return new PageGetResponse { Success = false, Error = $"Failed to create output directory '{schemaDir}': {ex.Message}" };
		}
		string bodyFile   = fileSystem.Path.Combine(schemaDir, "body.js");
		string bundleFile = fileSystem.Path.Combine(schemaDir, "bundle.json");
		string metaFile   = fileSystem.Path.Combine(schemaDir, "meta.json");
		try {
			fileSystem.File.WriteAllText(bodyFile,   response.Raw.Body);
			fileSystem.File.WriteAllText(bundleFile, System.Text.Json.JsonSerializer.Serialize(response.Bundle));
			fileSystem.File.WriteAllText(metaFile,   System.Text.Json.JsonSerializer.Serialize(response.Page));
		} catch (Exception ex) {
			return new PageGetResponse { Success = false, Error = $"Failed to write page files: {ex.Message}" };
		}
		return new PageGetResponse {
			Success = true,
			Page = response.Page,
			Files = new PageGetFilesInfo { BodyFile = bodyFile, BundleFile = bundleFile, MetaFile = metaFile }
		};
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
