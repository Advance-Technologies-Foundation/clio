using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for branding a Creatio environment through clio MCP:
/// replacing the product logos and applying a shell background image.
/// </summary>
[McpServerResourceType]
public sealed class BrandingGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/branding";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	[McpServerResource(UriTemplate = ResourceUri, Name = "branding-guidance")]
	[Description("Returns canonical MCP guidance for branding a Creatio environment with clio: replacing the product logos and applying a shell background image. For colours, fonts, and custom themes see the theming guide.")]
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
		       For brand colours, fonts, and custom themes read the theming guide (`get-guidance name=theming`); do not improvise theme changes from here.

		       Constraints
		       - Both branding assets are environment-wide (All-Users) settings, not per-user: applying them changes the look for every user after a page refresh.
		       - Branding writes require the `CanCustomizeBranding` license. Check up front with `check-theming-access` (`canCustomizeBranding` in the response); when it is false, stop — do not upload or write anything — and tell the user something like: "Custom branding is not available for the Growth plan. Upgrade your subscription to Enterprise or Unlimited."
		       - Applying a logo cannot be automatically reverted by clio; warn the user before writing one.

		       Calling the tools
		       - Wrap tool arguments under the top-level `args` JSON object exactly as advertised by the tool schema (for example `{"args": {"environment-name": "...", "file": "..."}}`). Do not flatten or rename canonical fields.

		       Logos
		       Four Binary system settings, one per product slot; write each from a local file with `update-sys-setting` + `value-file-path` (never inline the bytes — see `docs://mcp/guides/sys-settings` for the Binary rules, size cap, and file-security policy):
		       - `LogoImage` — login page (white background).
		       - `MenuLogoImage` — main menu / shell header (white background).
		       - `ConfigurationPageLogoImage` — configuration section (white background).
		       - `CrtAppToolbarLogo` — top panel (dark surface; use the white/light logo variant when one exists, otherwise the main logo).
		       After applying custom logos, set the `HideSplashScreenLogoImage` system setting (Boolean) to true with `update-sys-setting` so the stock splash logo does not flash during load; leave it untouched when no logos were applied. The `CrtAppToolbarLogoUnderlayColor` system setting (text) paints a backing color under the top-panel logo — write it with `update-sys-setting` only when the user explicitly asks.

		       Background
		       Call `set-background-image` with the local image file path (`file`); it uploads the file and makes it the shell background, replacing the currently configured one. To re-apply an image that was already uploaded with `upload-image`, pass its `image-id` instead of `file` (exactly one of the two).
		       """
	};
}
