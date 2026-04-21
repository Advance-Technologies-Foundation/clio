using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Resources;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class GuidanceGetTool {

	internal const string ToolName = "get-guidance";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Returns a named clio MCP guidance article, or lists all available guide names when the requested name is unknown.")]
	public Task<GuidanceGetResponse> GetGuidance(
		[Description("Parameters: name (required).")]
		[Required] GuidanceGetArgs args,
		CancellationToken cancellationToken = default) {
		if (GuidanceCatalog.Entries.TryGetValue(args.Name, out var article)) {
			return Task.FromResult(new GuidanceGetResponse {
				Success = true,
				Article = new GuidanceArticle {
					Name = args.Name,
					Uri = article.Uri,
					Text = article.Text
				}
			});
		}
		return Task.FromResult(new GuidanceGetResponse {
			Success = false,
			Error = $"Unknown guidance name '{args.Name}'. Use one of the listed names.",
			AvailableGuides = GuidanceCatalog.Entries.Keys.ToList()
		});
	}
}

public sealed record GuidanceGetArgs(
	[property: JsonPropertyName("name")]
	[property: Description("Guidance article name. Use one of the names returned in 'availableGuides' when unknown.")]
	[property: Required]
	string Name
);

/// <summary>
/// Response from the <c>get-guidance</c> MCP tool.
/// </summary>
public sealed class GuidanceGetResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("error")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }

	[JsonPropertyName("article")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public GuidanceArticle? Article { get; init; }

	[JsonPropertyName("availableGuides")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public List<string>? AvailableGuides { get; init; }
}

/// <summary>
/// A single named guidance article returned by <c>get-guidance</c>.
/// </summary>
public sealed class GuidanceArticle {
	[JsonPropertyName("name")]
	public string Name { get; init; }

	[JsonPropertyName("uri")]
	public string Uri { get; init; }

	[JsonPropertyName("text")]
	public string Text { get; init; }
}
