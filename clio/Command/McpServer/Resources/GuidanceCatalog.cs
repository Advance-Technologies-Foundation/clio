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
				"Canonical MCP guidance for Creatio application modeling, schema design, and page-editing workflows.",
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
			["page-schema-validators"] = Create(
				"page-schema-validators",
				"Canonical MCP guidance for creating and editing Freedom UI page validators inside raw page schema bodies.",
				PageSchemaValidatorsGuidanceResource.Guide),
			["page-modification"] = Create(
				"page-modification",
				"Canonical MCP guidance for Freedom UI page modification: replacing-schema concept, bundle.json structure, update-page write modes, multi-app target-package-uid resolution, and container selection.",
				PageModificationGuidanceResource.Guide),
			["agent-execution"] = Create(
				"agent-execution",
				"Canonical MCP guidance for executing approved plans through clio MCP: transport, execution order, branching, and recovery patterns.",
				AgentExecutionGuidanceResource.Guide),
			["support-mode"] = Create(
				"support-mode",
				"Canonical MCP guidance for diagnostic-first execution under support mode: severity routing, confirmation probes, fail-fast evidence, and reporting.",
				SupportModeGuidanceResource.Guide),
			["business-rules"] = Create(
				"business-rules",
				"Canonical MCP guidance for Freedom UI business rules: entity-level and page-level declarative condition-action rules for field/element visibility, editability, required state, and value assignment.",
				BusinessRulesGuidanceResource.Guide),
			["mobile-page-modification"] = Create(
				"mobile-page-modification",
				"Mobile-specific differences from the base page-modification guidance: plain JSON body format (no AMD), Scaffold root element rules, mobile component registry, naming conventions, and template hierarchy.",
				MobilePageGuidanceResource.Guide)
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
