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
		new Dictionary<string, GuidanceCatalogEntry>(StringComparer.OrdinalIgnoreCase) {
			["app-modeling"] = Create(
				"app-modeling",
				"Canonical MCP guidance for Creatio application modeling, schema design, and page-editing workflows.",
				AppModelingGuidanceResource.Guide),
			["existing-app-maintenance"] = Create(
				"existing-app-maintenance",
				"Canonical MCP guidance for existing-app discovery, inspection, and minimal mutation workflows.",
				ExistingAppMaintenanceGuidanceResource.Guide),
			["dataforge-orchestration"] = Create(
				"dataforge-orchestration",
				"Canonical MCP guidance for DataForge orchestration across active and passive enrichment flows.",
				DataForgeOrchestrationGuidanceResource.Guide),
			["page-schema-handlers"] = Create(
				"page-schema-handlers",
				"Canonical MCP guidance for creating and editing Freedom UI page handlers inside raw page schema bodies.",
				PageSchemaHandlersGuidanceResource.Guide),
			["page-schema-converters"] = Create(
				"page-schema-converters",
				"Canonical MCP guidance for creating and editing Freedom UI page converters inside raw page schema bodies.",
				PageSchemaConvertersGuidanceResource.Guide),
			["page-schema-validators"] = Create(
				"page-schema-validators",
				"Canonical MCP guidance for creating and editing Freedom UI page validators inside raw page schema bodies.",
				PageSchemaValidatorsGuidanceResource.Guide)
		};

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
