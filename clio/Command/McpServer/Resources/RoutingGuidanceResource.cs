using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// The canonical routing map: a names-only table — grouped into two levels by domain (pages, entities,
/// data, applications) so the AI first picks the domain, then the row — that points at the matching
/// <c>get-guidance</c> article. Never duplicate the routed guides' content here.
/// </summary>
[McpServerResourceType]
public sealed class RoutingGuidanceResource(IFeatureToggleService featureToggleService) {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/routing";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>Canonical guidance name this map is served under through <c>get-guidance</c>.</summary>
	internal const string GuideName = "routing";

	// The always-on map. The request-wiring rows are inserted right after this Pages sub-row (and only while
	// the requests-registry feature is on), so the anchor must stay an exact, unique substring of the map.
	private const string PageBusinessRulesRow =
		"         - page business rules (create/change/remove; visibility/required/value) -> name=business-rules";

	// Gated behind requests-registry: routing an agent here while the feature is off would point at
	// get-request-info / when-to-use-requests, both hidden by the same gate. The nine-space sub-row indent
	// matches the rendered map's Pages sub-rows so the rows slot in seamlessly after the anchor.
	private const string RequestWiringRows =
		"\n         - wire a button/menu action to a platform request (crt.*Request: print, close, cancel, ...) -> get-request-info + name=when-to-use-requests"
		+ "\n         - add a button/menu item that runs a business process -> get-process-signature FIRST + get-request-info (crt.RunBusinessProcessRequest)";

	private const string BaseMap = """
		clio MCP routing map

		Map the task to the guide(s) you MUST read with get-guidance before planning or mutating.
		Pick the domain, then the row (get-guidance name=...; an unknown name returns availableGuides).

		       - Pages (Freedom UI): create/edit -> get-component-info (read resolvedFrom) + name=page-modification
		         - page-modification is the entry; after its GATE read the ONE matching sub-guide: name=page-modification-overview (save lifecycle), name=page-modification-field-contract (insert a data-bound field), name=page-modification-containers (parentName / bundle.json), name=page-modification-components (button/handler/viewConfigDiff rules)
		         - dashboards (create a dashboard page, or lay out / size / style analytics widgets) -> name=dashboards (routes onward to dashboard-creation / dashboard-design)
		         - desktop pages (create/edit a desktop-selector workspace, CentralAreaDesktopTemplate, group Desktop) -> name=desktop-page
		         - page business rules (create/change/remove; visibility/required/value) -> name=business-rules
		         - bind which page opens for a record / which page adds a record (related pages) -> name=related-page-binding
		       - Entities & schemas: create/modify schema, app / schema modeling -> name=app-modeling
		         - virtual entity object, IEntityQueryExecutor reads, or EntityEventListener writes -> name=virtual-entities
		         - schema designer fails with "GetSchemaDesignItem returned an HTML error page" / package dependencies -> name=package-dependencies
		         - entity business rules (create/change/remove) / lookup filtering / dependent fields -> name=business-rules; static filters -> name=business-rule-filters
		       - Data: raw ESQ queries or filter work -> name=esq AND name=esq-filters
		         - esq-filters is the entry router; it selects name=esq-filters-frontend (JavaScript/page JSON/DataService), name=esq-filters-backend (native backend C# construction), or name=esq-filter-parsing (runtime C# interpretation)
		         - lookup seeding / data bindings -> name=data-bindings
		       - Applications, deploy & ops: deploy & provisioning -> name=deploy-lifecycle
		         - integration tests / ATF.Repository / Allure / process tests -> name=integration-testing
		         - environment inspection (version / db engine / framework / product / license) -> name=describe-environment
		         - executing an approved plan -> name=agent-execution
		         - identity assertion / Identity Service V3 -> name=identity-assertion
		       - Theming & branding: brand colours / fonts / custom themes (create, restyle, delete, list, set the default) -> name=theming
		""";

	/// <summary>
	/// Builds the routing map, inserting the request-wiring rows only when <paramref name="includeRequestWiring"/>
	/// is <c>true</c> (i.e. the <c>requests-registry</c> feature is enabled).
	/// </summary>
	/// <param name="includeRequestWiring">Whether to advertise the gated request-wiring guides/tools.</param>
	internal static TextResourceContents BuildGuide(bool includeRequestWiring) {
		// ReplaceUnique (not string.Replace) so anchor drift fails loudly in every unit-test run
		// instead of silently omitting the request rows while the feature is on.
		string text = includeRequestWiring
			? GuidanceArticleText.ReplaceUnique(BaseMap, PageBusinessRulesRow, PageBusinessRulesRow + RequestWiringRows)
			: GuidanceArticleText.NormalizeNewlines(BaseMap);
		return new TextResourceContents {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = text
		};
	}

	/// <summary>
	/// The feature-off baseline map (no gated rows). Used as the static <see cref="GuidanceCatalog"/> article;
	/// the feature-aware content is produced by <see cref="BuildGuide"/> at serve time.
	/// </summary>
	internal static readonly TextResourceContents Guide = BuildGuide(includeRequestWiring: false);

	/// <summary>
	/// Returns the canonical routing map that points the AI at the matching guidance article per domain, with
	/// feature-gated rows included only while their feature is enabled.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "routing-guidance")]
	[Description("Returns the canonical clio MCP routing map: a two-level, names-only table that maps a task (pages, entities, data, applications) to the get-guidance article(s) to read before acting.")]
	public ResourceContents GetGuide() =>
		BuildGuide(includeRequestWiring: featureToggleService.IsEnabled(typeof(WhenToUseRequestsGuidanceResource)));
}
