using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for creating and editing Freedom UI chart widgets through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class ChartWidgetGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/chart-widget";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP chart widget guide

		       Before you create, edit, filter, or troubleshoot a `crt.ChartWidget` on a Freedom UI page,
		       you MUST call `get-component-info` for `crt.ChartWidget` and read its documentation in full,
		       including every reference and link it points to.

		       That component documentation is the single source of truth for chart widgets. It owns the
		       generation contract (the viewConfigDiff insert, per-series aggregation expression, grouping,
		       seriesOrder, scales, and filter shapes), the intent -> runtime config translation, and the
		       authoring workflow, plus the related `esq`, `esq-filters`, `page-modification`, and
		       `page-schema-resources` guidance.

		       Do NOT author or edit a chart widget payload from memory or from this pointer alone — read
		       the `get-component-info` documentation and its references first.

		       ----

		       ## Routing — is a chart the right widget?

		       - A single aggregated value (one number) -> Metric tile (`crt.IndicatorWidget`); see `indicator-widget`.
		       - Multiple aggregated values to compare, break down, or show over time -> chart widget (this guide).
		       - Opportunities/Leads by sales/pipeline stage -> the dedicated pipeline widgets, NOT a chart.
		       - A list of records (top N, "my open ...") -> the list widget.

		       ## Chart type selection (alias to the supported wire values)

		       Supported `series[*].type` wire values: bar, horizontal-bar, line, spline, area, scatter,
		       doughnut, tsfunnel. Apply these aliases when the user names a different type:
		       - column            -> bar        (vertical bars)
		       - pie               -> doughnut
		       - funnel / pipeline -> tsfunnel   (note the non-obvious wire value)
		       Do NOT emit `waterfall` — it has no chart-widget series model and is not generatable here.

		       ## Series, aggregation, grouping, sorting

		       - Each entry in `config.series[]` is independent: its own type, color, label, aggregation, and
		         filter — so multiple series with DIFFERENT aggregation functions are supported.
		       - `series[*].color` colors only the DATA MARKS (bars/lines/slices). It is INDEPENDENT of `config.color`
		         (the title/header accent — see Title and header). REQUIRED for cartesian series
		         (bar/horizontal-bar/line/spline/area/scatter) — omit it and the series renders BLACK; doughnut/tsfunnel
		         ignore it and auto-color slices from the palette.
		       - Aggregation: COUNT aggregates `Id`; SUM/AVG/MIN/MAX use the explicit numeric column (date is
		         also valid for MIN/MAX), never `Id`. MIN/MAX on a DATE column are valid but render as near-equal
		         bars at the axis baseline (misleading) — for a date range prefer a numeric measure or a table. The
		         exact `aggregation.column.expression` shape and enum values are owned by the `esq` guidance.
		       - Grouping is SHARED across all series and is either by value or by date part (never mixed).
		         Group Month together with Year unless the user insists on Month only. The exact grouping
		         column shape (single column for by-value; a date-part array for by-date-part) is in the
		         `get-component-info` documentation.
		       - For a by-date-part (time) chart the date COLUMN is an authoring choice. WHEN THE ENTITY HAS a
		         business/event date that matches the user's intent (e.g. RegisteredOn, OrderDate, ResolvedOn,
		         DueDate), PREFER it over the audit columns CreatedOn/ModifiedOn for "new / over time / dynamics"
		         metrics: CreatedOn/ModifiedOn record when the ROW WAS WRITTEN, so on seeded/migrated/bulk-loaded
		         data they cluster at the data-load date and the chart reflects ingestion, not the business timeline
		         (structurally valid, so nothing flags it). If CreatedOn is the only date, or the user means
		         record-creation time, use it.
		       - Sorting: emit `seriesOrder` ONLY when the user explicitly asks to sort (by count = `by-aggregation-value`,
		         by name/category = `by-grouping-value`; asc/desc per request). Otherwise OMIT it — a by-value (categorical)
		         chart keeps the data source's natural order (the lookup's own order, stable across records); never impose a
		         default sort. Exception: by-date-part (time) charts keep `seriesOrder: { type: "by-grouping-value", direction: 1 }`
		         for chronological order.

		       ## Filters

		       - Static filters live in each series' `data.providing.filters.filter`; the filter/leaf contract
		         is owned by the `esq-filters` guidance. Keep the filter-group envelope even when empty.
		       - "Filter by page data" on a record page is wiring you MUST author — see the next section.

		       ## Filter by page data (record pages) — you MUST wire it

		       On a FormPage, "filter by page data" scopes the chart to the record open on the page. The runtime
		       is implemented, but when you author the body via `update-page` the binding is NOT auto-injected —
		       omit it and the chart shows ALL rows, not just the current record's.

		       Preferred — the mechanism an OOB detail and the OOB IndicatorWidget use (see `related-list`):
		       declare the child->master relation in each series' `data.providing.dependencies` (the exact shape is
		       in the `get-component-info` documentation, "Filter by page data"); the runtime builds the ESQ filter
		       AND waits for the record to load. No handler, no seeded filter, no `filterAttributes`. Read
		       `<primaryDataSourceName>` from the page modelConfigDiff (e.g. "PDS", or "ContactDS" on a Contact
		       page); `attributePath` is the chart entity's lookup column to that record (e.g. "Requester"). The
		       designer-style `config.sectionBindingColumn` + view-level `sectionBindingColumnRecordId: "$Id"`
		       alternative (both required together) is documented there too; prefer the `dependencies` form.

		       ## Display settings — applicability by chart type

		       Axis formatting and `scales.stacked` apply ONLY to cartesian charts
		       (bar, horizontal-bar, line, spline, area, scatter). For doughnut and tsfunnel, OMIT axes and
		       stacked — they are ignored at render. Legend, data labels (show values), per-series color, and
		       numeric precision apply to all chart types.

		       **Data labels — show values by DEFAULT.** Set `dataLabel.display: true` on every series unless the user
		       EXPLICITLY asks not to show values (then set `display: false` or omit `dataLabel`). Applies to all chart types.

		       ## Title and header (never ship a headerless chart)

		       - `config.title` is REQUIRED. Use `#ResourceString(<widgetName>_title)#` AND register that string in the
		         page's localizable strings — an unregistered or empty title renders a header with NO title.
		       - `config.color` is REQUIRED for a VISIBLE title. On `without-fill` (the dashboard/list/form default)
		         the title is drawn in `config.color`; omit it and the color defaults to white, so the title is white
		         and invisible on the white card even though it is set and registered — the usual cause of a
		         "title not showing". Always set `config.color` to a visible `WidgetColor` token (see Style below).
		       - `config.color` (title) is INDEPENDENT of `series[*].color` (data marks). Set or derive `config.color`
		         when CREATING a chart or fixing an invisible title — NOT on every edit. On a MODIFY, touch `config.color`
		         ONLY if the user asked about the title/header/accent color; a request to change a SERIES color changes
		         ONLY that series' `color` and leaves `config.color` untouched. If a color is needed, default to
		         "dark-blue" — never the series color.
		       - The widget header also shows a full-screen button by default. Do NOT emit the hidden `hideTitle` /
		         `hideTools` flags: `hideTitle: true` removes the title, `hideTools: true` removes the header tools
		         (including the full-screen button). Leave both unset so the platform shows the title and full-screen.

		       ## Style (theme) by page surface — match the indicator-widget policy

		       Pick `config.theme` from the surface you add the chart to (this also covers a `tsfunnel`/funnel chart —
		       it is a chart-widget series). If the user named a style/theme in the prompt (plain white, fully colored,
		       glass, or an explicit theme value), use that and IGNORE these defaults.

		       `config.color` is REQUIRED (see Title and header). It colors the title on `without-fill` and the card on
		       `full-fill`. On Home guess it from other components; for glassmorphism mirror the indicator color;
		       otherwise "dark-blue" is a safe default.

		       - **Dashboards** (page inheriting `BaseDashboardTemplate`): plain-white card policy — `theme`: "without-fill".
		         This WINS even if the dashboard sits on a Home page or Desktop. See the `dashboards` guidance.
		       - **Desktops** (`BaseDesktop` in the page hierarchy): `layout.color`: "transparent", `theme`: "glassmorphism".
		       - **Home pages** (`BaseHomePage` in the page hierarchy): `theme`: "full-fill"; guess the color from other
		         components on the page.
		       - **List pages and Form pages** (everything else): `theme`: "without-fill". Never use a transparent color
		         unless the user explicitly asked for glassmorphism (applies to all surfaces).

		       ## Placement and preserving existing widgets

		       - Insert the chart with a UNIQUE view-element `name` via `update-page` in APPEND mode; this
		         preserves the widgets already on the page. The chart self-fetches its data, so the
		         `modelConfigDiff` stays empty.
		       - SIZE FLOOR — never ship a tiny chart. In a `crt.GridContainer` set `layoutConfig.rowSpan` >= 6
		         (platform default 9; funnels 15). In a `crt.FlexContainer` the parent uses `FlexLayoutConfig` —
		         set `layoutConfig.height` >= 350 (px) so the flex child doesn't collapse. The same floor applies
		         to list/pivot widgets; metric/gauge TILES are exempt (they stay ~3 rows).
		         EXCEPTION — on a DESKTOP page (`CentralAreaDesktopTemplate`) the desktop sizing rule replaces
		         this floor: every widget (charts included) may be as short as 3 rows, targeting <= 10 rows total.
		         See `desktop-page`.
		       - For WHERE a chart goes on a dashboard, HOW BIG it is, and HOW it is styled, see the
		         `dashboards` guidance.
		       - On a DESKTOP page whose parent is `CentralAreaDesktopTemplate`, insert the chart into the slot
		         `FixedGridSlot_qwe4asds` (the template's editable area; an ~8-column, 60px-row grid — fixed name, not a
		         per-page id), NOT the top `Main` (the template's locked frame) — else the chart can't be moved, resized,
		         or deleted in the designer. `parentName: "FixedGridSlot_qwe4asds"`. See `desktop-page`.
		       - Before saving, validate each series' aggregation + filter with `execute-esq` against the
		         target environment to confirm the returned data matches the intended metric. For a by-date-part
		         chart, sanity-check the periods against the real data — a single period is FINE when the data
		         genuinely spans one; suspect the date column only if you expected a spread but got the load date.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for Freedom UI chart widgets.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "chart-widget-guidance")]
	[Description("Returns canonical MCP guidance for Freedom UI chart widgets, including Copilot-intent to runtime-config translation, chart-type and series selection, aggregation rules, and static filter authoring patterns.")]
	public ResourceContents GetGuide() => Guide;
}
