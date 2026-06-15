using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// The controlled use-case category vocabulary that Solution A (ENG-91571) owns and Solution D's
/// faceted discovery consumes (umbrella ADR Decision 1). A component's
/// <see cref="ComponentRegistryEntry.Category"/> is a single value from this ~5–15 stable
/// vocabulary so an AI agent can drill down category → components instead of scanning the flat
/// ~200-item catalog. There is no prior hardcoded "CategoryOrder" to reconcile (verified 2026-06-15
/// across clio, the registry generator, and creatio-ui) — this type is the single source. Identifiers
/// are kebab-case and treated as a stable wire contract: rename with care and keep producer data,
/// <see cref="ComponentRegistryEntry.Category"/>, and Solution D's facets in lockstep.
/// </summary>
public static class ComponentCategories {
	/// <summary>
	/// A single taxonomy category: a stable kebab-case identifier plus a one-line description used as
	/// the facet label in Solution D's list-mode discovery.
	/// </summary>
	/// <param name="Id">Stable kebab-case identifier stored on <see cref="ComponentRegistryEntry.Category"/>.</param>
	/// <param name="Description">One-line facet label shown to the agent in faceted discovery.</param>
	public sealed record Category(string Id, string Description);

	/// <summary>Structural containers and layout (tabs, groups, cards, flex/scroll containers).</summary>
	public const string LayoutContainer = "layout-container";

	/// <summary>Editable single-value field bound to a data source (text, number, date, lookup, checkbox).</summary>
	public const string DataInput = "data-input";

	/// <summary>Renders a collection of records (grids, lists, timelines).</summary>
	public const string DataCollection = "data-collection";

	/// <summary>Image, file, and visual media (image input, file list, image galleries).</summary>
	public const string Media = "media";

	/// <summary>Buttons and other action triggers.</summary>
	public const string Action = "action";

	/// <summary>Navigation and menus (menu, breadcrumbs, navigation panels).</summary>
	public const string Navigation = "navigation";

	/// <summary>Charts, dashboards, and analytics widgets.</summary>
	public const string ChartAnalytics = "chart-analytics";

	/// <summary>Filters and search (quick filter, filter builder, search).</summary>
	public const string FilterSearch = "filter-search";

	/// <summary>Progress, notifications, and status indicators.</summary>
	public const string FeedbackStatus = "feedback-status";

	/// <summary>Communication and social surfaces (communication options, feed, comments).</summary>
	public const string Communication = "communication";

	/// <summary>Static text, labels, and headings.</summary>
	public const string TextDisplay = "text-display";

	/// <summary>Utility components that do not fit another category.</summary>
	public const string Utility = "utility";

	private static readonly IReadOnlyList<Category> AllCategories = new[] {
		new Category(LayoutContainer, "Structural containers and layout (tabs, groups, cards, flex/scroll containers)."),
		new Category(DataInput, "Editable single-value fields bound to a data source (text, number, date, lookup, checkbox)."),
		new Category(DataCollection, "Components that render a collection of records (grids, lists, timelines)."),
		new Category(Media, "Image, file, and visual media (image input, file list, image galleries)."),
		new Category(Action, "Buttons and other action triggers."),
		new Category(Navigation, "Navigation and menus (menu, breadcrumbs, navigation panels)."),
		new Category(ChartAnalytics, "Charts, dashboards, and analytics widgets."),
		new Category(FilterSearch, "Filters and search (quick filter, filter builder, search)."),
		new Category(FeedbackStatus, "Progress, notifications, and status indicators."),
		new Category(Communication, "Communication and social surfaces (communication options, feed, comments)."),
		new Category(TextDisplay, "Static text, labels, and headings."),
		new Category(Utility, "Utility components that do not fit another category.")
	};

	private static readonly IReadOnlySet<string> KnownIds =
		AllCategories.Select(category => category.Id).ToHashSet(StringComparer.Ordinal);

	/// <summary>
	/// Gets every category in the controlled vocabulary, in canonical facet order. Solution D renders
	/// this as the full, always-visible facet space in list mode.
	/// </summary>
	public static IReadOnlyList<Category> All => AllCategories;

	/// <summary>
	/// Returns <c>true</c> when <paramref name="categoryId"/> is a member of the controlled vocabulary.
	/// Case-sensitive: the identifiers are a stable kebab-case wire contract, not free-form text.
	/// </summary>
	/// <param name="categoryId">Candidate category identifier (typically <see cref="ComponentRegistryEntry.Category"/>).</param>
	/// <returns><c>true</c> when the identifier is a known category; otherwise <c>false</c>.</returns>
	public static bool IsKnown(string? categoryId) =>
		!string.IsNullOrWhiteSpace(categoryId) && KnownIds.Contains(categoryId);
}
