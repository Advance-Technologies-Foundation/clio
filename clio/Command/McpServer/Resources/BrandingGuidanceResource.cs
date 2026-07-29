using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for branding a Creatio environment through clio MCP:
/// replacing the product logos, applying a shell background image, and setting the browser-tab favicon.
/// </summary>
[McpServerResourceType]
public sealed class BrandingGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/branding";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	[McpServerResource(UriTemplate = ResourceUri, Name = "branding-guidance")]
	[Description("Returns canonical MCP guidance for branding a Creatio environment with clio: replacing the product logos, applying a shell background image, and setting the browser-tab favicon. For colours, fonts, and custom themes see the theming guide.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP branding guide

		       Scope
		       Use this guide to brand a Creatio environment:
		       - Apply the product logos — see "Logos".
		       - Apply a shell background image — see "Background".
		       - Replace the browser-tab favicon — see "Favicon".
		       For brand colours, fonts, and custom themes read the theming guide (`get-guidance name=theming`); do not improvise theme changes from here.

		       Constraints
		       - Both branding assets are environment-wide (All-Users) settings, not per-user: applying them changes the look for every user after a page refresh.
		       - Branding writes require the `CanCustomizeBranding` license. Check up front with `check-theming-access` (`canCustomizeBranding` in the response); when it is false, stop — do not upload or write anything — and tell the user something like: "Custom branding is not available for the Growth plan. Upgrade your subscription to Enterprise or Unlimited."
		       - Applying a logo cannot be automatically reverted by clio; warn the user before writing one.

		       Target package (data delivery)
		       - `set-logo` and `set-background-image` do not only change this environment: each also binds the applied branding into a package as Creatio data bindings (their `package` argument, default `Custom`), so the branding ships with the package on an install or a transfer to another site.
		       - Decide one target package for the whole branding operation before applying anything: prefer a package the user names, otherwise the package of the app being branded; when neither exists, use the default `Custom`. Validate a user-named package against `list-packages`.
		       - When you finalize what will be done (theme, logos, background), tell the user which package the new data will be added to — name it, for example: "The theme, logos and background will be added to package <X>." Pass that same package to `create-theme` (its package argument) and to the `package` argument of `set-logo` / `set-background-image`.
		       - Binding needs an editable (unlocked) package and rights to modify package configuration. The `CanCustomizeBranding` license gates the APPLY side, not the package write.
		       - Read each tool's result back to the user: it names the package and reports what was bound and what was skipped. The `skipped` entries are the only place a delivery gap is reported — relay them; each one means the package ships less than the user may expect.
		       - Re-running the same tool after any later branding change refreshes both the environment and the packaged snapshot; the bindings are created when missing and updated in place when present. Removing or replacing an asset drops the bindings whose source row is gone (reported in `skipped`).

		       Calling the tools
		       - Wrap tool arguments under the top-level `args` JSON object exactly as advertised by the tool schema (for example `{"args": {"environment-name": "...", "file": "..."}}`). Do not flatten or rename canonical fields.

		       Logos
		       One call: `set-logo` with a local image file per slot you brand (pass only the slots the user wants; at least one):
		       - `logo` — login page (white background).
		       - `menu-logo` — main menu / shell header (white background).
		       - `configuration-logo` — configuration section (white background).
		       - `dark-logo` — the logo for a dark background: the Freedom UI top panel. Use the white/light logo variant when one exists, otherwise the main logo.
		       The tool writes each slot's Binary system setting from the file (the file rules, size cap, and file-security policy from `docs://mcp/guides/sys-settings` apply), suppresses the stock splash-screen logo automatically, and binds the applied values into the target package. Only the slots you pass (plus slots shipped by an earlier run) are bound — an unbranded slot is never delivered, so the package cannot overwrite an install target's own logo with this environment's stock value. The `CrtAppToolbarLogoUnderlayColor` system setting (text) paints a backing color under the top-panel logo — write it with `update-sys-setting` only when the user explicitly asks; it stays on this environment and does not travel with the package.

		       Background
		       Call `set-background-image` with the local image file path (`file`); it uploads the file and makes it the shell background, replacing the currently configured one. To re-apply an image that was already uploaded with `upload-image`, pass its `image-id` instead of `file` (exactly one of the two).
		       Applying a background also turns the `UsePanelIconBackground` feature off for all users: while it is on the panel's own icon background hides the shell background. Pass `keep-icon-background` = true only when the user explicitly wants that feature untouched. The turn-off is best-effort — a failure is reported as a warning, not a failure of the apply — and the off-state is bound with the background so the install target inherits it. When the feature was never turned off on this environment, it is reported as skipped.

		       Favicon
		       The browser-tab icon, driven by two system settings written with `update-sys-setting` (see `docs://mcp/guides/sys-settings` for the Binary rules, size cap, and file-security policy):
		       - `FaviconImage` (Binary) — the icon file (a small square SVG, PNG, or ICO); write it from a local file with `value-file-path`, never inline the bytes.
		       - `UseFaviconFromSysSettings` (Boolean) — set it to true, otherwise the platform ignores `FaviconImage` and keeps the stock Creatio icon.
		       Apply order: write `FaviconImage`, then set `UseFaviconFromSysSettings` to true. A favicon change is not visible on an open session — the user must sign out and back in, and an already-open browser tab may keep the old icon until it is closed and reopened, because browsers cache tab icons aggressively; tell the user this whenever the favicon changes.
		       """
	};
}
