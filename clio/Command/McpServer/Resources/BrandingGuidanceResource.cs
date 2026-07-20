using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical AI-facing guidance for branding a Creatio environment through clio MCP beyond the theme
/// itself: the four product logo slots (Binary sys settings written via <c>update-sys-setting</c> +
/// <c>value-file-path</c>), the splash/underlay companion settings, and the shell background image
/// (upload via the dedicated <c>upload-image</c> tool, Appearance-gallery registration via
/// <c>SysImageInTag</c>, activation via <c>CrtBackgroundConfig</c>). The theme part of branding
/// (colours, fonts, custom themes) is owned by the theming guide.
/// </summary>
[McpServerResourceType]
public sealed class BrandingGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/branding";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	[McpServerResource(UriTemplate = ResourceUri, Name = "branding-guidance")]
	[Description("Returns canonical MCP guidance for branding a Creatio environment with clio beyond the theme — the four product logo sys settings, the splash/underlay companion settings, and the shell background image (upload, gallery registration, activation). The theme part of branding (colours, fonts, custom themes) is covered by the theming guide.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP branding guide

		       Scope
		       Use this guide to brand a Creatio environment through clio beyond the theme itself:
		       - Apply or restore the product logos — see "Logos".
		       - Apply a shell background image — see "Background".
		       For the theme part of branding — brand colours, fonts, creating/restyling/applying a custom theme — read the theming guide (`get-guidance name=theming`); do not improvise theme changes from here.

		       Constraints
		       - Both branding assets are environment-wide (All-Users) settings, not per-user: applying them changes the look for every user after a page refresh.
		       - Branding writes require the `CanCustomizeBranding` license. Check up front with `check-theming-access` (`canCustomizeBranding` in the response); when it is false, stop — do not upload or write anything — and tell the user something like: "Custom branding is not available for the Growth plan. Upgrade your subscription to Enterprise or Unlimited."

		       Calling the tools
		       - Wrap tool arguments under the top-level `args` JSON object exactly as advertised by the tool schema (for example `{"args": {"environment-name": "...", "file": "..."}}`). Do not flatten or rename canonical fields.

		       Logos
		       Four Binary system settings, one per product slot; write each from a local file with `update-sys-setting` + `value-file-path` (never inline the bytes — see `docs://mcp/guides/sys-settings` for the Binary rules, size cap, and file-security policy):
		       - `LogoImage` — login page (white background).
		       - `MenuLogoImage` — main menu / shell header (white background).
		       - `ConfigurationPageLogoImage` — configuration section (white background).
		       - `CrtAppToolbarLogo` — Freedom UI top panel (dark surface; use the white/light logo variant when one exists, otherwise the main logo).
		       After applying custom logos, set `HideSplashScreenLogoImage` (Boolean) to true so the stock splash logo does not flash during load; leave it untouched when no logos were applied. `CrtAppToolbarLogoUnderlayColor` (text) paints a backing color under the top-panel logo — change it only when the user explicitly asks.
		       Restore the default logos by clearing the four Binary settings (empty value) and setting `HideSplashScreenLogoImage` back to false.

		       Background
		       The shell background is the `CrtBackgroundConfig` system setting (text) holding JSON `{"imageId":"<SysImage id>","mode":"Image"}`; it may be absent from the `list-sys-settings` catalog, so read and write it by code. The image is a `SysImage` record, but its binary column cannot be written through the OData JSON tools (the stream stays empty) — upload it with the dedicated `upload-image` tool, then register and activate it:
		       - Upload: call `upload-image` with the local image file path (`file`). It uploads the file through the platform image API on the authenticated clio session (both .NET Framework and .NET Core runtimes are handled) and returns the created `SysImage` record's `imageId` — keep it for the next two steps.
		       - Register the image in the Appearance-page gallery with `odata-create` on `SysImageInTag`: `{"EntityId":"<imageId>","TagId":"273C2402-7CAE-456B-A9C4-067D2024F1A7"}`. That TagId is the platform-seeded `SysImageTag` record named `shell_background` and carries the same id on every installation; if the write is rejected with a foreign-key error, read `SysImageTag` (filter `Name = "shell_background"`) and use the id it returns.
		       - Point `CrtBackgroundConfig` at the image with `update-sys-setting`. The Appearance setup page then lists the image in its gallery as the selected item and renders it in the preview; open pages show it after a refresh.
		       """
	};
}
