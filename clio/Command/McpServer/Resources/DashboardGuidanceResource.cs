using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// The dashboard router: a short index that points dashboard work at the matching guide
/// (creation vs widget design vs per-widget payload). It holds no dashboard rules itself —
/// the detailed guidance lives once in the guides it routes to.
/// </summary>
[McpServerResourceType]
public sealed class DashboardGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/dashboards";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP dashboards router

		       Pick the dashboard guide that matches the task (a dashboard is a page inheriting
		       `BaseDashboardTemplate`) and read it with get-guidance before planning or mutating:

		       - CREATE a dashboard page — the `BaseDashboardTemplate` schema and its link-back optional properties
		         (`DashboardsEntitySchemaName`, `DashboardsElementName`, `DashboardsClientUnitSchemaUId`),
		         including how to retrieve each value -> get-guidance name=dashboard-creation
		       - LAY OUT / size / group / style the analytical widgets — the 12-column grid, the
		         metric-band-then-chart-grid skeleton, per-widget-type sizes, the plain-white card style ->
		         get-guidance name=widget-layout
		       - FILTER a dashboard's widgets by its page data (the hidden `DashboardDS` source) ->
		         get-guidance name=dashboard-design
		       - A single widget's runtime payload — get-guidance name=indicator-widget (metrics) or
		         name=chart-widget (charts), plus get-component-info for its exact contract
		       """
	};

	/// <summary>
	/// Returns the dashboard router that points dashboard work at the matching guide.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "dashboards-guidance")]
	[Description("Returns the clio MCP dashboards router: a names-only index that routes dashboard work to dashboard-creation (create the page), widget-layout (widget layout/sizing/styling), dashboard-design (the DashboardDS filter-by-page-data binding), and indicator-widget / chart-widget (per-widget payload).")]
	public ResourceContents GetGuide() => Guide;
}
