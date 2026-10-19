using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Clio.Common;

namespace Clio.Command.EntityConnections;

/// <summary>
/// The effective connection columns of an object and where they were read from.
/// </summary>
/// <param name="ColumnUIds">Connection column UIds in server order, without duplicates.</param>
/// <param name="IsTableFallback">
/// <c>true</c> when <c>EntityConnectionsService.GetConnections</c> failed and the list holds only the
/// <c>EntityConnection</c> rows, so the items and removals of the <c>EntityConnections</c> add-on are not applied to it.
/// </param>
/// <param name="Warning">Why the fallback was taken; <c>null</c> when the service answered.</param>
internal sealed record EntityConnectionsSnapshot(
	IReadOnlyList<Guid> ColumnUIds,
	bool IsTableFallback,
	string Warning);

/// <summary>
/// Reads the effective "Connections" of an object the way the Object designer does: the CrtUIv2
/// <c>EntityConnectionsService.GetConnections</c> endpoint first, the <c>EntityConnection</c> rows ordered by
/// <c>Position</c> when it is missing or fails.
/// </summary>
internal interface IEntityConnectionsReader {

	/// <summary>Reads the effective connection columns of the object.</summary>
	/// <param name="entitySchemaUId">The object's persisted schema UId (the add-on target, not a designer copy).</param>
	/// <returns>The connection columns and whether they came from the table fallback.</returns>
	/// <exception cref="InvalidOperationException">Both the service and the table fallback failed.</exception>
	EntityConnectionsSnapshot Read(Guid entitySchemaUId);
}

/// <inheritdoc />
internal sealed class EntityConnectionsReader(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	ILogger logger)
	: IEntityConnectionsReader {

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	/// <inheritdoc />
	public EntityConnectionsSnapshot Read(Guid entitySchemaUId) {
		string serviceError = TryReadFromService(entitySchemaUId, out IReadOnlyList<Guid> columnUIds);
		if (serviceError is null) {
			return new EntityConnectionsSnapshot(columnUIds, false, null);
		}
		(IReadOnlyList<Guid> rows, string tableError) = PageSchemaMetadataHelper.QueryEntityConnectionColumnUIds(
			applicationClient, serviceUrlBuilder, entitySchemaUId);
		if (tableError is not null) {
			throw new InvalidOperationException(
				$"Could not read the connections of the object. EntityConnectionsService: {serviceError} "
				+ $"EntityConnection table: {tableError}");
		}
		string warning =
			"EntityConnectionsService.GetConnections is unavailable (" + serviceError + "); the list was read from the "
			+ "EntityConnection table, so the EntityConnections add-on items and removals are NOT reflected in it.";
		logger.WriteWarning(warning);
		return new EntityConnectionsSnapshot(rows, true, warning);
	}

	// Returns null on success, or why the service answer cannot be used.
	private string TryReadFromService(Guid entitySchemaUId, out IReadOnlyList<Guid> columnUIds) {
		columnUIds = [];
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetEntityConnections);
		string body = JsonSerializer.Serialize(new { entitySchemaUId = entitySchemaUId.ToString("D") });
		string responseBody;
		try {
			responseBody = applicationClient.ExecutePostRequest(url, body);
		} catch (Exception exception) when (exception is HttpRequestException or WebException or IOException
			or TaskCanceledException or TimeoutException) {
			return SensitiveErrorTextRedactor.Redact(exception.Message);
		}
		if (string.IsNullOrWhiteSpace(responseBody)) {
			return "the service returned an empty response";
		}
		GetConnectionsResponseDto response;
		try {
			response = JsonSerializer.Deserialize<GetConnectionsResponseDto>(responseBody, JsonOptions);
		} catch (JsonException) {
			// Creatio.Client returns an error page (404 when CrtUIv2 has no such endpoint) as the body, not a status.
			return "the service did not return JSON (the endpoint may be missing on this environment)";
		}
		if (response is null || !response.Success || response.Connections is null) {
			return response?.ErrorInfo?.Message ?? "the service returned no connections";
		}
		List<Guid> result = [];
		foreach (ConnectionDto connection in response.Connections) {
			if (!Guid.TryParse(connection?.ColumnUId, out Guid columnUId) || columnUId == Guid.Empty) {
				return "the service returned a connection with an invalid column UId";
			}
			if (!result.Contains(columnUId)) {
				result.Add(columnUId);
			}
		}
		columnUIds = result;
		return null;
	}

	private sealed class GetConnectionsResponseDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfo ErrorInfo { get; set; }

		[JsonPropertyName("connections")]
		public List<ConnectionDto> Connections { get; set; }
	}

	private sealed class ErrorInfo {
		[JsonPropertyName("message")]
		public string Message { get; set; }
	}

	private sealed class ConnectionDto {
		[JsonPropertyName("columnUId")]
		public string ColumnUId { get; set; }
	}
}
