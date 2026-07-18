using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.McpServer.Resources.ProcessDesigner;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical catalog of clio MCP guidance articles exposed both as MCP resources and tool-readable entries.
/// </summary>
internal static class GuidanceCatalog {
	private static readonly IReadOnlyDictionary<string, GuidanceCatalogEntry> Entries =
		CreateEntries();

	private static IReadOnlyDictionary<string, GuidanceCatalogEntry> CreateEntries() {
		Dictionary<string, GuidanceCatalogEntry> entries = new(StringComparer.OrdinalIgnoreCase) {
			["core-rules"] = Create(
				"core-rules",
				"The non-negotiable clio MCP invariants (compile/restart, long-running await, profile culture, destructive confirmation, correlation-id) that apply to every operation. The server instructions mandate reading this first on any operation.",
				CoreRulesGuidanceResource.Guide),
			["routing"] = Create(
				"routing",
				"The canonical clio MCP routing map: a two-level, names-only table that maps a task (pages, entities, data, applications) to the get-guidance article(s) to read before acting. The server instructions mandate reading this first on any operation.",
				RoutingGuidanceResource.Guide),
			["app-modeling"] = Create(
				"app-modeling",
				"Canonical MCP guidance for Creatio application modeling, schema design, and page modification workflows.",
				AppModelingGuidanceResource.Guide),
			["virtual-entities"] = Create(
				"virtual-entities",
				"Canonical guidance for creating and verifying a Creatio virtual entity object, implementing its IEntityQueryExecutor, and version-gating EntityEventListener writes to Creatio 10.0+.",
				VirtualEntitiesGuidanceResource.Guide),
			["data-bindings"] = Create(
				"data-bindings",
				"Canonical MCP guidance for generic lookup seeding and local or remote data-binding workflows.",
				DataBindingsGuidanceResource.Guide),
			["existing-app-maintenance"] = Create(
				"existing-app-maintenance",
				"Canonical MCP guidance for existing-app discovery, inspection, and minimal mutation workflows.",
				ExistingAppMaintenanceGuidanceResource.Guide),
			["dataforge-orchestration"] = Create(
				"dataforge-orchestration",
				"Canonical MCP guidance for DataForge orchestration across active and passive enrichment flows.",
				DataForgeOrchestrationGuidanceResource.Guide),
			["configuration-webservice"] = Create(
				"configuration-webservice",
				"Canonical MCP guidance for implementing Creatio configuration web services.",
				ConfigurationWebServiceGuidanceResource.Guide),
			["configuration-webservice-tests"] = Create(
				"configuration-webservice-tests",
				"Canonical MCP guidance for testing Creatio configuration web services.",
				ConfigurationWebServiceTestsGuidanceResource.Guide),
			["integration-testing"] = Create(
				"integration-testing",
				"Canonical guidance for portable Creatio integration tests, CI authentication, ATF.Repository, Allure, process scenarios, cleanup, and optional browser testing.",
				IntegrationTestingGuidanceResource.Guide),
			["page-schema-converters"] = Create(
				"page-schema-converters",
				"Canonical MCP guidance for creating and editing Freedom UI page converters inside raw page schema bodies.",
				PageSchemaConvertersGuidanceResource.Guide),
			["page-schema-handlers"] = Create(
				"page-schema-handlers",
				"Canonical MCP guidance for creating and editing Freedom UI page handlers inside raw page schema bodies.",
				PageSchemaHandlersGuidanceResource.Guide),
			["page-schema-creatio-devkit-common"] = Create(
				"page-schema-creatio-devkit-common",
				"Canonical MCP guidance for using @creatio-devkit/common in Freedom UI page handlers validators and related frontend-source patterns.",
				PageSchemaCreatioDevkitCommonGuidanceResource.Guide),
			["page-schema-resources"] = Create(
				"page-schema-resources",
				"Canonical MCP guidance for Freedom UI page localizable strings: $Resources.Strings.* as the recommended binding, #ResourceString()# required for validator params (and conventional for grid captions), resources parameter usage, and common mistakes.",
				PageSchemaResourcesGuidanceResource.Guide),
			["page-schema-validators"] = Create(
				"page-schema-validators",
				"Canonical MCP guidance for creating and editing Freedom UI page validators inside raw page schema bodies.",
				PageSchemaValidatorsGuidanceResource.Guide),
			["page-creation"] = Create(
				"page-creation",
				"Canonical MCP guidance for creating Freedom UI pages from supported templates via create-page: the list-page-templates -> create-page -> get-page flow, the supported web/mobile template catalog, required and optional inputs, validation/failure modes, and designer-type mapping.",
				PageCreationGuidanceResource.Guide),
			["page-modification"] = Create(
				"page-modification",
				"Entry guidance for Freedom UI page modification: the mandatory pre-edit GATE checklist and canonical flow, routing the detailed mechanics to the page-modification-overview / -field-contract / -containers / -components sub-guides (each kept small so one get-guidance response fits the agent token limit).",
				PageModificationGuidanceResource.Guide),
			["page-modification-overview"] = Create(
				"page-modification-overview",
				"Page-body save-lifecycle sub-guide of the page-modification family: canonical get-page/update-page/sync-pages flow, replacing-schema concept, design-package resolution, write modes (replace/append/diff), do-not-resend-full-body, external-modification conflicts, body formatting, and known limitations.",
				PageModificationOverviewGuidanceResource.Guide),
			["page-modification-field-contract"] = Create(
				"page-modification-field-contract",
				"Inserted-field contract sub-guide of the page-modification family: viewModelConfigDiff attribute binding, the label/resource rule, static-vs-diff body forms, the canonical NumberInput payload, data-source declaration, and the validation diagnostics emitted on contract violation.",
				PageModificationFieldContractGuidanceResource.Guide),
			["page-modification-containers"] = Create(
				"page-modification-containers",
				"Container-discovery sub-guide of the page-modification family: bundle.json top-level shape, jq recipes for walking viewConfig/containers, and how to pick a valid parentName from bundle.containers for a new component.",
				PageModificationContainersGuidanceResource.Guide),
			["page-modification-components"] = Create(
				"page-modification-components",
				"viewConfigDiff-composition sub-guide of the page-modification family: adding a button with a click handler, the handlers/viewConfigDiff section rules, the column-type-to-control mapping, the canonical add-button flow, and how to read a get-component-info detail response.",
				PageModificationComponentsGuidanceResource.Guide),
			["esq"] = CreateExternal(
				"esq",
				"Canonical MCP guidance for EntitySchemaQuery authoring: the DataService SelectQuery envelope, columns/select, expression building blocks, forward/backward reference column-path grammar, aggregations, and master enum tables.",
				EsqGuidanceResource.ResourceUri),
			["esq-filters"] = CreateExternal(
				"esq-filters",
				"Stable ESQ filter guidance router: choose frontend/DataService construction, native backend C# construction, or runtime C# parsing without duplicating detailed rules.",
				EsqFiltersGuidanceResource.ResourceUri),
			["esq-filters-frontend"] = CreateExternal(
				"esq-filters-frontend",
				"Canonical frontend ESQ filter construction guidance for JavaScript, Freedom UI, page JSON, and DataService SelectQuery payloads.",
				EsqFiltersGuidanceResource.FrontendResourceUri),
			["esq-filters-backend"] = CreateExternal(
				"esq-filters-backend",
				"Canonical and Lab-verified native Creatio backend C# guidance for constructing EntitySchemaQuery filter groups and compare leaves.",
				EsqFiltersBackendGuidanceResource.ResourceUri),
			["esq-filter-parsing"] = CreateExternal(
				"esq-filter-parsing",
				"Lab-verified runtime C# guidance for recursively parsing EntitySchemaQuery.Filters without access to Creatio backend source code.",
				EsqFilterParsingGuidanceResource.ResourceUri),
			["indicator-widget"] = Create(
				"indicator-widget",
				"Canonical MCP guidance for Freedom UI indicator widgets: Copilot-intent to runtime payload translation, aggregate selection, and static filter authoring.",
				IndicatorWidgetGuidanceResource.Guide),
			["chart-widget"] = Create(
				"chart-widget",
				"Canonical MCP guidance for Freedom UI chart widgets: Copilot-intent to runtime payload translation, chart-type selection (bar/column, doughnut/pie, line/spline), series and aggregation rules, and static filter authoring.",
				ChartWidgetGuidanceResource.Guide),
			["dashboards"] = Create(
				"dashboards",
				"The clio MCP dashboards router: a names-only index that routes dashboard work to dashboard-creation (create the page), dashboard-design (widget layout/sizing/styling), and indicator-widget / chart-widget (per-widget payload).",
				DashboardGuidanceResource.Guide),
			["dashboard-creation"] = Create(
				"dashboard-creation",
				"Canonical MCP guidance for creating a Freedom UI dashboard page via create-page with BaseDashboardTemplate, and resolving the DashboardsEntitySchemaName / DashboardsElementName / DashboardsClientUnitSchemaUId optional properties (including the root-schema UId rule).",
				DashboardCreationGuidanceResource.Guide),
			["dashboard-design"] = Create(
				"dashboard-design",
				"Canonical MCP guidance for placing, sizing, grouping, and styling Freedom UI analytical widgets (metrics and charts) on dashboards: the 12-column grid, the metric-band-then-chart-grid skeleton, section grouping, per-widget-type default sizes, the plain-white default card style, and the DashboardDS data source widgets filter by.",
				DashboardDesignGuidanceResource.Guide),
			["related-list"] = Create(
				"related-list",
				"Canonical MCP guidance for adding a Freedom UI related/child list and filtering it by the current page record (master-detail \"filter by page data\"): the declarative, dependencies-based scoping — no handler. Fetch the 'Expanded list' composite structure via get-component-info.",
				RelatedListGuidanceResource.Guide),
			["related-page-binding"] = Create(
				"related-page-binding",
				"Canonical MCP guidance for binding Freedom UI pages to an object via create-related-page-addon: choosing the default record page and the add-record page (optionally per role and type), name discovery, the replace-not-merge semantics, and error handling.",
				RelatedPageBindingGuidanceResource.Guide),
			["agent-execution"] = Create(
				"agent-execution",
				"Canonical MCP guidance for executing approved plans through clio MCP: transport, execution order, branching, and recovery patterns.",
				AgentExecutionGuidanceResource.Guide),
			["deploy-lifecycle"] = Create(
				"deploy-lifecycle",
				"Canonical MCP guidance for the Creatio deploy/provisioning lifecycle: assert-infrastructure -> show-passing-infrastructure -> find-empty-iis-port -> deploy-creatio/deploy-identity, plus build discovery, registration, IdentityService, and cliogate installation.",
				DeployLifecycleGuidanceResource.Guide),
			["describe-environment"] = Create(
				"describe-environment",
				"Canonical MCP guidance for describe-environment: classified base-probe failures, the single source-independent environment report (coreVersion, db engine, framework, productName, licenseInfo, locale/workspace metadata), which source supplies each field, and the cliogate / CanManageSolution prerequisites.",
				DescribeEnvironmentGuidanceResource.Guide),
			["support-mode"] = Create(
				"support-mode",
				"Canonical MCP guidance for diagnostic-first execution under support mode: severity routing, confirmation probes, fail-fast evidence, and reporting.",
				SupportModeGuidanceResource.Guide),
			["business-rules"] = Create(
				"business-rules",
				"Canonical MCP guidance for Freedom UI business rules: entity-level and page-level declarative condition-action rules for field/element visibility, editability, required state, and value assignment.",
				BusinessRulesGuidanceResource.Guide),
			["business-rule-filters"] = Create(
				"business-rule-filters",
				"Canonical MCP guidance for the apply-static-filter friendly filter contract: leaf comparisons, lookup values, forward paths, nested groups, backward EXISTS/NOT_EXISTS and COUNT/SUM/AVG/MIN/MAX aggregations, relative-date and current-user macros, age/birthday translation, multilingual handling, and the discovery flow.",
				BusinessRuleFiltersGuidanceResource.Guide),
			["mobile-page-modification"] = Create(
				"mobile-page-modification",
				"Mobile-specific differences from the base page-modification guidance: plain JSON body format (no AMD), Scaffold root element rules, mobile component registry, naming conventions, and template hierarchy.",
				MobilePageGuidanceResource.Guide),
			["desktop-page"] = Create(
				"desktop-page",
				"Canonical MCP guidance for Creatio desktop pages (desktop-selector workspaces): create-page with template CentralAreaDesktopTemplate, the Desktop schema-group invariant, automatic Desktop-entity registration by the platform, the FixedGridSlot_qwe4asds editable-slot rule, and record-rights-based selector visibility.",
				DesktopPageGuidanceResource.Guide),
			["sys-settings"] = Create(
				"sys-settings",
				"""
				Canonical MCP guidance for the Creatio sys-settings CRU surface: tool order, supported value-type-names and aliases, 
				Lookup resolution, SecureText masking, Date/Time TZ caveat, and Binary (write-only, via value-file-path) upload.
				""",
				SysSettingsGuidanceResource.Guide),
			["ui-project"] = Create(
				"ui-project",
				"""
				Canonical MCP guidance for scaffolding a Freedom UI Angular remote-module project inside an 
				existing clio workspace via new-ui-project: required arguments, naming constraints, file placement, 
				and the create-workspace prerequisite.
				""",
				WorkspaceUiProjectGuidanceResource.Guide),
			["process-modeling"] = Create(
				"process-modeling",
				"""
				Canonical MCP guidance for designing Creatio business processes (BPMN):
				the determinism contract (clio makes no LLM call; the agent owns intent->BPMN translation),
				the element catalog (data-id/label/purpose/setup fields), connection rules R1-R17 + can/can't matrix,
				the validate-then-drive build recipe, the buildable slice (Simple/Signal start, end, user tasks +
				sequence flows — gateways/conditional flows/timers not yet), and the modify-safety rules for
				editing an existing process.
				""",
				ProcessModelingGuidanceResource.Guide,
				featureGateType: typeof(ProcessModelingGuidanceResource)),
			["theming"] = Create(
				"theming",
				"Canonical MCP guidance for managing custom Creatio themes with clio — create, restyle, delete, list, and set the default — and shipping them to a Creatio environment.",
				ThemingGuidanceResource.Guide),
			["run-process-button"] = Create(
				"run-process-button",
				"""
				Canonical MCP guidance for adding a Freedom UI button that runs a business process
				(crt.RunBusinessProcessRequest) via update-page: get-process-signature first, parameter
				key = CODE not caption (silent-skip warning), and the static-constant /
				view-model-attribute-binding / current-record variants.
				""",
				RunProcessButtonGuidanceResource.Guide),
			["identity-assertion"] = Create(
				"identity-assertion",
				"Canonical MCP guidance for the Creatio identity-assertion / Identity Service V3 token-exchange "
				+ "flow used by the embedded AI chat: onboarding sequence (regenerate key, export public JWK, "
				+ "register with V3, issue assertion, exchange), the EnableIdentityAssertionIssuer feature and "
				+ "CanManageIdentityAssertionIssuer permission prerequisites, the four clio tools, and troubleshooting.",
				IdentityAssertionGuidanceResource.Guide),
			["server-to-server-oauth"] = Create(
				"server-to-server-oauth",
				"Canonical MCP guidance for using Creatio server-to-server OAuth client credentials: "
				+ "minting client_credentials tokens, handling expiry without refresh tokens, and calling "
				+ "Creatio APIs with an Authorization: Bearer token.",
				ServerToServerOAuthGuidanceResource.Guide),
			["package-dependencies"] = Create(
				"package-dependencies",
				"Canonical MCP guidance for managing Creatio package dependencies: the schema-designer "
				+ "'GetSchemaDesignItem returned an HTML error page' recovery via add-package-dependency "
				+ "(missing dependency on the owner of the extended object's upper layer), the symmetric "
				+ "remove-package-dependency cleanup, and the anti-patterns (no writes into the owning managed "
				+ "package, no raw SQL/OData/DataService dependency edits).",
				PackageDependenciesGuidanceResource.Guide)
		};

		foreach (ComposableAppSkillResourceEntry guide in ComposableAppSkillResourceCatalog.GetGuides()) {
			entries[guide.Skill] = new GuidanceCatalogEntry(
				guide.Skill, guide.Description, guide.Article.Uri, guide.Article, false);
		}

		return entries;
	}

	/// <summary>
	/// Returns the stable set of registered guidance names.
	/// </summary>
	internal static IReadOnlyList<string> GetNames() => Entries.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

	/// <summary>
	/// Returns the stable set of guidance names that are currently visible for the supplied feature-toggle state.
	/// Entries gated behind a disabled feature flag are omitted.
	/// </summary>
	/// <param name="toggles">The feature-toggle service used to evaluate each entry's gate type.</param>
	internal static IReadOnlyList<string> GetNames(IFeatureToggleService toggles) =>
		Entries.Values
			.Where(entry => IsVisible(entry, toggles))
			.Select(entry => entry.Name)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();

	internal static IReadOnlyList<GuidanceCatalogEntry> GetEntries(IFeatureToggleService toggles) =>
		Entries.Values.Where(entry => IsVisible(entry, toggles)).ToArray();

	/// <summary>
	/// Tries to resolve one guidance article by its canonical name, regardless of feature-toggle state.
	/// </summary>
	internal static bool TryGet(string name, out GuidanceCatalogEntry entry) => Entries.TryGetValue(name, out entry);

	/// <summary>
	/// Tries to resolve one currently-visible guidance article by its canonical name. An entry gated behind a
	/// disabled feature flag is treated as unknown (returns <c>false</c>).
	/// </summary>
	/// <param name="name">The canonical guidance name.</param>
	/// <param name="toggles">The feature-toggle service used to evaluate the entry's gate type.</param>
	/// <param name="entry">The resolved entry when visible; otherwise <c>null</c>.</param>
	internal static bool TryGet(string name, IFeatureToggleService toggles, out GuidanceCatalogEntry entry) {
		if (Entries.TryGetValue(name, out entry) && IsVisible(entry, toggles)) {
			return true;
		}
		entry = null;
		return false;
	}

	/// <summary>
	/// Determines whether a catalog entry is visible for the supplied feature-toggle state. An ungated entry
	/// (no <see cref="GuidanceCatalogEntry.FeatureGateType"/>) is always visible and never calls the toggle service.
	/// </summary>
	internal static bool IsVisible(GuidanceCatalogEntry entry, IFeatureToggleService toggles) =>
		entry.FeatureGateType is null || toggles.IsEnabled(entry.FeatureGateType);

	private static GuidanceCatalogEntry Create(string name, string description, ResourceContents contents, Type featureGateType = null) {
		if (contents is not TextResourceContents article) {
			throw new InvalidOperationException(
				$"Guidance '{name}' must return {nameof(TextResourceContents)}.");
		}

		return new GuidanceCatalogEntry(name, description, article.Uri, article, false, featureGateType);
	}

	private static GuidanceCatalogEntry CreateExternal(string name, string description, string uri) =>
		new(name, description, uri, null, true);
}

/// <summary>
/// Metadata and content for one named clio MCP guidance article.
/// </summary>
internal sealed record GuidanceCatalogEntry(
	string Name,
	string Description,
	string Uri,
	TextResourceContents? Article,
	bool IsExternal,
	Type FeatureGateType = null
);
