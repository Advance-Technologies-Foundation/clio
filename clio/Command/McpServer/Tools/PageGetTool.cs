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
		"Get a Freedom UI page. Writes body.js / bundle.json / meta.json to .clio-pages/{schema-name}/ and returns file paths. " +
		"Output is anchored at the clio workspace root (the nearest ancestor containing .clio/workspaceSettings.json), or at an explicit `output-directory` when supplied. " +
		"When the server runs from the home directory and no workspace is found, output falls back to the managed clio home root instead of littering $HOME — pass `output-directory` (your project root) to keep page files next to your code. " +
		"body.js contains the EDITABLE own-body of the replacing schema in the design package (empty template when no replacing schema exists yet) — this is what update-page should receive. " +
		"bundle.json contains the full merged view of the entire hierarchy and is the correct source for reading what components are on the page. " +
		"IMPORTANT: bundle.json is a JSON document. Use a JSON parsing tool (jq, PowerShell ConvertFrom-Json, Python json.load) to navigate it; do NOT rely on grep or line-oriented text search — it is typically minified to a single line. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows. " +
		"MOBILE PAGES: If the returned meta.json shows schema-type == \"mobile\", this is a mobile page. " +
		"Call get-guidance with name `mobile-page-modification` BEFORE editing the body — mobile pages use plain JSON bodies (not AMD), require different components, and disallow handlers, validators, and custom converters. " +
		"Call get-guidance with name `page-modification` BEFORE editing the returned raw.body; use its pre-edit checklist to select any specialized page-authoring guides. " +
		"Before editing the returned raw.body: " +
		"if the task involves conditional visibility, editability, or required state — whether driven by a field value, the CURRENT USER, the current user's ROLES, or the current DATE/TIME (e.g. \"when Status=Closed, hide Description\", \"show this field only for administrators\", \"... only for the Supervisor contact\", \"... only on 2026-06-09\") — or conditional set/clear of a value, or filtering a lookup, use BUSINESS RULES, not handlers or validators. Role-, current-user-, and date-based visibility is a business rule (a CurrentUserRoles / CurrentUser / CurrentUserContact / CurrentUserAccount / CurrentDate condition), NOT a HandleViewModelInitRequest handler — a role/user/date check is a rule condition, not handler 'data access'. Call get-guidance with name `business-rules` to learn more; " +
		"this covers restricting which records a lookup/ComboBox offers by ANY constraint mechanism — an attribute value, a now-relative period (date macro), a fixed calendar/clock part such as a time of day (datePart), the existence or count of related child records, or a constraint gated by another field's value — classify the mechanism, not the wording; all of these are apply-static-filter, never a handler or crt.InitRequest. A gated constraint puts the gate (X = Y) into the rule's condition group with the apply-static-filter action on the target lookup; " +

		"if the task involves display-only value transformation (email as mailto link, phone as tel link, text to uppercase, boolean inversion, number formatting, any value that should look different on screen without changing the underlying model) call get-guidance with name `page-schema-converters` first — this determines whether a converter is the right tool before touching any component type; " +
		"if the task targets SCHEMA_HANDLERS call get-guidance with name `page-schema-handlers` first — NOTE: restricting which records a lookup/ComboBox offers is NEVER handler business logic, regardless of the constraint mechanism (attribute value, relative period, fixed time-of-day, child existence/count, or gating by another field); it is an entity business rule (apply-static-filter), so use create-entity-business-rule, not crt.InitRequest; " +
		"if the task targets SCHEMA_VALIDATORS call get-guidance with name `page-schema-validators` first; " +
		"if the task adds or edits `@creatio-devkit/common` usage call get-guidance with name `page-schema-creatio-devkit-common` before editing SCHEMA_DEPS or SDK calls.")]
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
		return ExecuteWithCleanLog(() => {
			PageGetCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageGetCommand>(options);
			} catch (Exception ex) {
				return new PageGetResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryGetPage(options, out PageGetResponse response);
			if (response.Success) {
				return WriteFilesAndCompact(response, args.SchemaName, args.OutputDirectory);
			}
			return response;
		});
	}

	private PageGetResponse WriteFilesAndCompact(PageGetResponse response, string schemaName, string? outputDirectory) {
		string anchor = PageOutputDirectoryResolver.ResolveAnchor(
			fileSystem,
			fileSystem.Directory.GetCurrentDirectory(),
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			ClioRuntimePaths.Home,
			outputDirectory);
		string rootDir = fileSystem.Path.Combine(anchor, ".clio-pages");
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
	string? Password,

	[property: JsonPropertyName("output-directory")]
	[property: Description("Optional. Directory to anchor .clio-pages output under — typically your project/workspace root. When omitted, the workspace root is auto-detected by walking up for .clio/workspaceSettings.json; if the server runs from the home directory with no workspace found, output falls back to the clio home root rather than $HOME.")]
	string? OutputDirectory = null
);
