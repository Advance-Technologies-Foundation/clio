using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides the canonical AI-facing entry point for authoring Freedom UI analytics widgets through clio MCP.
/// </summary>
/// <remarks>
/// This type ships a thin index <see cref="Guide"/> that orients an agent and routes to the deep guides
/// (<c>dashboards</c>, <c>indicator-widget</c>, <c>page-modification</c>) plus a net-new
/// <see cref="PlacementContexts"/> reference describing the non-dashboard surfaces. The detailed layout,
/// 12-column grid, widget catalog, sizing, styling and patterns intentionally live in the <c>dashboards</c>
/// guidance and are not duplicated here.
/// </remarks>
[McpServerResourceType]
public sealed class AnalyticsWidgetsGuidanceResource {
	private const string DocsScheme = "docs";
	private const string GuidePath = "mcp/guides/analytics-widgets";
	private const string GuideUri = DocsScheme + "://" + GuidePath;
	private const string ReferenceBasePath = "mcp/references/analytics-widgets";

	/// <summary>
	/// Thin index guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = GuideUri,
		MimeType = "text/plain",
		Text = """
		       INTERIM (review by 2026-12-18): analytics-widgets is the clio-owned single source of truth for analytics-widget authoring; the ADAC toolkit ships only a thin pointer. Migration tracking: ENG-TBD. Do not duplicate this content elsewhere.

		       analytics-widgets is the routing index for placing metric (indicator) widgets and charts on Freedom UI analytics surfaces. Use it the moment a task involves a dashboard or analytics page, or adding metric widgets or charts to any page, so you reach the deep guidance instead of authoring from memory. This index does not itself contain the layout, catalog, or payload rules — it routes you to the guides that do.

		       Routing:
		       - For the full dashboard layout, 12-column grid, widget catalog, sizing, styling and patterns call get-guidance name=dashboards.
		       - For the indicator (metric) widget runtime payload contract call get-guidance name=indicator-widget.
		       - For non-dashboard surfaces read docs://mcp/references/analytics-widgets/placement-contexts; for surrounding page structure call get-guidance name=page-modification.

		       Three things to get right (place / style / size):
		       - Place: choose the surface that matches the user's intent (monitor -> dashboard; section-scoped -> list-page analytics view; arrival -> home page; record-scoped -> form-page widget area); see the placement-contexts reference.
		       - Style: keep cards plain white with default title and value colors unless the user asked otherwise.
		       - Size: size to the surface's actual column count.
		       The exact rules for all three live in the dashboards guidance — call get-guidance name=dashboards before you author anything; this index only points the way.
		       """
	};

	/// <summary>
	/// Net-new reference describing the four analytics-widget placement surfaces. Accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents PlacementContexts = CreateReference(
		"placement-contexts",
		"""
		Placement contexts — where widgets live

		The same widget vocabulary and the same plain-white + 12-column rules apply on every surface (call get-guidance name=dashboards for both). This reference notes what changes per surface.

		Scope note: Dashboards are fully specified. The other three surfaces below capture the rules that already hold; placement specifics will be expanded as more product examples are gathered. When unsure on a non-dashboard surface, call get-guidance name=dashboards — those rules transfer.

		## 1. Dashboards (canonical)

		A standalone analytics screen the user opens from the Dashboards section (selectable views like "Agent 360 View", "Service command center"). This is the surface the dashboards guidance describes in full: metric band on top, chart grid below, sections for multi-topic, plain white throughout.

		Use dashboards when the analytics are the destination — the user comes here to monitor, not to work a record.

		## 2. List-page analytics view

		Every section list page (Accounts, Contacts, Cases, Leads, Orders, ...) has an analytics view toggled from the list/chart switch in the top-right, plus a Summaries panel. The product examples ("Accounts analytics", "Contacts analytics", "Leads analytics", "Orders analytics") follow the same skeleton as a dashboard:

		- A top metric band (e.g. "Number of accounts", "Open leads", "Total orders value", "Accounts without primary contact") — 4-up at 3 cols each.
		- A chart grid below: doughnut breakdowns, bar charts, and a wide bar/category chart at the bottom.
		- Plain white cards; metric values default-colored (red for gap/overdue counts like "Accounts without primary contact").

		What differs from a standalone dashboard:

		- The analytics are scoped to the section's records and respect the active folders/filters/tags chosen on the list (e.g. the Orders view filtered to a "Supervisor" owner). Design metrics/charts to read correctly under filtering.
		- It's a per-section analytics view, not a cross-topic command center, so section headers are usually unnecessary — one metric band + one chart grid is enough. Add headers only if you genuinely have distinct groups.
		- Keep it focused: a list-page analytics view should answer "what does this filtered set look like?", not become a full multi-section dashboard.

		## 3. Home page

		A landing page composed of widgets (and often links/navigation), shown when a user opens the app or a workspace.

		- Same widget types, plain white style, 12-column sizing.
		- Lead with the few KPIs that matter on arrival (a compact metric band), then a small number of charts and/or a List widget for "my open items" the user acts on immediately.
		- Favor fewer, higher-signal widgets over a dense dashboard — a home page is a starting point, not an analysis surface.
		- (Detailed home-page placement patterns: to be expanded with examples.)

		## 4. Form-page widget area

		Widgets embedded on a record/form page (e.g. a metrics/analytics strip on an account or case form, or an "Actions dashboard"-style area).

		- Widgets here are contextual to the open record (this account's orders, this case's related metrics).
		- Keep the footprint small: a short metric strip and at most one or two charts, so widgets support the form rather than overwhelm the fields.
		- Respect the form's container column count, not a blanket 12 — a widget area inside a tab or group may have fewer columns. Read the container's actual column count first (call get-guidance name=page-modification, which covers container-column checks, before placing anything).
		- Plain white style still applies.
		- (Detailed form-page placement patterns: to be expanded with examples.)

		## Cross-surface checklist

		- [ ] Chose the right surface for the user's intent (monitor -> dashboard; record-scoped -> form; section-scoped -> list analytics; arrival -> home).
		- [ ] Reused the metric-band -> chart-grid skeleton.
		- [ ] Sized to the surface's actual column count (12 on dashboards; the container's real count on forms).
		- [ ] Plain white everywhere; defaults for titles and value colors.
		- [ ] On list analytics, widgets still read correctly under the active folder/filter.
		""");

	/// <summary>
	/// Returns the thin index guidance article that routes to the deep analytics-widget guidance.
	/// </summary>
	[McpServerResource(UriTemplate = GuideUri, Name = "analytics-widgets-guidance")]
	[Description("Returns the thin index MCP guidance for Freedom UI analytics widgets, routing to dashboards (layout), indicator-widget (payload), page-modification (page structure), and the placement-contexts reference (non-dashboard surfaces).")]
	public ResourceContents GetGuide() => Guide;

	/// <summary>
	/// Returns the placement-contexts reference describing the four analytics-widget surfaces.
	/// </summary>
	[McpServerResource(UriTemplate = DocsScheme + "://" + ReferenceBasePath + "/placement-contexts", Name = "analytics-widgets-placement-contexts-reference")]
	[Description("Returns the analytics-widget placement-contexts reference: dashboard, list-page analytics view, home page, and form-page widget area, with what changes per surface.")]
	public ResourceContents GetPlacementContexts() => PlacementContexts;

	private static TextResourceContents CreateReference(string name, string text) =>
		new() {
			Uri = $"{DocsScheme}://{ReferenceBasePath}/{name}",
			MimeType = "text/plain",
			Text = text
		};
}
