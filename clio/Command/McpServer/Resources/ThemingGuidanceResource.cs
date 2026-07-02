using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical AI-facing guidance for managing custom Creatio themes through clio MCP: guides the palette
/// conversation via the read-only <c>theme-color-advisor</c> tool (verdict per colour decision), builds the
/// theme CSS with the native <c>build-theme</c> tool (deterministic palette engine + bundled, version-pinned
/// template), and routes between the workspace/dev and no-code/server delivery flows.
/// </summary>
[McpServerResourceType]
public sealed class ThemingGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/theming";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	[McpServerResource(UriTemplate = ResourceUri, Name = "theming-guidance")]
	[Description("Returns canonical MCP guidance for managing custom Creatio themes with clio — create, restyle, delete, list, and set the default — and shipping them to a Creatio environment.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP custom-theme guide

		       Scope
		       Use this guide to manage a custom Creatio theme through clio:
		       - Choose the brand colours with guidance — see "Choosing the colours".
		       - Create, restyle, or delete a theme on an environment — see "Which flow".
		       - List existing themes — see "List themes".
		       - Get or set the default theme — see "Get / set the default theme".

		       Choosing the colours — theme-color-advisor
		       Never judge a colour by eye. For every colour decision call the `theme-color-advisor` tool (read-only, offline) and act on the verdict it returns — it owns all the thresholds. It is stateless: hold the running choices yourself and re-call it whenever a colour input changes. Show colours as inline-code swatches (e.g. `#004fd6`), one decision at a time; show the -500 value per decision and reveal a full 12-stop scale or the underlying contrast numbers only if the user asks — otherwise relay the verdict in plain words. Only a low-contrast primary forces a fix (step 2); for every other role a below-threshold verdict is a caveat the user may accept, not a block — offer to keep the flagged colour and continue, and note any they keep so you can recap it at the end.
		       1. Primary. Ask for the main brand colour and pass what the user gives to `theme-color-advisor operation=triage`: it normalizes each input (reporting any conversion or rejection) and scores contrast on white. If more than one colour passes, ask which is primary (if the user declines, take the highest-contrast one); if exactly one passes, use it as the primary without asking; if none pass, take the highest-contrast one and let step 2 make it readable. Keep every other accepted colour as an accent candidate. A single colour is the primary.
		       2. Readability. Call `operation=adapt-primary`: on `compliant` keep the colour; on `adapted` offer the darker variant it returns versus the original (explain the trade-off in words) and let the user choose; on `could-not-adapt` tell the user and keep the original.
		       3. Secondary. Call `operation=derive-secondary` for the automatic secondary; show it and recommend keeping it. To override, pass the user's colour to the same operation and relay its verdict (a below-threshold secondary is a caveat, not a block — see above).
		       4. Accent. Its role is highlighting active elements, not large fills. Evaluate spare colours with `operation=accent-evaluate-stored`, or generate options with `operation=accent-suggest`; offer only what the tool marks recommended/valid (or its best pick), and validate a hand-typed accent with `operation=accent-validate-manual`. Use the primary as the accent only when the tool says the fallback is available.
		       5. System colours. Success and error keep the platform defaults unless the user's brand differs; validate any override with `operation=validate-color`.
		       6. Preview. Call `operation=preview` to show the compact stops (50, 100, 300, 500, 800) with the system colours — the full 12 only if asked. This is a checkpoint, not an approval gate. Preview readability is for UI colours (non-text); text contrast is finalized during the build.
		       Once the colours are settled, pass their -500 values to `build-theme`.

		       Building the theme CSS — build-theme
		       - Build the `theme.css` with the native `build-theme` tool: give it brand colors (and optional fonts) and it returns the full CSS, computed by a deterministic OKLCH palette engine over a bundled, version-pinned template — no hand-computed color math and no external package.
		       - Required: a primary colour and a theme name — pass the name as `caption`; `cssClassName` is derived from it (slugified) when omitted, or pass `cssClassName` explicitly. secondary/accent/success/error and fonts are optional. At least one of `caption` or `cssClassName` is required.
		       - The `--crt-*` design-token catalog and advanced hand-authoring beyond the generated palette are not restated here — consult the official Creatio theming documentation for token names and descriptor rules; do not infer them.

		       Which flow
		       - Workspace / dev flow — use it when you have a clio workspace/package — see "Workspace / dev flow".
		       - No-code / server flow — use it when you have only a registered environment and credentials (no workspace/package) — see "No-code / server flow".

		       Workspace / dev flow
		       Prerequisites: a registered clio environment and the `CanCustomizeBranding` license; confirm the user has it before authoring.
		       1. Ensure a clio workspace and a package to hold the theme files; if missing, create the workspace with `create-workspace` and add a package with `add-package`.
		       2. Build the theme with `build-theme`, passing `workspaceDirectory` (the workspace root, absolute) and `packageName`: it writes `theme.css` + `theme.json` into `<workspaceDirectory>/packages/<packageName>/Files/themes/<cssClassName>/` and returns the path (the CSS is not echoed back). Restyle by re-running it, delete by removing that theme folder.
		       3. Deploy the package with `push-workspace`.
		       4. Confirm the change with `list-themes-by-environment`.
		       Note: a push refreshes the theme registry on its own; `clear-themes-cache-by-environment` is only for the rare case of theme files changed on the environment outside a clio install.

		       No-code / server flow
		       Use it when you have only a registered environment and credentials (no clio workspace/package); the theme is created and edited directly on the environment via the native ThemeService — no files and no push.
		       Prerequisites: the `CanCustomizeBranding` license and the `CanManageThemes` system operation. Check both in one call with `check-theming-access-by-environment` (or `-by-credentials`) — it returns `{ canManageThemes, canCustomizeBranding }`. When either is false, tell the user which one is missing; the check is advisory — `create-theme-by-environment` is the authoritative test and returns an explicit access error when a right is genuinely absent.
		       1. Produce the theme CSS first — call `build-theme` (returns the `theme.css` string; see "Building the theme CSS"). It goes into `create-theme-by-environment` as text in `cssContent` (step 2), so external fonts must be referenced via `@import` — local font binaries cannot be uploaded this way.
		       2. Create with `create-theme-by-environment`: pass the theme name as `caption` and the CSS as inline `cssContent`. `cssClassName` is optional — omit it and clio derives a valid one by slugifying the caption (pass it explicitly only to override); at least one of `caption` or `cssClassName` is required. `id` is optional — omit it to get an auto-generated id back; `packageName` is optional — omit it to use the environment's CurrentPackageId system setting.
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
