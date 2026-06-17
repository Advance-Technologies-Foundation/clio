using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for placing, sizing, and styling Creatio Freedom UI
/// analytical widgets. Deliberately a thin pointer: the authoritative layout math and component
/// detail live in the <c>dashboards</c> and <c>indicator-widget</c> guides, referenced by name to
/// avoid content duplication.
/// </summary>
[McpServerResourceType]
public sealed class AnalyticsWidgetsGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/analytics-widgets";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP analytics-widgets guide

		       Add and arrange Creatio Freedom UI analytical widgets (metrics/indicators and charts) so they look
		       native and consistent with Creatio's standard analytics dashboards. This guide LEADS when the task
		       is about placing widgets; for surrounding page structure follow get-guidance name=page-modification.

		       This guide is a POINTER. The authoritative detail lives in two existing guides — read them, do not
		       expect their content copied here:
		       - See also: get-guidance name=dashboards — the canonical metric-band-then-chart-grid layout, the
		         12-column sizing math, per-widget-type default sizes, section grouping, and the plain-white card
		         policy.
		       - See also: get-guidance name=indicator-widget — metric (crt.IndicatorWidget) aggregate selection,
		         intent-to-runtime-config translation, and static-filter shapes. That guide also requires calling
		         get-component-info for crt.IndicatorWidget before authoring a widget.

		       ## The three things to get right (in this order)
		       1. Right place — a top band of metric tiles, then a grid of chart widgets below, optionally grouped
		          under section headers. Metrics never go below their charts; charts never sit in the metric band.
		       2. Right style — plain white card by default. A colored widget background is a rare signal for a
		          single emphasized KPI, never the starting point, and never more than one or two on a screen.
		       3. Right size — size by widget TYPE, not by eye. Metrics are small and short; charts are wider and
		          ~3x taller. Every row must sum to exactly 12 columns (4+4+4, 6+6, 4+8); composition breakdowns
		          (pie/doughnut) read best as three at 4 columns each.

		       For the exact per-type defaults, the 12-column math, per-row counts, and grouping rules, follow the
		       dashboards guide.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for Creatio Freedom UI analytical widget placement.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "analytics-widgets-guidance")]
	[Description("Returns canonical MCP guidance for placing, sizing, and styling Freedom UI analytical widgets; a pointer that defers to the dashboards and indicator-widget guides.")]
	public ResourceContents GetGuide() => Guide;
}
