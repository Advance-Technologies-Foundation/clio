using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for placing, sizing, grouping, and styling Freedom UI
/// analytical widgets (metrics/indicators and charts) on dashboards, so a custom dashboard keeps the
/// native "plain white" card look and the canonical metric-band-then-chart-grid layout, plus the
/// dashboard's auto-generated hidden page data source (DashboardDS) that widgets filter by via a dependencies entry.
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

		       Use this guide whenever you add, arrange, size, group, or style Freedom UI ANALYTICAL widgets
		       (metrics/indicators and charts) on a DASHBOARD so they look native and balanced — matching how
		       Creatio's own product dashboards look. A dashboard is a standalone analytics screen the user opens
		       from the Dashboards section to monitor — the analytics are the destination, not part of working a
		       record. To CREATE a dashboard page (the `BaseDashboardTemplate` schema and its link-back
		       properties) read `dashboard-creation`; this guide owns only the widget design on an existing dashboard.

		       This is a LAYOUT / DESIGN-QUALITY guide. It owns WHERE a widget goes on a dashboard, HOW BIG it is,
		       and HOW it is styled. It does NOT own the runtime payload of any single widget — for the
		       `crt.IndicatorWidget` generation contract (aggregate, static filter, intent -> config translation)
		       and its exact style theme values read the dedicated `indicator-widget` guidance and call
		       `get-component-info`. For the `crt.ChartWidget` generation contract (chart type, series,
		       aggregation, grouping, sorting, static filter, intent -> config translation) read the dedicated
		       `chart-widget` guidance and call `get-component-info`. For surrounding page structure, fields,
		       containers, and accessibility/contrast defer to the general Freedom UI page guidance
		       (`page-modification` and the UI guidelines).

		       It also documents a dashboard-specific structural detail: the auto-generated hidden page data
		       source (`DashboardDS`) that widgets filter by — see the section below.

		       ## Core rules (in priority order)

		       Get these three right for every widget, in this order. Treat each as a hard rule unless the user
		       explicitly overrides it — they are what keep a custom dashboard looking native. They are stated
		       here once as the single source of truth; later sections only add the exact numbers and examples.

		       1. Right PLACE — metric tiles go in a top BAND; charts go in a GRID below. Metrics never go below
		          their charts; charts never sit in the metric band. When a dashboard covers several subject areas,
		          split it into LABELED sections (separated by a header and spacing, never by card color), each
		          section being its own metric band + chart row.
		       2. Right STYLE — PLAIN WHITE card is the default for EVERY widget. A colored card is a rare,
		          deliberate signal for ONE critical KPI (e.g. an SLA-breach counter the business wants impossible
		          to miss) — at most one or two per screen, on metric tiles only, never on charts, and never for
		          variety or to "brand" a section. Keep the theme-default title color; let metric value/icon colors
		          follow the default semantic (red is reserved for overdue / negative / gap counts). Don't hand-pick
		          decorative colors.
		       3. Right SIZE — size by widget TYPE, not by eye (exact column counts per type are in "Widget
		          catalog" below). Metric tiles are 1 row tall and share equal width across the band; charts are
		          ~9 rows tall (`rowSpan`; never below 6). Every row's widget widths must sum to a full 12 columns — less than 12 leaves a
		          visible gap, more than 12 wraps and breaks alignment. Composition (pie/doughnut) breakdowns come
		          in threes.

		       ## The 12-column grid

		       A Creatio dashboard lays widgets on a responsive 12-column grid. Every widget occupies a whole
		       number of columns wide and a whole number of rows tall.

		       - WIDTH is what you tune most — a widget's width is a slice of 12.
		       - HEIGHT is `layoutConfig.rowSpan` (grid rows). Platform defaults: metric tile 3, chart 9, funnel 15,
		         list/pivot 9. HARD FLOOR: a chart, list, or pivot must have `rowSpan` >= 6 — below that it renders
		         unreadably short. Metric/gauge tiles are exempt (they stay ~3). The "rows" elsewhere in this guide
		         are these `rowSpan` values, so "a chart ~3 rows" means the ~9-`rowSpan` default, not literally 3.
		       - Each row of widgets must sum to EXACTLY 12 columns.

		       The only widths you normally need:

		       | Width | Columns | Fits per row | Use for |
		       |---|---|---|---|
		       | One-sixth | 2 | 6 | metric tiles (6-up band) |
		       | One-quarter | 3 | 4 | metric tiles (4-up band) |
		       | One-third | 4 | 3 | charts (default), doughnuts in threes |
		       | Half | 6 | 2 | bar charts with long labels, lists |
		       | Two-thirds | 8 | 1 (+ a 4-col sibling) | a focal chart beside a metric/doughnut column |
		       | Full | 12 | 1 | a wide trend/timeline, a wide list |

		       Sizing math: metric tile width = 12 / (tiles in the band). Use 4 or 6 tiles per band -> 3 or 2
		       columns each. Avoid 5 per row (doesn't divide 12 evenly) — use 4 or 6, or split into two bands.
		       After laying out a row, verify its columns sum to 12 and rebalance (widen a chart, or add/resize
		       siblings) if they don't.

		       ## Canonical layout

		       Every native Creatio dashboard follows the same top-down shape: a metric band on top, then chart
		       rows below, optionally grouped into labeled sections.

		       ```
		       +--------------------------------------------------------------+
		       |  METRIC BAND   [tile][tile][tile][tile]     (4 or 6 tiles)   |  <- 1 row tall
		       +--------------------------------------------------------------+
		       |  CHART ROW     [ chart 4 ][ chart 4 ][ chart 4 ]            |  <- ~3 rows tall
		       |  CHART ROW     [   chart 6   ][   chart 6   ]               |
		       |  CHART ROW     [ doughnut 4 ][ doughnut 4 ][ doughnut 4 ]   |
		       |  WIDE TREND    [          line / timeline 12          ]     |
		       +--------------------------------------------------------------+
		       ```

		       Rules baked into this skeleton:

		       1. Metrics first, always on top — the eye should hit the headline numbers before the breakdowns.
		       2. Charts below, in full-12 rows. Mix widths only so each row still sums to 12.
		       3. Doughnuts grouped in threes.
		       4. Wide trends last / full width when they have many time points.

		       Multi-topic dashboards split into labeled sections, each its own mini-skeleton (metric band + chart
		       row):

		       ```
		       My Calls            <- section header
		         [m][m][m][m]      <- 4 metric tiles (3 cols each)
		         [chart4][chart4][chart4]

		       My Chats            <- section header
		         [m][m][m]
		         [chart4][chart4][chart4]

		       My Cases            <- section header
		         [m][m][m][m][m][m]   <- 6 metric tiles (2 cols each)
		         [chart6][chart6]
		       ```

		       ## Worked patterns (lifted from product dashboards)

		       Use these as ready templates.

		       Pattern A — Section stack: per topic, a 4-up (or 6-up) metric band, then a 3-up
		       chart row of [bar 4][comparison 4][trend 4]. Repeat for each topic with its own header.

		       Pattern B — KPI strip + focus rows:
		       - Top: 6 metric tiles (2 cols each) across the full width.
		       - Then repeated [chart 4][chart 8] rows: a small left chart (status/priority/channel breakdown)
		         beside a wide right chart (trend/activity/dynamics).
		       - Bottom: a 3-up row (4+4+4).

		       Pattern C — Metric column + focal chart:
		       - Left: a 2-wide column of metric tiles stacked (each tile 2 cols -> the column is 4 cols wide),
		         e.g. 4 rows of two tiles.
		       - Right: a focal doughnut at 8 cols spanning the height of that metric column.
		       - Then a 3-up chart row (4+4+4), then [doughnut 4][chart 8].

		       Pattern D — KPI strip + breakdown grid:
		       - Top: 6 metric tiles (2 cols each).
		       - Then [bar 6][bar 6] (two half-width bars).
		       - Then [doughnut 4][doughnut 4][doughnut 4] (by service / by category / by channel).
		       - Then more 6+6 or full-12 trend rows.

		       ## Concept -> widget map

		       Pick the widget from what the user wants to show, not from what looks nice:

		       | The user wants to show... | Widget type |
		       |---|---|
		       | A single number / KPI (count, sum, average, min/max) | Metric (indicator) |
		       | How a total splits into parts / shares of 100% | Doughnut / Pie |
		       | A value compared across categories (few categories, or ranking) | Bar / Column chart |
		       | How a value changes over time (trend, dynamics) | Line / Spline chart |
		       | A "X vs Y" comparison (e.g. Accepted vs Missed) | Bar/Column (grouped) or Line if over time |
		       | A short live list of records (top N, my open items) | List widget |
		       | A multi-dimension table of aggregates | Pivot table |
		       | External content (report, page, embed) | Web page widget |

		       ## Widget catalog — type, when to use, default size

		       Every widget keeps the PLAIN WHITE default (see Core rules); only the rare emphasized-KPI exception
		       differs. Sizes below are in 12-grid columns; height is ~9 `rowSpan` for every chart (floor 6) and ~3 for a
		       metric tile unless noted.

		       Metric (indicator) — a single aggregated value with a caption and a small leading icon; the
		       workhorse of the top band.
		       - When to use: any headline number to see at a glance — counts, sums, averages, min/max, and
		         "without X" gap counts (e.g. "Accounts without primary contact").
		       - Examples: "Total calls — 3", "Average handle time, sec — 160", "Overdue cases (response) — 1".
		       - Size: 2 columns (6-up band) or 3 columns (4-up band); height 1 row. Only in the top band, never
		         interleaved with charts.
		       - For the runtime payload (aggregate, static filter, parentName placement, theme presets) read the
		         `indicator-widget` guidance.

		       Bar / Column chart — compares a measure across categories.
		       - When to use: ranking or comparing discrete categories; grouped "X vs Y". Vertical (column) for few
		         categories with short labels (e.g. "Accounts by category"); horizontal (bar) for many categories
		         or long labels like people/accounts/services (e.g. "Orders by owner").
		       - Size: 4 columns when grouped 3-per-row; 6 columns when labels are long or it's primary.
		       - Series use the theme palette (a single blue or magenta series by default); don't recolor bars per
		         value.

		       Doughnut / Pie chart — composition (shares of a whole that sum to 100%).
		       - When to use: part-to-whole with a small number of slices (<= ~6). For many categories use a bar
		         chart instead. Examples: "Cases by service", "Cases by category", "Cases by channel".
		       - Size: 4 columns, grouped 3 per row (by service / by category / by channel side by side). A single
		         focal doughnut may take 8 columns beside a metric column.
		       - Don't use for time series, for > 6 slices, or for values that don't sum to a meaningful whole.

		       Line / Spline chart — how a measure moves over time.
		       - When to use: trends, dynamics, "by month/week/day"; "X vs Y" when both are time series. Examples:
		         "Call volume trend", "Customer satisfaction score dynamics".
		       - Size: 4 columns when grouped with sibling charts; 6-12 columns for a primary timeline with many
		         points (wider = more readable). Trends often sit beside or directly under the metrics they explain.

		       List widget — a compact live list of records (top N, "my open ...").
		       - When to use: when the user must see and act on individual records, not an aggregate (e.g. "My
		         overdue cases", "Latest leads").
		       - Size: 6-12 columns (lists need horizontal room); height ~5-10 visible rows. Keep the column set
		         minimal.

		       Pivot table — a table of aggregates across two dimensions (e.g. cases by priority x channel).
		       - When to use: only when a chart can't convey the two-dimensional aggregate clearly.
		       - Size: 6-12 columns; height by row count.

		       Web page widget — embeds external content / a URL.
		       - When to use: surfacing an external report or page; avoid when a native widget can show the same
		         data.
		       - Size: 6-12 columns, height generous.

		       ## Styling rationale

		       PLAIN WHITE is the native default because a customization should not visually stand out unless a
		       business priority requires it — it keeps a custom dashboard consistent with the base product. The
		       colored-card exception and the title/value color rules live in Core rule 2; don't restate them with
		       different limits. When a colored background IS used, verify text/contrast against the
		       accessibility/contrast guidance. The exact indicator-widget `theme` / `layout.color` values are
		       owned by the `indicator-widget` guidance.

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

		       ## Finish checklist

		       - Metric tiles are in a top band, equal width, one row tall, 4 or 6 across (never 5).
		       - Every chart row sums to exactly 12 columns — no row at 8 or 10 (dead space) or 14 (wrap).
		       - Doughnut/pie breakdowns are grouped (ideally 3 per row at 4 cols); no lone doughnut floating at an
		         odd width.
		       - No chart sits in the metric band, and no metric tile is dropped among the charts.
		       - Every widget uses the plain white style (no stray colored cards for variety or branding).
		       - Each chart/list/pivot meets the `rowSpan` floor (>= 6; default 9, funnel 15); metric/gauge tiles stay short (~3).
		       - Multi-topic dashboards are split into labeled sections, each = metric band + chart row.
		       - Titles and value colors use theme defaults (red only for overdue/negative).
		       - If the dashboard has a `DashboardsEntitySchemaName`, every data-bound widget has a `dependencies`
		         entry to `DashboardDS.Id` (`attributePath` = `Id` when the widget entity matches it, else its FK column).
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for placing, sizing, grouping, and styling Freedom UI
	/// dashboard widgets.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "dashboard-design-guidance")]
	[Description("Returns canonical MCP guidance for laying out, sizing, grouping, and styling Freedom UI analytical widgets (metrics and charts) on dashboards, including the DashboardDS data source widgets filter by.")]
	public ResourceContents GetGuide() => Guide;
}
