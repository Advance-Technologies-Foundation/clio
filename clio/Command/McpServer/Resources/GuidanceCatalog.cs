using System;
using System.Collections.Generic;
using System.Linq;
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
			["esq"] = Create(
				"esq",
				"Canonical MCP guidance for EntitySchemaQuery authoring: the DataService SelectQuery envelope, columns/select, expression building blocks, forward/backward reference column-path grammar, aggregations, and master enum tables.",
				EsqGuidanceResource.Guide),
			["esq-filters"] = Create(
				"esq-filters",
				"Canonical MCP guidance for ESQ-style filter authoring: every filter type and comparison operator, value shapes per column type, the full date/time macro catalog, lookup-value handling, forward and backward references, and common generation pitfalls.",
				EsqFiltersGuidanceResource.Guide),
			["indicator-widget"] = Create(
				"indicator-widget",
				"Canonical MCP guidance for Freedom UI indicator widgets: Copilot-intent to runtime payload translation, aggregate selection, and static filter authoring.",
				IndicatorWidgetGuidanceResource.Guide),
			["dashboards"] = Create(
				"dashboards",
				"Canonical MCP guidance for placing, sizing, grouping, and styling Freedom UI analytical widgets (metrics and charts) on dashboards: the 12-column grid, the metric-band-then-chart-grid skeleton, section grouping, per-widget-type default sizes, and the plain-white default card style.",
				DashboardGuidanceResource.Guide),
			["related-list"] = Create(
				"related-list",
				"Canonical MCP guidance for adding a Freedom UI related/child list and filtering it by the current page record (master-detail \"filter by page data\"): the declarative, dependencies-based scoping — no handler. Fetch the 'Expanded list' composite structure via get-component-info.",
				RelatedListGuidanceResource.Guide),
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
				"Canonical MCP guidance for describe-environment: the single source-independent environment report (coreVersion, db engine, framework, productName, licenseInfo, locale/workspace metadata), which source supplies each field, and the cliogate / CanManageSolution prerequisites.",
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
			["sys-settings"] = Create(
				"sys-settings",
				"Canonical MCP guidance for the Creatio sys-settings CRU surface: tool order, supported value-type-names and aliases, Lookup resolution, SecureText masking, Date/Time TZ caveat, and Binary exclusion.",
				SysSettingsGuidanceResource.Guide),
			["ui-project"] = Create(
				"ui-project",
				"Canonical MCP guidance for scaffolding a Freedom UI Angular remote-module project inside an existing clio workspace via new-ui-project: required arguments, naming constraints, file placement, and the create-workspace prerequisite.",
				WorkspaceUiProjectGuidanceResource.Guide),
			["run-process-button"] = Create(
				"run-process-button",
				"Canonical MCP guidance for adding a Freedom UI button that runs a business process "
				+ "(crt.RunBusinessProcessRequest) via update-page: get-process-signature first, parameter "
				+ "key = CODE not caption (silent-skip warning), and the static-constant / "
				+ "view-model-attribute-binding / current-record variants.",
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
				ServerToServerOAuthGuidanceResource.Guide)
		};

		foreach (ComposableAppSkillResourceEntry guide in ComposableAppSkillResourceCatalog.GetGuides()) {
			entries[guide.Skill] = new GuidanceCatalogEntry(guide.Skill, guide.Description, guide.Article);
		}

		return entries;
	}

	/// <summary>
	/// Returns the stable set of registered guidance names.
	/// </summary>
	internal static IReadOnlyList<string> GetNames() => Entries.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

	/// <summary>
	/// Tries to resolve one guidance article by its canonical name.
	/// </summary>
	internal static bool TryGet(string name, out GuidanceCatalogEntry entry) => Entries.TryGetValue(name, out entry);

	private static GuidanceCatalogEntry Create(string name, string description, ResourceContents contents) {
		if (contents is not TextResourceContents article) {
			throw new InvalidOperationException(
				$"Guidance '{name}' must return {nameof(TextResourceContents)}.");
		}

		return new GuidanceCatalogEntry(name, description, article);
	}
}

/// <summary>
/// Metadata and content for one named clio MCP guidance article.
/// </summary>
internal sealed record GuidanceCatalogEntry(
	string Name,
	string Description,
	TextResourceContents Article
);
