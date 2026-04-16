using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Clio.Common.DataForge;

public enum DataForgeAuthMode {
	None,
	OAuthClientCredentials
}

public sealed record DataForgeConfigRequest {
	public string? ServiceUrl { get; init; }
	public int? TimeoutMs { get; init; }
	public int? SimilarTablesLimit { get; init; }
	public int? LookupResultLimit { get; init; }
	public int? TableRelationshipsLimit { get; init; }
	public string? AuthAppUri { get; init; }
	public string? ClientId { get; init; }
	public string? ClientSecret { get; init; }
	public string? Scope { get; init; }
	public bool AllowSysSettingsAuthFallback { get; init; }
}

public sealed record DataForgeResolvedConfig(
	string ServiceUrl,
	int TimeoutMs,
	int SimilarTablesLimit,
	int LookupResultLimit,
	int TableRelationshipsLimit,
	DataForgeAuthMode AuthMode,
	string? TokenUrl,
	string? ClientId,
	string? ClientSecret,
	string Scope
);

public sealed class DataForgeTargetOptions : EnvironmentOptions {
	public bool AllowSysSettingsAuthFallback { get; set; }

	[Description("OAuth scope for dataforge-service token requests. Defaults to use_enrichment.")]
	public string? Scope { get; set; }
}

/// <summary>
/// Source and target table pair requested for Data Forge relation resolution.
/// </summary>
/// <param name="SourceTable">Source table name.</param>
/// <param name="TargetTable">Target table name.</param>
public sealed record DataForgeRelationPair(
	string SourceTable,
	string TargetTable
);

/// <summary>
/// Aggregation request for the Data Forge context service.
/// </summary>
/// <param name="RequirementSummary">Optional fallback summary used when explicit candidate terms are not provided.</param>
/// <param name="CandidateTerms">Optional table-search terms.</param>
/// <param name="LookupHints">Optional lookup-search hints.</param>
/// <param name="RelationPairs">Optional relation pairs to resolve.</param>
public sealed record DataForgeContextRequest(
	string? RequirementSummary,
	IReadOnlyList<string>? CandidateTerms,
	IReadOnlyList<string>? LookupHints,
	IReadOnlyList<DataForgeRelationPair>? RelationPairs
);

public sealed record DataForgeErrorResult(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("message")] string Message
);

public sealed record SimilarTableResult(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("description")] string? Description
);

public sealed record SimilarLookupResult(
	[property: JsonPropertyName("lookup-id")] string LookupId,
	[property: JsonPropertyName("schema-name")] string SchemaName,
	[property: JsonPropertyName("value")] string Value,
	[property: JsonPropertyName("score")] decimal? Score
);

public sealed record DataForgeColumnResult(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")] string? Caption,
	[property: JsonPropertyName("description")] string? Description,
	[property: JsonPropertyName("data-type")] string DataType,
	[property: JsonPropertyName("required")] bool Required,
	[property: JsonPropertyName("reference-schema-name")] string? ReferenceSchemaName
);

public sealed record DataForgeHealthResult(
	[property: JsonPropertyName("liveness")] bool Liveness,
	[property: JsonPropertyName("readiness")] bool Readiness,
	[property: JsonPropertyName("data-structure-readiness")] bool DataStructureReadiness,
	[property: JsonPropertyName("lookups-readiness")] bool LookupsReadiness,
	[property: JsonPropertyName("correlation-id")] string CorrelationId
);

public sealed record DataForgeMaintenanceStatusResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("error")] string? Error
);

public sealed record DataForgeCoverage(
	[property: JsonPropertyName("health")] bool Health,
	[property: JsonPropertyName("tables")] bool Tables,
	[property: JsonPropertyName("lookups")] bool Lookups,
	[property: JsonPropertyName("relations")] bool Relations,
	[property: JsonPropertyName("table-columns")] bool Columns
);

public sealed record DataForgeHealthResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("correlation-id")] string CorrelationId,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
	[property: JsonPropertyName("error")] DataForgeErrorResult? Error,
	[property: JsonPropertyName("health")] DataForgeHealthResult? Health
);

public sealed record DataForgeStatusResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("correlation-id")] string CorrelationId,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
	[property: JsonPropertyName("error")] DataForgeErrorResult? Error,
	[property: JsonPropertyName("health")] DataForgeHealthResult? Health,
	[property: JsonPropertyName("status")] DataForgeMaintenanceStatusResult? Status
);

public sealed record DataForgeFindTablesResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("correlation-id")] string CorrelationId,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
	[property: JsonPropertyName("error")] DataForgeErrorResult? Error,
	[property: JsonPropertyName("similar-tables")] IReadOnlyList<SimilarTableResult> SimilarTables
);

public sealed record DataForgeFindLookupsResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("correlation-id")] string CorrelationId,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
	[property: JsonPropertyName("error")] DataForgeErrorResult? Error,
	[property: JsonPropertyName("similar-lookups")] IReadOnlyList<SimilarLookupResult> SimilarLookups
);

public sealed record DataForgeRelationsResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("correlation-id")] string CorrelationId,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
	[property: JsonPropertyName("error")] DataForgeErrorResult? Error,
	[property: JsonPropertyName("relations")] IReadOnlyList<string> Relations
);

public sealed record DataForgeColumnsResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("correlation-id")] string CorrelationId,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
	[property: JsonPropertyName("error")] DataForgeErrorResult? Error,
	[property: JsonPropertyName("columns")] IReadOnlyList<DataForgeColumnResult> Columns
);

public sealed record DataForgeMaintenanceResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("correlation-id")] string CorrelationId,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
	[property: JsonPropertyName("error")] DataForgeErrorResult? Error,
	[property: JsonPropertyName("status")] DataForgeMaintenanceStatusResult Status
);

public sealed record DataForgeContextResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("correlation-id")] string CorrelationId,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
	[property: JsonPropertyName("error")] DataForgeErrorResult? Error,
	[property: JsonPropertyName("health")] DataForgeHealthResult? Health,
	[property: JsonPropertyName("status")] DataForgeMaintenanceStatusResult? Status,
	[property: JsonPropertyName("similar-tables")] IReadOnlyList<SimilarTableResult> SimilarTables,
	[property: JsonPropertyName("similar-lookups")] IReadOnlyList<SimilarLookupResult> SimilarLookups,
	[property: JsonPropertyName("relations")] IReadOnlyDictionary<string, IReadOnlyList<string>> Relations,
	[property: JsonPropertyName("columns")] IReadOnlyDictionary<string, IReadOnlyList<DataForgeColumnResult>> Columns,
	[property: JsonPropertyName("coverage")] DataForgeCoverage Coverage
);

/// <summary>
/// Aggregated Data Forge context payload returned by the context service before MCP envelope mapping.
/// </summary>
/// <param name="CorrelationId">Correlation identifier from the health probe.</param>
/// <param name="Warnings">Non-fatal warnings collected during aggregation.</param>
/// <param name="Health">Aggregated health payload.</param>
/// <param name="Status">Maintenance status payload.</param>
/// <param name="SimilarTables">Distinct similar-table results.</param>
/// <param name="SimilarLookups">Distinct similar-lookup results.</param>
/// <param name="Relations">Resolved relation paths keyed by source-target pair.</param>
/// <param name="Columns">Resolved Data Forge column projections keyed by table name.</param>
/// <param name="Coverage">Coverage flags for the aggregated context payload.</param>
public sealed record DataForgeContextAggregationResult(
	string CorrelationId,
	IReadOnlyList<string> Warnings,
	DataForgeHealthResult Health,
	DataForgeMaintenanceStatusResult Status,
	IReadOnlyList<SimilarTableResult> SimilarTables,
	IReadOnlyList<SimilarLookupResult> SimilarLookups,
	IReadOnlyDictionary<string, IReadOnlyList<string>> Relations,
	IReadOnlyDictionary<string, IReadOnlyList<DataForgeColumnResult>> Columns,
	DataForgeCoverage Coverage
);
