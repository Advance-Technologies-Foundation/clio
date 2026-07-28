using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for the dashboard-specific auto-generated hidden page data
/// source (DashboardDS) that a dashboard's widgets filter by via a dependencies entry. General widget
/// layout, sizing, grouping, and styling live in the shared <c>dashboard-and-home-page-layout</c> guide.
/// </summary>
[McpServerResourceType]
public sealed class DashboardDesignGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/dashboard-design";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP dashboard design guide

		       Use this guide for the DASHBOARD-SPECIFIC part of widget design: the auto-generated hidden page data
		       source (`DashboardDS`) that a dashboard's widgets filter by. A dashboard is a standalone analytics
		       screen the user opens from the Dashboards section to monitor — the analytics are the destination,
		       not part of working a record. To CREATE a dashboard page (the `BaseDashboardTemplate` schema and its
		       link-back properties) read `dashboard-creation`.

		       For the GENERAL layout, sizing, grouping, and styling of analytical widgets — the 12-column grid,
		       the metric-band-then-chart-grid skeleton, per-widget-type sizes, the plain-white card style, worked
		       patterns, and the finish checklist — read `dashboard-and-home-page-layout`. Those rules apply to a dashboard exactly
		       as to any other analytics surface; this guide does not restate them. This guide adds only what is
		       unique to a dashboard: the `DashboardDS` binding below.

		       ## The dashboard's hidden page data source (`DashboardDS`)

		       A dashboard auto-generates a HIDDEN page-level data source at runtime from the schema's
		       `DashboardsEntitySchemaName` optional property, exposed as `DashboardDS` (scope `page`). It is NOT
		       written in the schema body, so do NOT conclude "this dashboard has no page record source" from a
		       static body with no `primaryDataSourceName`. Read `DashboardsEntitySchemaName` from the
		       `optionalProperties` array in the `bundle.json` that `get-page` writes (it is NOT in the `get-page`
		       response or `meta.json`). To SET it when creating a dashboard, see `dashboard-creation`.

		       `DashboardDS` exists so a widget can be filtered by the dashboard's page data. A widget opts in by
		       BINDING to it with a `dependencies` entry (the "Apply filter by page data" toggle) — the same
		       declarative mechanism as record-page details (see `related-list`), with `DashboardDS` as the master
		       instead of `PDS`.

		       MANDATORY: whenever the dashboard declares a `DashboardsEntitySchemaName` you MUST add this binding to
		       EVERY data-bound widget — on LIST hosts exactly as on FORM hosts (a list host filters widgets by the
		       list's active filter, a form host by the current record). It is the easiest step to forget: a widget
		       left unbound silently ignores the host filter and always shows unfiltered data. Treat any unbound
		       data-bound widget as a defect — the dashboard is NOT done until every one is bound.

		       The binding entry is `{ "attributePath": <col>, "relationPath": "DashboardDS.Id" }`:
		       - `relationPath` is ALWAYS `"DashboardDS.Id"`.
		       - `attributePath` is `"Id"` when the widget's entity EQUALS `DashboardsEntitySchemaName` (Id = Id);
		         otherwise the widget entity's bare FK column pointing at that entity (never the `...Id` form —
		         resolve via `get-app-info` / `get-entity-schema-properties`).

		       Its placement and exact shape are widget-specific — read each widget's contract via
		       `get-component-info`.

		       ## Finish checklist (dashboard-specific)

		       - If the dashboard has a `DashboardsEntitySchemaName`, every data-bound widget has a `dependencies`
		         entry to `DashboardDS.Id` (`attributePath` = `Id` when the widget entity matches it, else its FK column).
		       - For the general layout/sizing/styling checklist, see `dashboard-and-home-page-layout`.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for placing, sizing, grouping, and styling Freedom UI
	/// dashboard widgets.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "dashboard-design-guidance")]
	[Description("Returns canonical MCP guidance for the dashboard-specific DashboardDS hidden page data source that a dashboard's widgets filter by; general widget layout/sizing/styling lives in the dashboard-and-home-page-layout guide.")]
	public ResourceContents GetGuide() => Guide;
}
