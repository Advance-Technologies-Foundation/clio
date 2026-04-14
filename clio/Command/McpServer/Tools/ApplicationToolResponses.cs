using System.Collections.Generic;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Common.DataForge;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Structured application list envelope returned by the application MCP tool family.
/// </summary>
public sealed record ApplicationListResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("applications")] IReadOnlyList<ApplicationListItemResult>? Applications = null,
	[property: JsonPropertyName("error")] string? Error = null);

/// <summary>
/// Structured installed-application item returned by the <c>list-apps</c> MCP tool.
/// </summary>
public sealed record ApplicationListItemResult(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("version")] string Version);

/// <summary>
/// Structured application context envelope returned by application read/create tools.
/// </summary>
public sealed record ApplicationContextResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("package-u-id")] string? PackageUId = null,
	[property: JsonPropertyName("package-name")] string? PackageName = null,
	[property: JsonPropertyName("canonical-main-entity-name")] string? CanonicalMainEntityName = null,
	[property: JsonPropertyName("application-id")] string? ApplicationId = null,
	[property: JsonPropertyName("application-name")] string? ApplicationName = null,
	[property: JsonPropertyName("application-code")] string? ApplicationCode = null,
	[property: JsonPropertyName("application-version")] string? ApplicationVersion = null,
	[property: JsonPropertyName("entities")] IReadOnlyList<ApplicationEntityResult>? Entities = null,
	[property: JsonPropertyName("pages")] IReadOnlyList<PageListItem>? Pages = null,
	[property: JsonPropertyName("dataforge")] ApplicationDataForgeResult? DataForge = null,
	[property: JsonPropertyName("error")] string? Error = null);

/// <summary>
/// Structured existing-app section creation envelope returned by application section MCP tools.
/// </summary>
public sealed record ApplicationSectionContextResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("package-u-id")] string? PackageUId = null,
	[property: JsonPropertyName("package-name")] string? PackageName = null,
	[property: JsonPropertyName("application-id")] string? ApplicationId = null,
	[property: JsonPropertyName("application-name")] string? ApplicationName = null,
	[property: JsonPropertyName("application-code")] string? ApplicationCode = null,
	[property: JsonPropertyName("application-version")] string? ApplicationVersion = null,
	[property: JsonPropertyName("section")] ApplicationSectionResult? Section = null,
	[property: JsonPropertyName("entity")] ApplicationEntityResult? Entity = null,
	[property: JsonPropertyName("pages")] IReadOnlyList<PageListItem>? Pages = null,
	[property: JsonPropertyName("error")] string? Error = null);

/// <summary>
/// Structured existing-app section update envelope returned by application section update MCP tools.
/// </summary>
public sealed record ApplicationSectionUpdateContextResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("package-u-id")] string? PackageUId = null,
	[property: JsonPropertyName("package-name")] string? PackageName = null,
	[property: JsonPropertyName("application-id")] string? ApplicationId = null,
	[property: JsonPropertyName("application-name")] string? ApplicationName = null,
	[property: JsonPropertyName("application-code")] string? ApplicationCode = null,
	[property: JsonPropertyName("application-version")] string? ApplicationVersion = null,
	[property: JsonPropertyName("previous-section")] ApplicationSectionResult? PreviousSection = null,
	[property: JsonPropertyName("section")] ApplicationSectionResult? Section = null,
	[property: JsonPropertyName("error")] string? Error = null);

/// <summary>
/// Structured section item returned by the <c>create-app-section</c> MCP tool.
/// </summary>
public sealed record ApplicationSectionResult(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("description")] string? Description,
	[property: JsonPropertyName("entity-schema-name")] string? EntitySchemaName,
	[property: JsonPropertyName("package-id")] string? PackageId,
	[property: JsonPropertyName("section-schema-u-id")] string? SectionSchemaUId,
	[property: JsonPropertyName("icon-id")] string? IconId,
	[property: JsonPropertyName("icon-background")] string? IconBackground,
	[property: JsonPropertyName("client-type-id")] string? ClientTypeId);

/// <summary>
/// Structured Data Forge enrichment diagnostics returned by <c>create-app</c>.
/// </summary>
public sealed record ApplicationDataForgeResult(
	[property: JsonPropertyName("used")] bool Used,
	[property: JsonPropertyName("health")] DataForgeHealthResult? Health,
	[property: JsonPropertyName("status")] DataForgeMaintenanceStatusResult? Status,
	[property: JsonPropertyName("coverage")] DataForgeCoverage? Coverage,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
	[property: JsonPropertyName("context-summary")] ApplicationDataForgeContextSummary? ContextSummary);

/// <summary>
/// Compact Data Forge context projection for app-creation responses.
/// </summary>
public sealed record ApplicationDataForgeContextSummary(
	[property: JsonPropertyName("similar-tables")] IReadOnlyList<SimilarTableResult> SimilarTables,
	[property: JsonPropertyName("similar-lookups")] IReadOnlyList<SimilarLookupResult> SimilarLookups,
	[property: JsonPropertyName("relation-pairs")] IReadOnlyList<string> RelationPairs,
	[property: JsonPropertyName("column-hints")] IReadOnlyList<ApplicationDataForgeColumnHint> ColumnHints);

/// <summary>
/// Per-table column summary returned inside the compact Data Forge context projection.
/// </summary>
public sealed record ApplicationDataForgeColumnHint(
	[property: JsonPropertyName("table-name")] string TableName,
	[property: JsonPropertyName("column-count")] int ColumnCount,
	[property: JsonPropertyName("required-column-count")] int RequiredColumnCount,
	[property: JsonPropertyName("lookup-column-count")] int LookupColumnCount);

/// <summary>
/// Structured application entity item returned by the application MCP tool family.
/// </summary>
public sealed record ApplicationEntityResult(
	[property: JsonPropertyName("u-id")] string UId,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("columns")] IReadOnlyList<ApplicationColumnResult> Columns);

/// <summary>
/// Structured application column item returned by the application MCP tool family.
/// </summary>
public sealed record ApplicationColumnResult(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("data-value-type")] string DataValueType,
	[property: JsonPropertyName("reference-schema")] string? ReferenceSchema = null);

/// <summary>
/// Structured response from the <c>delete-app-section</c> MCP tool.
/// </summary>
public sealed record ApplicationSectionDeleteContextResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("package-u-id")] string? PackageUId = null,
	[property: JsonPropertyName("package-name")] string? PackageName = null,
	[property: JsonPropertyName("application-id")] string? ApplicationId = null,
	[property: JsonPropertyName("application-name")] string? ApplicationName = null,
	[property: JsonPropertyName("application-code")] string? ApplicationCode = null,
	[property: JsonPropertyName("application-version")] string? ApplicationVersion = null,
	[property: JsonPropertyName("deleted-section")] ApplicationSectionResult? DeletedSection = null,
	[property: JsonPropertyName("error")] string? Error = null);

/// <summary>
/// MCP response returned by the <c>application-section-get-list</c> tool.
/// </summary>
public sealed record ApplicationSectionListContextResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("package-u-id")] string? PackageUId = null,
	[property: JsonPropertyName("package-name")] string? PackageName = null,
	[property: JsonPropertyName("application-id")] string? ApplicationId = null,
	[property: JsonPropertyName("application-name")] string? ApplicationName = null,
	[property: JsonPropertyName("application-code")] string? ApplicationCode = null,
	[property: JsonPropertyName("application-version")] string? ApplicationVersion = null,
	[property: JsonPropertyName("sections")] IReadOnlyList<ApplicationSectionResult>? Sections = null,
	[property: JsonPropertyName("error")] string? Error = null);
