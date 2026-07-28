namespace Clio.Command;

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Clio.Command.McpServer.Tools;

// Shared, converter-agnostic page-conversion contract.
// Reused by every page converter (Freedom web -> mobile today; classic web -> Freedom web later).
// Converter-specific logic lives in the per-converter files, for example WebToMobileConversion.
// These types are intentionally general so a second converter can return the same report shape.

/// <summary>
/// Classification of a source component when converting a page (ENG-89620 §3.2 five categories).
/// </summary>
public enum ComponentMappingCategory {
	/// <summary>Component has a direct equivalent on the target (same type).</summary>
	DirectMapping,

	/// <summary>Component can be transferred but requires layout/property/behavior adaptation.</summary>
	WithAdaptation,

	/// <summary>No direct equivalent, but a recommended alternative exists.</summary>
	AlternativeAvailable,

	/// <summary>Component is not supported on the target and was not converted.</summary>
	Unsupported,

	/// <summary>Automatic conversion may produce wrong UX / loss of business meaning; needs a manual decision.</summary>
	RequiresManualDecision
}

/// <summary>
/// One classified component in a conversion report.
/// </summary>
public sealed class ComponentConversionEntry {
	[JsonPropertyName("name")]
	public string Name { get; init; }

	[JsonPropertyName("sourceType")]
	public string SourceType { get; init; }

	[JsonPropertyName("targetType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string TargetType { get; init; }

	[JsonPropertyName("message")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Message { get; init; }
}

/// <summary>
/// Page-level business-rule status in a conversion report. In v1 rules are not analyzed
/// (detect-and-flag for manual review only).
/// </summary>
public sealed class PageConversionBusinessRulesInfo {
	[JsonPropertyName("note")]
	public string Note { get; init; }
}

/// <summary>
/// Structured page-conversion report (ENG-89620 §5). Converter-agnostic.
/// </summary>
public sealed class PageConversionReport {
	[JsonPropertyName("sourcePage")]
	public string SourcePage { get; init; }

	[JsonPropertyName("targetPage")]
	public string TargetPage { get; init; }

	[JsonPropertyName("status")]
	public string Status { get; init; }

	[JsonPropertyName("directMapping")]
	public IReadOnlyList<ComponentConversionEntry> DirectMapping { get; init; } = [];

	[JsonPropertyName("withAdaptation")]
	public IReadOnlyList<ComponentConversionEntry> WithAdaptation { get; init; } = [];

	[JsonPropertyName("alternativeAvailable")]
	public IReadOnlyList<ComponentConversionEntry> AlternativeAvailable { get; init; } = [];

	[JsonPropertyName("unsupported")]
	public IReadOnlyList<ComponentConversionEntry> Unsupported { get; init; } = [];

	[JsonPropertyName("requiresManualDecision")]
	public IReadOnlyList<ComponentConversionEntry> RequiresManualDecision { get; init; } = [];

	[JsonPropertyName("forbiddenSectionsRemoved")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> ForbiddenSectionsRemoved { get; init; }

	[JsonPropertyName("businessRules")]
	public PageConversionBusinessRulesInfo BusinessRules { get; init; }

	[JsonPropertyName("warnings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Warnings { get; init; }

	[JsonPropertyName("recommendedManualActions")]
	public IReadOnlyList<string> RecommendedManualActions { get; init; } = [];

	[JsonPropertyName("recommendedMobileTemplate")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string RecommendedMobileTemplate { get; init; }

	[JsonPropertyName("openInDesigner")]
	public string OpenInDesigner { get; init; }

	[JsonPropertyName("validation")]
	public PageSyncValidationResult Validation { get; init; }
}

/// <summary>
/// Converter-agnostic response envelope for page-conversion MCP tools.
/// </summary>
public sealed class PageConversionResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("sourceSchemaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SourceSchemaName { get; init; }

	[JsonPropertyName("targetSchemaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string TargetSchemaName { get; init; }

	[JsonPropertyName("proposedBodyFile")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ProposedBodyFile { get; init; }

	[JsonPropertyName("report")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageConversionReport Report { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }
}
