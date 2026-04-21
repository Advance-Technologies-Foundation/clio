using System.Collections.Generic;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Registry of named guidance articles available through <c>get-guidance</c>.
/// </summary>
internal static class GuidanceCatalog {

	/// <summary>
	/// All named guidance entries keyed by canonical article name (case-insensitive).
	/// </summary>
	internal static readonly IReadOnlyDictionary<string, TextResourceContents> Entries =
		new Dictionary<string, TextResourceContents>(System.StringComparer.OrdinalIgnoreCase) {
			["app-modeling"] = AppModelingGuidanceResource.Guide,
			["existing-app-maintenance"] = ExistingAppMaintenanceGuidanceResource.Guide,
			["dataforge-orchestration"] = DataForgeOrchestrationGuidanceResource.Guide,
			["page-schema-validators"] = PageSchemaValidatorsGuidanceResource.Guide,
			["page-modification"] = PageModificationGuidanceResource.Guide
		};
}
