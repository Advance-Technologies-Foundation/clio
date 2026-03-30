using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Structured application list envelope returned by the application MCP tool family.
/// </summary>
public sealed record ApplicationListResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("applications")] IReadOnlyList<ApplicationListItemResult>? Applications = null,
	[property: JsonPropertyName("error")] string? Error = null);

/// <summary>
/// Structured installed-application item returned by the <c>application-get-list</c> MCP tool.
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
	[property: JsonPropertyName("error")] string? Error = null);

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
