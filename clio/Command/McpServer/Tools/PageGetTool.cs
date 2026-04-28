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
	[Description(
		"Get a Freedom UI page. Writes body.js / bundle.json / meta.json to .clio-pages/{schema-name}/ in the working directory and returns file paths. " +
		"body.js contains the EDITABLE own-body of the replacing schema in the design package (empty template when no replacing schema exists yet) — this is what update-page should receive. " +
		"bundle.json contains the full merged view of the entire hierarchy and is the correct source for reading what components are on the page. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows. " +
		"Before editing the returned raw.body: " +
		"if the task targets SCHEMA_HANDLERS call get-guidance with name `page-schema-handlers` first; " +
		"if the task targets SCHEMA_VALIDATORS call get-guidance with name `page-schema-validators` first; " +
		"if the task adds or edits `@creatio-devkit/common` usage call get-guidance with name `page-schema-sdk-common` before editing SCHEMA_DEPS or SDK calls.")]
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
		string rootDir = fileSystem.Path.Combine(
			fileSystem.Directory.GetCurrentDirectory(), ".clio-pages");
		string schemaDir = fileSystem.Path.Combine(rootDir, schemaName);
		try {
			if (fileSystem.Directory.Exists(schemaDir)) {
				fileSystem.Directory.Delete(schemaDir, recursive: true);
			}
			fileSystem.Directory.CreateDirectory(schemaDir);
			EnsureGitIgnoreEntry(rootDir);
		} catch (Exception ex) {
			return new PageGetResponse { Success = false, Error = $"Failed to prepare output directory '{schemaDir}': {ex.Message}" };
		}
		string bodyFile   = fileSystem.Path.Combine(schemaDir, "body.js");
		string bundleFile = fileSystem.Path.Combine(schemaDir, "bundle.json");
		string metaFile   = fileSystem.Path.Combine(schemaDir, "meta.json");
		string fetchedAt = DateTime.UtcNow.ToString("o");
		try {
			fileSystem.File.WriteAllText(bodyFile,   response.Raw.Body);
			fileSystem.File.WriteAllText(bundleFile, System.Text.Json.JsonSerializer.Serialize(response.Bundle));
			fileSystem.File.WriteAllText(metaFile,   System.Text.Json.JsonSerializer.Serialize(new {
				fetchedAt,
				page = response.Page
			}));
		} catch (Exception ex) {
			return new PageGetResponse { Success = false, Error = $"Failed to write page files: {ex.Message}" };
		}
		return new PageGetResponse {
			Success = true,
			Page = response.Page,
			Files = new PageGetFilesInfo {
				BodyFile = bodyFile,
				BundleFile = bundleFile,
				MetaFile = metaFile,
				FetchedAt = fetchedAt
			}
		};
	}

	private void EnsureGitIgnoreEntry(string rootDir) {
		try {
			if (!fileSystem.Directory.Exists(rootDir)) {
				fileSystem.Directory.CreateDirectory(rootDir);
			}
			string gitignorePath = fileSystem.Path.Combine(rootDir, ".gitignore");
			if (!fileSystem.File.Exists(gitignorePath)) {
				fileSystem.File.WriteAllText(gitignorePath, "*\n!.gitignore\n");
			}
		} catch {
			// ignore — gitignore is best-effort hygiene; never block a successful get-page.
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
