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
				"Canonical MCP guidance for Freedom UI page modification: replacing-schema concept, bundle.json structure, update-page write modes, multi-app target-package-uid resolution, and container selection.",
				PageModificationGuidanceResource.Guide),
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
				"Canonical MCP guidance for adding a Freedom UI related/child list (detail) and filtering it by the current page record: the ExpansionPanel + DataGrid composite, the child EntityDataSource, the isCollection attribute, and the declarative modelConfig.dependencies (attributePath/relationPath) that scopes the list by page data — no handler.",
				RelatedListGuidanceResource.Guide),
			["agent-execution"] = Create(
				"agent-execution",
				"Canonical MCP guidance for executing approved plans through clio MCP: transport, execution order, branching, and recovery patterns.",
				AgentExecutionGuidanceResource.Guide),
			["deploy-lifecycle"] = Create(
				"deploy-lifecycle",
				"Canonical MCP guidance for the Creatio deploy/provisioning lifecycle: assert-infrastructure -> show-passing-infrastructure -> find-empty-iis-port -> deploy-creatio, plus build discovery, registration, and cliogate installation.",
				DeployLifecycleGuidanceResource.Guide),
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
			["ui-guidelines"] = Create(
				"ui-guidelines",
				"Canonical MCP guidance index for designing and reviewing Creatio Freedom UI pages so they look native, are understandable, and meet accessibility expectations; routes to the ui-page-layout, ui-accessibility, and ui-review-checklists leaf guides.",
				UiGuidelinesGuidanceResource.Guide),
			["ui-page-layout"] = Create(
				"ui-page-layout",
				"Canonical MCP guidance for Creatio Freedom UI page layout and controls: the concept-to-component map, grid/column math, container nesting and gap avoidance, grouping, fields, buttons, captions, and list-page container slots.",
				UiGuidelinesGuidanceResource.PageLayout),
			["ui-accessibility"] = Create(
				"ui-accessibility",
				"Canonical MCP guidance for Creatio Freedom UI accessibility and color: WCAG 2.2 AA page criteria, contrast rules, accessible chart/tab/progress-bar palettes, and image/icon text alternatives.",
				UiGuidelinesGuidanceResource.Accessibility),
			["ui-review-checklists"] = Create(
				"ui-review-checklists",
				"Canonical MCP guidance for Creatio Freedom UI audits: the quick audit checklist, audit/design output templates, severity model, and common recommendation snippets.",
				UiGuidelinesGuidanceResource.ReviewChecklists)
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
