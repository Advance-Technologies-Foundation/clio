using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.McpServer.Resources;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Returns canonical clio MCP guidance articles by stable guide name.
/// </summary>
[McpServerToolType]
public sealed class GuidanceGetTool {
	internal const string ToolName = "get-guidance";

	/// <summary>
	/// Resolves one named guidance article and returns its plain-text content.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Returns canonical clio MCP guidance text by stable guide name so clients do not need to fetch docs:// resources directly. Known names: app-modeling, existing-app-maintenance, dataforge-orchestration, page-schema-handlers, page-schema-converters, page-schema-validators.")]
	public GuidanceGetResponse GetGuidance(
		[Description("Parameters: name (required). Use one of the known guidance names such as page-schema-validators or existing-app-maintenance.")]
		[Required]
		GuidanceGetArgs args) {
		if (GuidanceCatalog.TryGet(args.Name, out GuidanceCatalogEntry entry)) {
			return new GuidanceGetResponse(
				Success: true,
				Guidance: new GuidanceArticle(
					Name: entry.Name,
					Uri: entry.Article.Uri,
					MimeType: entry.Article.MimeType,
					Description: entry.Description,
					Text: entry.Article.Text));
		}

		return new GuidanceGetResponse(
			Success: false,
			Error: $"Unknown guidance '{args.Name}'.",
			AvailableGuides: GuidanceCatalog.GetNames());
	}
}

/// <summary>
/// Request arguments for <c>get-guidance</c>.
/// </summary>
public sealed record GuidanceGetArgs(
	[property: JsonPropertyName("name")]
	[property: Description("Stable guidance name, for example page-schema-validators or existing-app-maintenance.")]
	[property: Required]
	string Name
);

/// <summary>
/// Response envelope for <c>get-guidance</c>.
/// </summary>
public sealed record GuidanceGetResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("guidance")] GuidanceArticle? Guidance = null,
	[property: JsonPropertyName("error")] string? Error = null,
	[property: JsonPropertyName("available-guides")] IReadOnlyList<string>? AvailableGuides = null
);

/// <summary>
/// One resolved guidance article exposed through the MCP tool surface.
/// </summary>
public sealed record GuidanceArticle(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("uri")] string Uri,
	[property: JsonPropertyName("mime-type")] string MimeType,
	[property: JsonPropertyName("description")] string Description,
	[property: JsonPropertyName("text")] string Text
);
