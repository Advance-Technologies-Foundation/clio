using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Knowledge;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Returns canonical clio MCP guidance articles by stable guide name.
/// </summary>
[McpServerToolType]
internal sealed class GuidanceGetTool {
	internal const string ToolName = "get-guidance";

	private readonly IKnowledgeGuidanceSource _guidanceSource;

	/// <summary>
	/// Initializes a new instance of the <see cref="GuidanceGetTool"/> class.
	/// </summary>
	/// <param name="guidanceSource">Resolves embedded and externally delivered guidance without fallback.</param>
	public GuidanceGetTool(IKnowledgeGuidanceSource guidanceSource) {
		_guidanceSource = guidanceSource ?? throw new ArgumentNullException(nameof(guidanceSource));
	}

	// Compatibility-only constructor for legacy embedded-guide tests; production resolves the source from DI.
#pragma warning disable CLIO001
	internal GuidanceGetTool(IFeatureToggleService featureToggleService)
		: this(new KnowledgeGuidanceSource(
			featureToggleService,
			new NoOpKnowledgeBundleActivator(),
			new UnavailableKnowledgeBundleRuntime())) {
	}
#pragma warning restore CLIO001

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
		+ "page-modification, theming, and page-schema-handlers plus composable-app skill "
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
					AvailableGuides = _guidanceSource.GetNames().ToList()
				});
			}
			KnowledgeArticleLookup lookup = _guidanceSource.FindByName(effectiveName);
			if (lookup.Status == KnowledgeArticleLookupStatus.Active) {
				return Task.FromResult(new GuidanceGetResponse {
					Success = true,
					Hint = aliasHint,
					Article = new GuidanceArticle {
						Name = lookup.Article!.Name,
						Uri = lookup.Article.Uri,
						Text = lookup.Article.Text,
						LibraryId = lookup.Provenance?.LibraryId,
						ItemId = lookup.Provenance?.ItemId,
						TopicId = lookup.Provenance?.TopicId,
						Sequence = lookup.Provenance?.Sequence,
						BundleDigest = lookup.Provenance?.BundleDigest,
						SourceAlias = lookup.Provenance?.SourceAlias,
						LocalPath = lookup.Provenance?.LocalPath
					}
				});
			}
			if (lookup.Status == KnowledgeArticleLookupStatus.Ambiguous) {
				return Task.FromResult(new GuidanceGetResponse {
					Success = false,
					ErrorCode = KnowledgeGuidanceAmbiguousException.ErrorCode,
					Error = lookup.Diagnostic,
					AvailableGuides = _guidanceSource.GetNames().ToList()
				});
			}
			if (lookup.Status == KnowledgeArticleLookupStatus.Unavailable) {
				return Task.FromResult(new GuidanceGetResponse {
					Success = false,
					ErrorCode = KnowledgeGuidanceUnavailableException.ErrorCode,
					Error = $"Guidance '{effectiveName}' is unavailable because no compatible verified knowledge bundle is active.",
					AvailableGuides = _guidanceSource.GetNames().ToList()
				});
			}
			return Task.FromResult(new GuidanceGetResponse {
				Success = false,
				ErrorCode = "guidance-not-found",
				Error = $"Unknown guidance '{effectiveName}'. Use one of availableGuides.",
				AvailableGuides = _guidanceSource.GetNames().ToList()
			});
		} catch (Exception ex) {
			return Task.FromResult(new GuidanceGetResponse {
				Success = false,
				Error = SensitiveErrorTextRedactor.Redact($"get-guidance failed: {ex.Message}. Expected args: {{\"name\": \"<guide>\"}}."),
				AvailableGuides = _guidanceSource.GetNames().ToList()
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

	[JsonPropertyName("errorCode")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ErrorCode { get; init; }

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

	/// <summary>Gets the stable publisher library identifier for externally delivered guidance.</summary>
	[JsonPropertyName("libraryId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? LibraryId { get; init; }

	/// <summary>Gets the stable item identifier inside the publisher library.</summary>
	[JsonPropertyName("itemId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ItemId { get; init; }

	/// <summary>Gets the logical topic used for deterministic cross-library resolution.</summary>
	[JsonPropertyName("topicId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? TopicId { get; init; }

	/// <summary>Gets the signed generation sequence for the selected library.</summary>
	[JsonPropertyName("sequence")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ulong? Sequence { get; init; }

	/// <summary>Gets the verified digest of the selected bundle generation.</summary>
	[JsonPropertyName("bundleDigest")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? BundleDigest { get; init; }

	/// <summary>Gets the operator-defined trusted-source alias.</summary>
	[JsonPropertyName("sourceAlias")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? SourceAlias { get; init; }

	/// <summary>Gets the readable installed content path when the article came from disk.</summary>
	[JsonPropertyName("localPath")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? LocalPath { get; init; }
}
