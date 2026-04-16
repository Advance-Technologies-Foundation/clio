using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Clio.Common.EntitySchema;

/// <summary>
/// Reads rich runtime entity schema payloads by schema name through the RuntimeEntitySchemaRequest endpoint.
/// </summary>
public interface IRuntimeEntitySchemaReader {
	/// <summary>
	/// Loads the runtime schema for the requested entity schema name.
	/// </summary>
	/// <param name="schemaName">Creatio entity schema name.</param>
	/// <returns>A rich runtime schema result with schema-level and column-level metadata.</returns>
	RuntimeEntitySchemaResult GetByName(string schemaName);
}

/// <summary>
/// Rich runtime entity schema metadata returned by the shared by-name runtime reader.
/// </summary>
/// <param name="UId">Runtime schema identifier.</param>
/// <param name="Name">Runtime schema name.</param>
/// <param name="PrimaryColumnUId">Primary column identifier.</param>
/// <param name="PrimaryDisplayColumnName">Resolved primary display column name.</param>
/// <param name="PrimaryDisplayColumnUId">Primary display column identifier when exposed by the runtime schema.</param>
/// <param name="Columns">Complete runtime column set without inherited filtering.</param>
public sealed record RuntimeEntitySchemaResult(
	Guid UId,
	string Name,
	Guid PrimaryColumnUId,
	string? PrimaryDisplayColumnName,
	Guid? PrimaryDisplayColumnUId,
	IReadOnlyList<RuntimeEntitySchemaColumnResult> Columns
);

/// <summary>
/// Rich runtime column metadata returned by the shared by-name runtime reader.
/// </summary>
/// <param name="UId">Runtime column identifier.</param>
/// <param name="Name">Runtime column name.</param>
/// <param name="Caption">Resolved localized caption.</param>
/// <param name="Description">Resolved localized description.</param>
/// <param name="DataValueType">Creatio runtime data-value-type identifier.</param>
/// <param name="IsRequired">Whether the runtime column is required.</param>
/// <param name="IsInherited">Whether the runtime column is inherited from a parent schema.</param>
/// <param name="ReferenceSchemaName">Optional lookup reference schema name.</param>
public sealed record RuntimeEntitySchemaColumnResult(
	Guid UId,
	string Name,
	string? Caption,
	string? Description,
	int DataValueType,
	bool IsRequired,
	bool IsInherited,
	string? ReferenceSchemaName
);

internal sealed class RuntimeEntitySchemaReader(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder)
	: IRuntimeEntitySchemaReader {

	// This reader intentionally supports only Name-based RuntimeEntitySchemaRequest reads.
	// It does not replace the by-UId designer path used by RemoteEntitySchemaDesignerClient.
	public RuntimeEntitySchemaResult GetByName(string schemaName) {
		if (string.IsNullOrWhiteSpace(schemaName)) {
			throw new InvalidOperationException("Schema name is required.");
		}

		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest);
		string requestBody = JsonSerializer.Serialize(
			new RuntimeEntitySchemaRequestDto(schemaName),
			RuntimeEntitySchemaJson.RequestOptions);
		string responseJson = applicationClient.ExecutePostRequest(url, requestBody);
		RuntimeEntitySchemaResponseDto? response = JsonSerializer.Deserialize<RuntimeEntitySchemaResponseDto>(
			responseJson,
			RuntimeEntitySchemaJson.Options);
		if (response?.Success != true || response.Schema is null) {
			throw new InvalidOperationException(
				response?.ErrorInfo?.Message ?? $"Runtime schema '{schemaName}' was not returned by Creatio.");
		}

		RuntimeEntitySchemaPayloadDto schema = response.Schema;
		List<RuntimeEntitySchemaColumnResult> columns = schema.Columns?.Items?.Values
			.Select(column => new RuntimeEntitySchemaColumnResult(
				column.UId,
				column.Name,
				GetLocalizedValue(column.Caption),
				GetLocalizedValue(column.Description),
				column.DataValueType,
				column.IsRequired,
				column.IsInherited,
				column.ReferenceSchemaName))
			.OrderBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
			.ToList() ?? [];

		return new RuntimeEntitySchemaResult(
			schema.UId,
			string.IsNullOrWhiteSpace(schema.Name) ? schemaName.Trim() : schema.Name,
			schema.PrimaryColumnUId,
			ResolvePrimaryDisplayColumnName(schema, columns),
			schema.PrimaryDisplayColumnUId,
			columns);
	}

	private static string? ResolvePrimaryDisplayColumnName(
		RuntimeEntitySchemaPayloadDto schema,
		IReadOnlyList<RuntimeEntitySchemaColumnResult> columns) {
		if (!string.IsNullOrWhiteSpace(schema.PrimaryDisplayColumnName)) {
			return schema.PrimaryDisplayColumnName;
		}

		if (schema.PrimaryDisplayColumnUId is not Guid primaryDisplayColumnUId) {
			return null;
		}

		return columns.FirstOrDefault(column => column.UId == primaryDisplayColumnUId)?.Name;
	}

	private static string? GetLocalizedValue(JsonElement localizedValue) {
		if (localizedValue.ValueKind == JsonValueKind.String) {
			return localizedValue.GetString();
		}

		if (localizedValue.ValueKind != JsonValueKind.Object) {
			return null;
		}

		if (TryGetPropertyIgnoreCase(localizedValue, "en-US", out JsonElement englishValue)
			&& englishValue.ValueKind == JsonValueKind.String) {
			return englishValue.GetString();
		}

		foreach (JsonProperty property in localizedValue.EnumerateObject()) {
			if (property.Value.ValueKind == JsonValueKind.String) {
				return property.Value.GetString();
			}
		}

		return null;
	}

	private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement property) {
		if (element.ValueKind != JsonValueKind.Object) {
			property = default;
			return false;
		}

		foreach (JsonProperty candidate in element.EnumerateObject()) {
			if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
				property = candidate.Value;
				return true;
			}
		}

		property = default;
		return false;
	}

	private static class RuntimeEntitySchemaJson {
		internal static readonly JsonSerializerOptions Options = new() {
			PropertyNameCaseInsensitive = true
		};

		internal static readonly JsonSerializerOptions RequestOptions = new() {
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		};
	}

	private sealed record RuntimeEntitySchemaResponseDto(
		bool Success,
		RuntimeEntitySchemaPayloadDto? Schema,
		RuntimeEntitySchemaErrorInfoDto? ErrorInfo);

	private sealed record RuntimeEntitySchemaRequestDto(string Name);

	private sealed record RuntimeEntitySchemaErrorInfoDto(string? Message);

	private sealed record RuntimeEntitySchemaPayloadDto(
		RuntimeEntitySchemaColumnsDto? Columns,
		Guid PrimaryColumnUId,
		Guid UId,
		string Name,
		string? PrimaryDisplayColumnName,
		Guid? PrimaryDisplayColumnUId);

	private sealed record RuntimeEntitySchemaColumnsDto(
		Dictionary<string, RuntimeEntitySchemaColumnDto> Items);

	private sealed record RuntimeEntitySchemaColumnDto(
		Guid UId,
		string Name,
		JsonElement Caption,
		JsonElement Description,
		int DataValueType,
		bool IsRequired,
		bool IsInherited,
		string? ReferenceSchemaName);
}
