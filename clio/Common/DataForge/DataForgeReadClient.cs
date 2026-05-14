using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Common.DataForge;

/// <summary>
/// Reads Data Forge data through Creatio's <c>DataForgeSchemaReadService</c> REST endpoints
/// instead of calling the DataForge microservice directly.
/// </summary>
public interface IDataForgeReadClient {
	/// <summary>
	/// Finds tables similar to the provided query string.
	/// </summary>
	IReadOnlyList<SimilarTableResult> FindSimilarTables(string query, int? limit = null);

	/// <summary>
	/// Finds lookups similar to the provided query string.
	/// </summary>
	IReadOnlyList<SimilarLookupResult> FindSimilarLookups(string query, string? schemaName = null, int? limit = null);

	/// <summary>
	/// Retrieves relationship paths between two tables.
	/// </summary>
	IReadOnlyList<string> GetTableRelationships(string sourceTable, string targetTable, int? limit = null);

}

/// <summary>
/// Calls Creatio's <c>/rest/DataForgeSchemaReadService</c> endpoints via <see cref="IApplicationClient"/>.
/// Authentication is handled by the existing Creatio session — no direct OAuth or microservice URL needed.
/// </summary>
public sealed class DataForgeReadClient(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IDataForgePlatformVersionGuard versionGuard) : IDataForgeReadClient {
	private const string ServiceBasePath = "rest/DataForgeSchemaReadService";
	internal const int RequestTimeoutMs = 60_000;

	private static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public IReadOnlyList<SimilarTableResult> FindSimilarTables(string query, int? limit = null) {
		var request = new { request = new { query, limit } };
		string response = ExecuteProxyPost("GetSimilarTableNames", request);
		CreatioTablesEnvelope? envelope = Deserialize<CreatioTablesEnvelope>(response);
		CreatioTablesPayload? payload = envelope?.GetSimilarTableNamesResult;
		if (payload is null || !payload.Success) {
			string error = payload?.ErrorInfo?.Message ?? "Empty response from DataForgeSchemaReadService/GetSimilarTableNames";
			throw new InvalidOperationException(error);
		}

		return payload.Data?
			.Select(t => new SimilarTableResult(t.Name ?? string.Empty, t.Caption ?? string.Empty, t.Description))
			.ToList() ?? [];
	}

	/// <inheritdoc />
	public IReadOnlyList<SimilarLookupResult> FindSimilarLookups(string query, string? schemaName = null, int? limit = null) {
		var request = new { request = new { query, schemaName, limit } };
		string response = ExecuteProxyPost("GetLookupValues", request);
		CreatioLookupsEnvelope? envelope = Deserialize<CreatioLookupsEnvelope>(response);
		CreatioLookupsPayload? payload = envelope?.GetLookupValuesResult;
		if (payload is null || !payload.Success) {
			string error = payload?.ErrorInfo?.Message ?? "Empty response from DataForgeSchemaReadService/GetLookupValues";
			throw new InvalidOperationException(error);
		}

		return payload.Data?
			.Select(l => new SimilarLookupResult(
				l.ValueId ?? l.Id?.ToString() ?? string.Empty,
				l.ReferenceSchemaName ?? l.Name ?? string.Empty,
				l.ValueName ?? string.Empty,
				(decimal?)l.VectorSimilarityScore))
			.ToList() ?? [];
	}

	/// <inheritdoc />
	public IReadOnlyList<string> GetTableRelationships(string sourceTable, string targetTable, int? limit = null) {
		var request = new { request = new { sourceTable, targetTable, limit } };
		string response = ExecuteProxyPost("GetTableRelationships", request);
		CreatioRelationsEnvelope? envelope = Deserialize<CreatioRelationsEnvelope>(response);
		CreatioRelationsPayload? payload = envelope?.GetTableRelationshipsResult;
		if (payload is null || !payload.Success) {
			string error = payload?.ErrorInfo?.Message ?? "Empty response from DataForgeSchemaReadService/GetTableRelationships";
			throw new InvalidOperationException(error);
		}

		return payload.Paths ?? [];
	}

	private string BuildMethodUrl(string methodName) => serviceUrlBuilder.Build($"{ServiceBasePath}/{methodName}");

	private string ExecuteProxyPost(string methodName, object request) {
		versionGuard.EnsureSupported();
		return applicationClient.ExecutePostRequest(
			BuildMethodUrl(methodName),
			JsonSerializer.Serialize(request, JsonOptions),
			RequestTimeoutMs,
			1,
			1);
	}

	private static T? Deserialize<T>(string response) where T : class {
		if (string.IsNullOrWhiteSpace(response)) {
			return null;
		}
		try {
			return JsonSerializer.Deserialize<T>(response, JsonOptions);
		} catch (JsonException) {
			return null;
		}
	}

	#region Creatio response envelope DTOs

	private sealed record CreatioErrorInfo(
		[property: JsonPropertyName("ErrorCode")] string? ErrorCode,
		[property: JsonPropertyName("Message")] string? Message);

	// --- Tables ---
	private sealed record CreatioSimilarTable(
		[property: JsonPropertyName("Name")] string? Name,
		[property: JsonPropertyName("Caption")] string? Caption,
		[property: JsonPropertyName("Description")] string? Description);

	private sealed record CreatioTablesPayload(
		[property: JsonPropertyName("Success")] bool Success,
		[property: JsonPropertyName("ErrorInfo")] CreatioErrorInfo? ErrorInfo,
		[property: JsonPropertyName("Data")] List<CreatioSimilarTable>? Data);

	private sealed record CreatioTablesEnvelope(
		[property: JsonPropertyName("GetSimilarTableNamesResult")] CreatioTablesPayload? GetSimilarTableNamesResult);

	// --- Lookups ---
	private sealed record CreatioLookupItem(
		[property: JsonPropertyName("id")] Guid? Id,
		[property: JsonPropertyName("name")] string? Name,
		[property: JsonPropertyName("referenceSchemaName")] string? ReferenceSchemaName,
		[property: JsonPropertyName("valueId")] string? ValueId,
		[property: JsonPropertyName("valueName")] string? ValueName,
		[property: JsonPropertyName("vectorSimilarityScore")] double? VectorSimilarityScore);

	private sealed record CreatioLookupsPayload(
		[property: JsonPropertyName("Success")] bool Success,
		[property: JsonPropertyName("ErrorInfo")] CreatioErrorInfo? ErrorInfo,
		[property: JsonPropertyName("Data")] List<CreatioLookupItem>? Data);

	private sealed record CreatioLookupsEnvelope(
		[property: JsonPropertyName("GetLookupValuesResult")] CreatioLookupsPayload? GetLookupValuesResult);

	// --- Relations ---
	private sealed record CreatioRelationsPayload(
		[property: JsonPropertyName("Success")] bool Success,
		[property: JsonPropertyName("ErrorInfo")] CreatioErrorInfo? ErrorInfo,
		[property: JsonPropertyName("Paths")] List<string>? Paths);

	private sealed record CreatioRelationsEnvelope(
		[property: JsonPropertyName("GetTableRelationshipsResult")] CreatioRelationsPayload? GetTableRelationshipsResult);

	#endregion
}
