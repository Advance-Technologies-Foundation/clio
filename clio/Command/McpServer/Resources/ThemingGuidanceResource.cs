using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical AI-facing guidance for managing custom Creatio themes through clio MCP: guides the palette
/// conversation via the read-only <c>advise-theme-palette</c> tool (verdict per colour decision), builds the
/// theme CSS with the native <c>build-theme</c> tool (deterministic palette engine + bundled, version-pinned
/// template), and routes between the workspace/dev and no-code/server delivery flows.
/// </summary>
[McpServerResourceType]
public sealed class ThemingGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/theming";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	[McpServerResource(UriTemplate = ResourceUri, Name = "theming-guidance")]
	[Description("Returns canonical MCP guidance for managing custom Creatio themes with clio — create, restyle, delete, list, and set the default — shipping them to a Creatio environment, and the branding companion mechanics (logo sys settings and the shell background image).")]
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

		       Constraints
		       - Theming is supported only on Creatio 10.0.0 or later. On an older environment the theme tools refuse with an explicit version-requirement error — relay it to the user and stop; do not retry or work around it.
		       - Do not change font sizes or line heights — only the font family is meant to change. The interface is not yet adapted to altered typography metrics, so overriding them degrades or breaks the layout. When the user wants to change them, tell them this and leave them at their defaults. If the user keeps insisting, change them only when the user explicitly tells you to ignore this guide. Hand-authored `css-content` must not override them unless that explicit override was given.

		       Calling the tools
		       - Wrap tool arguments under the top-level `args` JSON object exactly as advertised by the tool schema (for example `{"args": {"environment-name": "...", "css-content": "..."}}`). Do not flatten or rename canonical fields.

		       Choosing the colours — advise-theme-palette
		       Never judge a colour by eye. For every colour decision call the `advise-theme-palette` tool (read-only, offline) and act on the verdict it returns — it owns all the thresholds. It is stateless: hold the running choices yourself and re-call it whenever a colour input changes. Handle one decision at a time; surface the -500 value per decision and reveal a full 12-stop scale or the underlying contrast numbers only if the user asks — otherwise relay the verdict in plain words. Only a low-contrast primary forces a fix (step 2); for every other role a below-threshold verdict is a caveat the user may accept, not a block — offer to keep the flagged colour and continue, and note any they keep so you can recap it at the end.
		       1. Primary. Ask for the main brand colour and pass what the user gives to `advise-theme-palette operation=triage`: it normalizes each input (reporting any conversion or rejection) and scores contrast on white. If more than one colour passes, ask which is primary (if the user declines, take the highest-contrast one); if exactly one passes, use it as the primary without asking; if none pass, take the highest-contrast one and let step 2 make it readable. Keep every other accepted colour as an accent candidate. A single colour is the primary.
		       2. Readability. Call `operation=adapt-primary`: on `compliant` keep the colour; on `adapted` offer the darker variant it returns versus the original (explain the trade-off in words) and let the user choose; on `could-not-adapt` tell the user and keep the original.
		       3. Secondary. Call `operation=derive-secondary` for the automatic secondary; show it and recommend keeping it. To override, pass the user's colour to the same operation and relay its verdict (a below-threshold secondary is a caveat, not a block — see above).
		       4. Accent. Its role is highlighting active elements, not large fills. Evaluate spare colours with `operation=accent-evaluate-stored`, or generate options with `operation=accent-suggest`; offer only what the tool marks recommended/valid (or its best pick), and validate a hand-typed accent with `operation=accent-validate-manual`. Use the primary as the accent only when the tool says the fallback is available.
		       5. System colours. Success and error keep the platform defaults unless the user's brand differs; validate any override with `operation=validate-color`.
		       6. Preview. Call `operation=preview` to review the base -500 colour of each role together with the system colours — the full palette stops only if the user asks (`full-stops=true`). This is a checkpoint, not an approval gate. Preview readability is for UI colours (non-text); text contrast is finalized during the build.
		       Once the colours are settled, pass their -500 values to `build-theme`.

		       Building the theme CSS — build-theme
		       - Build the `theme.css` with the native `build-theme` tool: give it brand colors (and optional fonts) and it returns the full CSS, computed by a deterministic OKLCH palette engine over a bundled, version-pinned template — no hand-computed color math and no external package.
		       - Required: a primary colour and a theme name — pass the name as `caption`; `css-class-name` is derived from it (lowercased and hyphenated) when omitted, or pass `css-class-name` explicitly. secondary/accent/success/error and fonts are optional. At least one of `caption` or `css-class-name` is required.
		       - Font weights: build-theme loads a standard set of weights for any custom font by default (see the `font-weights` parameter). When a custom Google Font is chosen, confirm the family actually ships those weights — a family that offers only one weight will render the heavier ones as the nearest available fallback.
		       - The `--crt-*` design-token catalog and advanced hand-authoring beyond the generated palette are out of scope here. Do not invent token names or descriptor rules; stay within the generated palette and the clio tools.

		       Which flow
		       - Workspace / dev flow — use it when you have a clio workspace/package — see "Workspace / dev flow".
		       - No-code / server flow — use it when you have only a registered environment (no workspace/package) — see "No-code / server flow".
		       Pick the flow from what the user already has, not as a fallback — never create a clio workspace/package solely to route a theme around a blocked no-code operation.

		       Checking access
		       Check access up front with `check-theming-access` — it returns `{ success, canManageThemes, canCustomizeBranding }`. The write tools enforce the same rights and return an explicit access error, so never retry past a failed check.
		       - `canCustomizeBranding` false means the environment has no branding license and no custom theme will apply at all — stop, do not build or create anything, and tell the user something like: "Custom branding is not available for the Growth plan. Upgrade your subscription to Enterprise or Unlimited."
		       - `canManageThemes` false blocks the no-code `create-theme` / `update-theme` / `delete-theme` operations, so the operation the user asked for will be rejected — stop and tell the user they lack the right for this operation and to contact their system administrator. Do not switch to the workspace / dev flow to work around this — choose the flow from the user's context (see "Which flow"), never as a fallback for a blocked no-code operation. That flow ships theme files through a package push and so does not exercise this right, but use it only when the user is already working in a clio workspace/package, not to bypass the missing operation.
		       - Never try to grant the license or the system operation yourself, or to work around missing access — that is the administrator's job. Report the gap and stop.

		       Workspace / dev flow
		       Prerequisites: a registered clio environment and the `CanCustomizeBranding` license — see "Checking access".
		       1. Ensure a clio workspace and a package to hold the theme files; if missing, create the workspace with `create-workspace` and add a package with `add-package`.
		       2. Build the theme with `build-theme`, passing `workspace-directory` (the workspace root, absolute) and `package-name`: it writes `theme.css` + `theme.json` into `<workspace-directory>/packages/<package-name>/Files/themes/<css-class-name>/` and returns the path (the CSS is not echoed back). Restyle by re-running it, delete by removing that theme folder.
		       3. Deploy the package with `push-workspace`.
		       4. Confirm the change with `list-themes`.
		       Note: a push refreshes the theme registry on its own; `clear-themes-cache` is only for the rare case of theme files changed on the environment outside a clio install.

		       No-code / server flow
		       Use it when you have only a registered environment (no clio workspace/package); the theme is created and edited directly on the environment via the native ThemeService — no files and no push.
		       Prerequisites: the `CanCustomizeBranding` license and the `CanManageThemes` system operation — see "Checking access".
		       1. Produce the theme CSS first — call `build-theme` (returns the `theme.css` string; see "Building the theme CSS"). It goes into `create-theme` as text in `css-content` (step 2), so external fonts must be referenced via `@import` — local font binaries cannot be uploaded this way.
		       2. Create with `create-theme`: pass the theme name as `caption` and the CSS as inline `css-content`. `css-class-name` is optional (derived from the caption when omitted — see "Building the theme CSS"). `id` is optional — omit it to get an auto-generated id back; `package-name` is optional — omit it to use the environment's CurrentPackageId system setting.
		       3. Restyle with `update-theme` (by id; a full overwrite of caption + css-class-name + css-content; the package cannot be changed).
		       4. Delete with `delete-theme` (by id; deleting an unknown id is an error). If you delete the theme that is currently the default, see "Get / set the default theme".
		       5. Confirm the change with `list-themes`.

		       List themes
		       - List the custom themes on an environment with `list-themes`: it returns each theme's `id`, `caption`, `cssClassName`, and `cssFilePath`. Use it to confirm a theme is available and to find a theme's `id`.
		       - An empty list means the environment has no custom themes, or the caller lacks the `CanCustomizeBranding` license.

		       Get / set the default theme
		       The default is the `DefaultTheme` system setting (value = a theme `id`) and applies to all users — unless the user asked you to make a theme the default, confirm before changing it. Read the current value with `get-sys-setting`. To change it:
		       1. Confirm the target theme is available on the environment (see "List themes").
		       2. Set `DefaultTheme` to the target theme's `id` with `update-sys-setting` (see `docs://mcp/guides/sys-settings`).
		       If you delete the theme that is currently the default, and the user hasn't already specified what to do, inform them and ask whether to set `DefaultTheme` to another theme's `id` or clear it (empty → the stock theme).

		       Branding — logos and background
		       Tool mechanics for the two branding assets beyond the theme itself. Both are environment-wide (All-Users) settings, not per-user: applying them changes the look for every user after a page refresh. The conversation flow (when to ask, which template, which palette stop goes where) is owned by the consuming skill; this section covers only how the writes are made.

		       Logos. Four Binary system settings, one per product slot; write each from a local file with `update-sys-setting` + `value-file-path` (never inline the bytes — see `docs://mcp/guides/sys-settings` for the Binary rules, size cap, and file-security policy):
		       - `LogoImage` — login page (white background).
		       - `MenuLogoImage` — main menu / shell header (white background).
		       - `ConfigurationPageLogoImage` — configuration section (white background).
		       - `CrtAppToolbarLogo` — Freedom UI top panel (dark surface; use the white/light logo variant when one exists, otherwise the main logo).
		       After applying custom logos, set `HideSplashScreenLogoImage` (Boolean) to true so the stock splash logo does not flash during load; leave it untouched when no logos were applied. `CrtAppToolbarLogoUnderlayColor` (text) paints a backing color under the top-panel logo — change it only when the user explicitly asks.

		       Background. The shell background is the `CrtBackgroundConfig` system setting (text) holding JSON `{"imageId":"<SysImage id>","mode":"Image"}`; it may be absent from the `list-sys-settings` catalog, so read and write it by code. The image is a `SysImage` record, but its binary column cannot be written through the OData JSON tools (the stream stays empty) — upload through the platform image API on an authenticated browser session (`get-browser-session` provides one):
		       - POST `<app-url>/0/rest/ImageAPIService/upload?fileapi<timestamp>&totalFileLength=<bytes>&fileId=<new-guid>&mimeType=<url-encoded mime>` with headers `Content-Range: bytes 0-<bytes>/<bytes>`, `Content-Type: <mime>`, `Content-Disposition: attachment; filename=<name>`, and `BPMCSRF: <BPMCSRF cookie value>`; the body is the raw file bytes. The service creates the `SysImage` record with `Id` = the `fileId` you generated.
		       - Verify the upload by fetching `<app-url>/0/img/entity/hash/SysImage/Data/<fileId>` (the `hash` segment is literal) — it must return the file with its mime type.
		       - Register the image in the Appearance-page gallery with `odata-create` on `SysImageInTag`: `{"EntityId":"<fileId>","TagId":"273C2402-7CAE-456B-A9C4-067D2024F1A7"}` (the shell-background tag).
		       - Point `CrtBackgroundConfig` at the image with `update-sys-setting`. The Appearance setup page then lists the image in its gallery as the selected item and renders it in the preview; open pages show it after a refresh.
		       """
	};
}
