using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical AI-facing guidance for managing custom Creatio themes through clio MCP: routes between the
/// workspace/dev and no-code/server delivery flows and delegates the token catalog, theme template,
/// palette engine, and authoring rules to the <c>@creatio/theming</c> package.
/// </summary>
[McpServerResourceType]
public sealed class ThemingGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/theming";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	[McpServerResource(UriTemplate = ResourceUri, Name = "theming-guidance")]
	[Description("Returns canonical MCP guidance for managing custom Creatio themes with @creatio/theming — create, restyle, delete, list, and set the default — and shipping them to a Creatio environment with Clio.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP custom-theme guide

		       Scope
		       Use this guide to manage a custom Creatio theme through clio:
		       - Create, restyle, or delete a theme on an environment — see "Which flow".
		       - List existing themes — see "List themes".
		       - Get or set the default theme — see "Get / set the default theme".

		       Source of truth — @creatio/theming
		       - Start at `AI_GUIDES_INDEX.md` — it is the authority for which guides and templates exist and their names.
		       - Follow it to the theme authoring guide, the `--crt-*` design-token catalog, and the theme templates — the only source for token names and descriptor rules; do not infer them.
		       - It also ships a deterministic palette engine (an importable module) and a palette-algorithm guide describing it — the flows below use the engine FROM CODE to build the theme CSS from brand colors, instead of hand-computing color math.
		       - Fetch these as reference: run `npm pack @creatio/theming` and read the extracted tarball, or `npm i @creatio/theming` in a Node project (a `new-ui-project` already declares it) and read its `node_modules`.

		       Which flow
		       - Workspace / dev flow — use it when you have a clio workspace/package — see "Workspace / dev flow".
		       - No-code / server flow — use it when you have only a registered environment and credentials (no workspace/package) — see "No-code / server flow".

		       Workspace / dev flow
		       Prerequisites: a registered clio environment and the `CanCustomizeBranding` license; confirm the user has it before authoring.
		       1. Ensure a clio workspace and a package to hold the theme files; if missing, create the workspace with `create-workspace` and add a package with `add-package`.
		       2. Create, restyle, or delete the theme by editing the package files, following the package's authoring guide and template.
		       3. Deploy the package with `push-workspace`.
		       4. Confirm the change with `list-themes-by-environment`.
		       Note: a push refreshes the theme registry on its own; `clear-themes-cache-by-environment` is only for the rare case of theme files changed on the environment outside a clio install.

		       No-code / server flow
		       Use it when you have only a registered environment and credentials (no clio workspace/package); the theme is created and edited directly on the environment via the native ThemeService — no files and no push.
		       Prerequisites: the `CanCustomizeBranding` license and the `CanManageThemes` system operation; confirm the user has them before authoring.
		       1. Produce the theme CSS first — build it with the `@creatio/theming` palette engine (see "Source of truth"). It goes into `create-theme-by-environment` as text in `css-content` (step 2), so external fonts must be referenced via `@import` — local font binaries cannot be uploaded this way.
		       2. Create with `create-theme-by-environment` (required: css-class-name and inline css-content). `--caption` is optional — derived from css-class-name when omitted; `--id` is optional — omit it to get an auto-generated id back; `--package-name` is optional — omit it to use the environment's CurrentPackageId system setting.
		       3. Restyle with `update-theme-by-environment` (by id; a full overwrite of caption + css-class-name + css-content; the package cannot be changed).
		       4. Delete with `delete-theme-by-environment` (by id; deleting an unknown id is an error). If you delete the theme that is currently the default, see "Get / set the default theme".
		       5. Confirm the change with `list-themes-by-environment`.

		       List themes
		       - List the custom themes on an environment with `list-themes-by-environment`: it returns each theme's `id`, `caption`, `cssClassName`, and `cssFilePath`. Use it to confirm a theme is available and to find a theme's `id`.
		       - An empty list means the environment has no custom themes, or the caller lacks the `CanCustomizeBranding` license.

		       Get / set the default theme
		       The default is the `DefaultTheme` system setting (value = a theme `id`) and applies to all users — unless the user asked you to make a theme the default, confirm before changing it. Read the current value with `get-sys-setting`. To change it:
		       1. Confirm the target theme is available on the environment (see "List themes").
		       2. Set `DefaultTheme` to the target theme's `id` with `update-sys-setting` (see `docs://mcp/guides/sys-settings`).
		       If you delete the theme that is currently the default, and the user hasn't already specified what to do, inform them and ask whether to set `DefaultTheme` to another theme's `id` or clear it (empty → the stock theme).
		       """
	};
}
