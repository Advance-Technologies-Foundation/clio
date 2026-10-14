using System.Text.Json.Serialization;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record RunProcessEnvelope(
	[property: JsonPropertyName("status")] string? Status,
	[property: JsonPropertyName("processId")] string? ProcessId,
	[property: JsonPropertyName("resultParameterValues")] Dictionary<string, object>? ResultParameterValues,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string>? Warnings,
	[property: JsonPropertyName("error")] string? Error,
	[property: JsonPropertyName("versionName")] string? VersionName = null,
	[property: JsonPropertyName("isActiveVersion")] bool? IsActiveVersion = null,
	[property: JsonPropertyName("activeVersionName")] string? ActiveVersionName = null,
	[property: JsonPropertyName("waitingOn")] IReadOnlyList<RunProcessWaitingElement>? WaitingOn = null,
	[property: JsonPropertyName("created")] IReadOnlyList<RunProcessRecord>? Created = null,
	[property: JsonPropertyName("cleanup")] RunProcessCleanup? Cleanup = null);

internal sealed record RunProcessWaitingElement(
	[property: JsonPropertyName("caption")] string? Caption,
	[property: JsonPropertyName("type")] string? Type);

internal sealed record RunProcessRecord(
	[property: JsonPropertyName("entity")] string Entity,
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("title")] string? Title);

internal sealed record RunProcessCleanup(
	[property: JsonPropertyName("deleted")] IReadOnlyList<RunProcessRecord> Deleted,
	[property: JsonPropertyName("failed")] IReadOnlyList<RunProcessCleanupFailure> Failed);

internal sealed record RunProcessCleanupFailure(
	[property: JsonPropertyName("entity")] string Entity,
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("error")] string? Error);
