using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for creating and editing Freedom UI indicator widgets through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class IndicatorWidgetGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/indicator-widget";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP indicator widget guide

		       Before you create, edit, filter, or troubleshoot a `crt.IndicatorWidget` on a Freedom UI page,
		       you MUST call `get-component-info` for `crt.IndicatorWidget` and read its documentation in full,
		       including every reference and link it points to.

		       That component documentation is the single source of truth for indicator widgets. It owns the
		       generation contract (diff sections, aggregation expression, filter-leaf shapes), the intent ->
		       runtime config translation, the authoring workflow, and the related `esq-filters`,
		       `page-modification`, and `page-schema-resources` guidance.

		       Do NOT author or edit an indicator widget payload from memory or from this pointer alone — read
		       the `get-component-info` documentation and its references first.

		       ----

		       ## General

		       ### Placement Rules
		       - Never set `parentName` as code of a dashboard component.
		       - You may use `parentName`: "Main" only when working with Home pages.
		       - On any other page, if the user asks to add a widget but does not clarify where on the page, and
		         you know there are other widgets, place it near the existing ones (use the same `parentName` as
		         another widget).

		       ## Card theme
		       The card theme is set by the SURFACE's guide, not here: `dashboard-and-home-page-layout` for dashboards and home
		       pages (plain-white / `theme` "without-fill"), `desktop-page` for desktops (glassmorphism). For the
		       rest of the runtime config read the `crt.IndicatorWidget` documentation via `get-component-info`.

		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for Freedom UI indicator widgets.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "indicator-widget-guidance")]
	[Description("Returns canonical MCP guidance for Freedom UI indicator widgets, including Copilot-intent to runtime-config translation, aggregation rules, and static filter authoring patterns.")]
	public ResourceContents GetGuide() => Guide;
}
