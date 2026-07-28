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
	[property: JsonPropertyName("schema-name-prefix")] string? SchemaNamePrefix = null,
	[property: JsonPropertyName("dataforge")] ApplicationDataForgeResult? DataForge = null,
	[property: JsonPropertyName("error")] string? Error = null);

/// <summary>
/// Structured existing-app section creation envelope returned by application section MCP tools.
/// On classified failures (ENG-90679) the envelope additionally carries
/// <c>error-class</c> (<c>transport</c> | <c>creatio-timeout</c> | <c>server-error</c>),
/// <c>section-created</c> (<c>true</c> | <c>false</c> | <c>unknown</c> | <c>in-progress</c>), and
/// <c>retry-guidance</c> so the calling agent can make a rational retry-vs-abandon decision. The
/// <c>in-progress</c> value (ENG-91316) means the response deadline elapsed while the section was
/// still being created server-side: the agent must poll, not retry. See
/// <c>spec/adr/adr-create-app-section-response-deadline.md</c>.
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
	[property: JsonPropertyName("error")] string? Error = null,
	[property: JsonPropertyName("error-class")] string? ErrorClass = null,
	[property: JsonPropertyName("section-created")] string? SectionCreated = null,
	[property: JsonPropertyName("retry-guidance")] string? RetryGuidance = null);

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
	[property: JsonPropertyName("columns")] IReadOnlyList<ApplicationColumnResult> Columns,
	[property: JsonPropertyName("virtual")] bool Virtual = false);

/// <summary>
/// Structured application column item returned by the application MCP tool family.
/// </summary>
/// <remarks>
/// The column vocabulary is unified with the <c>sync-schemas</c> write surfaces so the natural
/// read-modify-write workflow round-trips without manual field translation (ENG-90313):
/// <list type="bullet">
/// <item><c>name</c> matches the write-side column identity.</item>
/// <item><c>type</c> is the canonical type field (mirrors <c>data-value-type</c>, which is retained
/// as a legacy alias and may be removed in a future major version).</item>
/// <item><c>reference-schema-name</c> is the canonical lookup-reference field (mirrors
/// <c>reference-schema</c>, retained as a legacy alias).</item>
/// <item><c>required</c> exposes the column required flag accepted by the write surfaces.</item>
/// </list>
/// </remarks>
public sealed record ApplicationColumnResult(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("data-value-type")] string DataValueType,
	[property: JsonPropertyName("reference-schema")] string? ReferenceSchema = null,
	[property: JsonPropertyName("required")] bool Required = false) {

	/// <summary>
	/// Gets the canonical column type. Mirrors <see cref="DataValueType"/> so the read shape can be sent
	/// verbatim to the <c>sync-schemas</c> write surfaces, which expect <c>type</c>.
	/// </summary>
	[JsonPropertyName("type")]
	public string Type => DataValueType;

	/// <summary>
	/// Gets the canonical lookup reference schema name. Mirrors <see cref="ReferenceSchema"/> so the read
	/// shape can be sent verbatim to the write surfaces, which expect <c>reference-schema-name</c>.
	/// </summary>
	[JsonPropertyName("reference-schema-name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ReferenceSchemaName => ReferenceSchema;
}

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
