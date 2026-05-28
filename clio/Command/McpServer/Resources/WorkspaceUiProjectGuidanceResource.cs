using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical AI-facing guidance for scaffolding a Freedom UI Angular remote-module project
/// inside an existing clio workspace through the <c>new-ui-project</c> MCP tool.
/// </summary>
[McpServerResourceType]
public sealed class WorkspaceUiProjectGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/ui-project";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	[McpServerResource(UriTemplate = ResourceUri, Name = "ui-project-guidance")]
	[Description("Returns canonical MCP guidance for scaffolding a Freedom UI Angular remote-module project inside an existing clio workspace via new-ui-project: required arguments, naming constraints, where files are placed, and the create-workspace prerequisite.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP UI project (Freedom UI remote module) guide

		       Scope
		       - Use this guide when the user asks to create a new Angular project, Freedom UI remote module, or "ui project" inside a clio workspace.
		       - This guide covers the `new-ui-project` MCP tool only. It does NOT cover Freedom UI page authoring (`create-page`, `update-page`) or Creatio package creation through `create-app`.

		       What `new-ui-project` does
		       - Scaffolds an Angular project (Freedom UI remote module template) at `<workspaceDirectory>/projects/<projectName>`.
		       - Ensures a hosting package exists under `<workspaceDirectory>/packages/<packageName>` and links the project to it through the project's template files (vendor prefix, dist path, project name placeholders are substituted into `.json`, `.js`, `.ts`, `.conf`, `.config`, `.scss`, `.css`).
		       - Runs in silent mode through the MCP wrapper: the underlying CLI's interactive "download package?" prompt is auto-answered "no". The tool never reaches out to a Creatio environment.

		       Hard prerequisite — the workspace must already exist
		       - The supplied `workspaceDirectory` MUST be an absolute path to an existing directory that contains `.clio/workspaceSettings.json`. The tool refuses to run otherwise. There is no auto-create fallback.
		       - If the user names a folder that does not exist or is not yet a clio workspace, FIRST call `create-workspace` to initialize it, THEN call `new-ui-project`.

		       Required arguments (wire-format names are camelCase)
		       - `workspaceDirectory` — absolute path to the workspace root. Network-share paths are not supported. Relative paths and missing paths are rejected.
		       - `projectName` — Angular project name. MUST match `^[0-9a-z_]+$` (snake_case: lowercase letters, digits, underscores). Examples: `rss_reader`, `task_board`, `kpi_widget_v2`. PascalCase / camelCase / kebab-case names are rejected by the underlying creator.
		       - `packageName` — clio package name that will host the project. PascalCase is conventional (`UsrRssReader`, `RssReader`). Created if it does not exist; reused if it does.
		       - `vendorPrefix` — 1-50 lowercase letters only (`^[a-z]{1,50}$`). Examples: `usr`, `crt`, `acme`. Mixed-case (`Usr`) and digits are rejected by the options validator.

		       Optional arguments
		       - `empty` (boolean, default `false`) — when `true`, scaffold the `ui-project-Empty` template instead of the default `ui-project` template. Use for a minimal shell when the standard template's sample components are unwanted.
		       - `creatioVersion` (string, default empty) — pick a template variant matching a specific Creatio version. Omit to use the template provider's current default.

		       Where the files land
		       - `<workspaceDirectory>/projects/<projectName>/` — Angular project sources (with `<%projectName%>`, `<%vendorPrefix%>`, `<%distPath%>` placeholders already substituted in template-text files).
		       - `<workspaceDirectory>/packages/<packageName>/` — clio package shell (created if missing).
		       - Build output (configured in the template's tsconfig/angular.json) points the Angular `dist` folder at `packages/<packageName>/Files/src/js/<projectName>/` so that `push-workspace` later ships the built remote module along with the package.

		       Typical workflow
		       1. Confirm or pick the workspace directory. If it does not already exist as a clio workspace, call `create-workspace` first.
		       2. Decide on a snake_case `projectName`, a PascalCase `packageName`, and a lowercase `vendorPrefix`. Translate any user input that does not match these shapes (e.g., user says "RssReader" → `projectName: rss_reader`).
		       3. Call `new-ui-project` with the resolved values. Treat a non-zero `exit-code` as failure and surface `execution-log-messages[*].value` to the user.
		       4. (Optional) After the project is scaffolded, normal Angular workflows apply inside `projects/<projectName>` — install dependencies, run `ng build`, etc.; clio does not manage Node tooling.

		       Common mistakes to avoid
		       - Do NOT call `new-ui-project` against an arbitrary current working directory. The MCP wrapper pins `workspaceDirectory` precisely to avoid scaffolding files in unexpected locations.
		       - Do NOT pass a `projectName` with uppercase letters, hyphens, or spaces. Always translate the user's chosen name into snake_case before calling the tool.
		       - Do NOT pass a `vendorPrefix` containing uppercase letters or digits. Lowercase only.
		       - Do NOT confuse this tool with `create-app` — `new-ui-project` is purely local file-system scaffolding inside a workspace. It does not install anything into a Creatio environment and does not need an `environment-name` argument.
		       - Do NOT call this tool to add Freedom UI pages or sections to an existing app — use `create-page`, `create-app-section`, or `update-page` for those workflows.
		       """
	};
}
