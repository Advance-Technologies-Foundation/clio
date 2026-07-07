using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Resources;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Returns canonical clio MCP guidance articles by stable guide name.
/// </summary>
[McpServerToolType]
public sealed class GuidanceGetTool {
	internal const string ToolName = "get-guidance";

	private readonly IFeatureToggleService _featureToggleService;

	/// <summary>
	/// Initializes a new instance of the <see cref="GuidanceGetTool"/> class.
	/// </summary>
	/// <param name="featureToggleService">
	/// Evaluates feature-gated guidance entries so experimental guides stay hidden from the always-on
	/// <c>get-guidance</c> tool while their feature flag is off.
	/// </param>
	public GuidanceGetTool(IFeatureToggleService featureToggleService) {
		_featureToggleService = featureToggleService ?? throw new ArgumentNullException(nameof(featureToggleService));
	}

	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["topic"] = "name",
		["guide"] = "name",
		["guideName"] = "name",
		["guide-name"] = "name",
		["article"] = "name",
		["articleName"] = "name",
		["guidanceName"] = "name"
	};

	/// <summary>
	/// Resolves one named guidance article and returns its plain-text content.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Returns a named clio MCP guidance article, or lists all available guide names when "
		+ "the requested name is unknown. Known names include clio guides such as app-modeling, "
		+ "page-modification, and page-schema-handlers plus composable-app skill "
		+ "guides such as atf-repository-dev, feature-toggle, sys-setting, configuration-webservice, "
		+ "and their test guides. Always read the availableGuides list for the authoritative set.")]
	public Task<GuidanceGetResponse> GetGuidance(
		[Description("Parameters: name (required). Use one of the known guidance names returned in availableGuides, for example atf-repository-dev, feature-toggle-tests, sys-setting, configuration-webservice, page-modification, page-schema-handlers, related-list, esq-filters, or existing-app-maintenance.")]
		[Required] GuidanceGetArgs args,
		CancellationToken cancellationToken = default) {
		try {
			string? effectiveName = args.Name;
			string? aliasHint = null;
			if (string.IsNullOrWhiteSpace(effectiveName) && args.ExtensionData is not null) {
				foreach (string key in args.ExtensionData.Keys.Where(k => LegacyAliases.ContainsKey(k))) {
					JsonElement value = args.ExtensionData[key];
					if (value.ValueKind == JsonValueKind.String) {
						effectiveName = value.GetString();
						aliasHint = $"Accepted '{key}' as 'name' (rename to 'name' in future calls).";
						break;
					}
				}
			}
			if (string.IsNullOrWhiteSpace(effectiveName)) {
				return Task.FromResult(new GuidanceGetResponse {
					Success = false,
					Error = "Missing required parameter 'name'. Pass {\"name\": \"<guide>\"}. See availableGuides for valid values.",
					AvailableGuides = GuidanceCatalog.GetNames(_featureToggleService).ToList()
				});
			}
			if (GuidanceCatalog.TryGet(effectiveName, _featureToggleService, out GuidanceCatalogEntry entry)) {
				return Task.FromResult(new GuidanceGetResponse {
					Success = true,
					Hint = aliasHint,
					Article = new GuidanceArticle {
						Name = entry.Name,
						Uri = entry.Article.Uri,
						Text = entry.Article.Text
					}
				});
			}
			return Task.FromResult(new GuidanceGetResponse {
				Success = false,
				Error = $"Unknown guidance '{effectiveName}'. Use one of availableGuides.",
				AvailableGuides = GuidanceCatalog.GetNames(_featureToggleService).ToList()
			});
		} catch (Exception ex) {
			return Task.FromResult(new GuidanceGetResponse {
				Success = false,
				Error = SensitiveErrorTextRedactor.Redact($"get-guidance failed: {ex.Message}. Expected args: {{\"name\": \"<guide>\"}}."),
				AvailableGuides = GuidanceCatalog.GetNames(_featureToggleService).ToList()
			});
		}
	}
}

/// <summary>
/// Request arguments for <c>get-guidance</c>.
/// </summary>
public sealed record GuidanceGetArgs(
	[property: JsonPropertyName("name")]
	[property: Description("Stable guidance name. Use one of the names returned in 'availableGuides' when unknown, for example atf-repository-dev, feature-toggle-tests, sys-setting, configuration-webservice, page-modification, page-schema-handlers, related-list, esq-filters, or existing-app-maintenance.")]
	string? Name = null
) {
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Response from the <c>get-guidance</c> MCP tool.
/// </summary>
public sealed class GuidanceGetResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }

	[JsonPropertyName("hint")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Hint { get; init; }

	[JsonPropertyName("article")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public GuidanceArticle? Article { get; init; }

	[JsonPropertyName("availableGuides")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
