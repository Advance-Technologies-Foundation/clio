using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record ApplicationListItemEnvelope(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("version")] string Version);

internal sealed record ApplicationListResponseEnvelope(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("applications")] IReadOnlyList<ApplicationListItemEnvelope>? Applications,
	[property: JsonPropertyName("error")] string? Error);

internal sealed record ApplicationContextResponseEnvelope(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("package-u-id")] string? PackageUId,
	[property: JsonPropertyName("package-name")] string? PackageName,
	[property: JsonPropertyName("canonical-main-entity-name")] string? CanonicalMainEntityName,
	[property: JsonPropertyName("application-id")] string? ApplicationId,
	[property: JsonPropertyName("application-name")] string? ApplicationName,
	[property: JsonPropertyName("application-code")] string? ApplicationCode,
	[property: JsonPropertyName("application-version")] string? ApplicationVersion,
	[property: JsonPropertyName("entities")] IReadOnlyList<ApplicationEntityEnvelope>? Entities,
	[property: JsonPropertyName("pages")] IReadOnlyList<ApplicationPageEnvelope>? Pages,
	[property: JsonPropertyName("dataforge")] ApplicationDataForgeEnvelope? DataForge,
	[property: JsonPropertyName("error")] string? Error);

internal sealed record ApplicationSectionContextResponseEnvelope(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("package-u-id")] string? PackageUId,
	[property: JsonPropertyName("package-name")] string? PackageName,
	[property: JsonPropertyName("application-id")] string? ApplicationId,
	[property: JsonPropertyName("application-name")] string? ApplicationName,
	[property: JsonPropertyName("application-code")] string? ApplicationCode,
	[property: JsonPropertyName("application-version")] string? ApplicationVersion,
	[property: JsonPropertyName("section")] ApplicationSectionEnvelope? Section,
	[property: JsonPropertyName("entity")] ApplicationEntityEnvelope? Entity,
	[property: JsonPropertyName("pages")] IReadOnlyList<ApplicationPageEnvelope>? Pages,
	[property: JsonPropertyName("error")] string? Error);

internal sealed record ApplicationSectionUpdateContextResponseEnvelope(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("package-u-id")] string? PackageUId,
	[property: JsonPropertyName("package-name")] string? PackageName,
	[property: JsonPropertyName("application-id")] string? ApplicationId,
	[property: JsonPropertyName("application-name")] string? ApplicationName,
	[property: JsonPropertyName("application-code")] string? ApplicationCode,
	[property: JsonPropertyName("application-version")] string? ApplicationVersion,
	[property: JsonPropertyName("previous-section")] ApplicationSectionEnvelope? PreviousSection,
	[property: JsonPropertyName("section")] ApplicationSectionEnvelope? Section,
	[property: JsonPropertyName("error")] string? Error);

internal sealed record ApplicationDeleteResponseEnvelope(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("error")] string? Error);

internal sealed record ApplicationEntityEnvelope(
	[property: JsonPropertyName("u-id")] string UId,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("columns")] IReadOnlyList<ApplicationColumnEnvelope> Columns);

internal sealed record ApplicationColumnEnvelope(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("data-value-type")] string DataValueType,
	[property: JsonPropertyName("reference-schema")] string? ReferenceSchema);

internal sealed record ApplicationPageEnvelope(
	[property: JsonPropertyName("schema-name")] string SchemaName,
	[property: JsonPropertyName("uId")] string UId,
	[property: JsonPropertyName("packageName")] string PackageName,
	[property: JsonPropertyName("parentSchemaName")] string ParentSchemaName);

internal sealed record ApplicationSectionEnvelope(
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

internal sealed record ApplicationDataForgeEnvelope(
	[property: JsonPropertyName("used")] bool Used,
	[property: JsonPropertyName("health")] DataForgeHealthEnvelope? Health,
	[property: JsonPropertyName("status")] DataForgeStatusEnvelope? Status,
	[property: JsonPropertyName("coverage")] DataForgeCoverageEnvelope? Coverage,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string>? Warnings,
	[property: JsonPropertyName("context-summary")] ApplicationDataForgeContextSummaryEnvelope? ContextSummary);

internal sealed record DataForgeHealthEnvelope(
	[property: JsonPropertyName("liveness")] bool Liveness,
	[property: JsonPropertyName("readiness")] bool Readiness,
	[property: JsonPropertyName("data-structure-readiness")] bool DataStructureReadiness,
	[property: JsonPropertyName("lookups-readiness")] bool LookupsReadiness,
	[property: JsonPropertyName("correlation-id")] string CorrelationId);

internal sealed record DataForgeStatusEnvelope(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("error")] string? Error);

internal sealed record DataForgeCoverageEnvelope(
	[property: JsonPropertyName("health")] bool Health,
	[property: JsonPropertyName("tables")] bool Tables,
	[property: JsonPropertyName("lookups")] bool Lookups,
	[property: JsonPropertyName("relations")] bool Relations,
	[property: JsonPropertyName("table-columns")] bool Columns);

internal sealed record ApplicationDataForgeContextSummaryEnvelope(
	[property: JsonPropertyName("similar-tables")] IReadOnlyList<ApplicationDataForgeTableEnvelope>? SimilarTables,
	[property: JsonPropertyName("similar-lookups")] IReadOnlyList<ApplicationDataForgeLookupEnvelope>? SimilarLookups,
	[property: JsonPropertyName("relation-pairs")] IReadOnlyList<string>? RelationPairs,
	[property: JsonPropertyName("column-hints")] IReadOnlyList<ApplicationDataForgeColumnHintEnvelope>? ColumnHints);

internal sealed record ApplicationDataForgeTableEnvelope(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")] string? Caption,
	[property: JsonPropertyName("description")] string? Description);

internal sealed record ApplicationDataForgeLookupEnvelope(
	[property: JsonPropertyName("lookup-id")] string LookupId,
	[property: JsonPropertyName("schema-name")] string SchemaName,
	[property: JsonPropertyName("value")] string Value,
	[property: JsonPropertyName("score")] decimal? Score);

internal sealed record ApplicationDataForgeColumnHintEnvelope(
	[property: JsonPropertyName("table-name")] string TableName,
	[property: JsonPropertyName("column-count")] int ColumnCount,
	[property: JsonPropertyName("required-column-count")] int RequiredColumnCount,
	[property: JsonPropertyName("lookup-column-count")] int LookupColumnCount);

internal static class ApplicationResultParser {
	public static ApplicationListResponseEnvelope ExtractList(CallToolResult callResult) {
		if (TryExtract(callResult, IsValidListEnvelope, out ApplicationListResponseEnvelope? envelope)) {
			return envelope!;
		}

		throw new InvalidOperationException("Could not parse list-apps MCP result.");
	}

	public static ApplicationContextResponseEnvelope ExtractInfo(CallToolResult callResult) {
		if (TryExtract(callResult, IsValidContextEnvelope, out ApplicationContextResponseEnvelope? envelope)) {
			return envelope!;
		}

		throw new InvalidOperationException("Could not parse get-app-info MCP result.");
	}

	public static ApplicationDeleteResponseEnvelope ExtractDelete(CallToolResult callResult) {
		if (TryExtract(callResult, IsValidDeleteEnvelope, out ApplicationDeleteResponseEnvelope? envelope)) {
			return envelope!;
		}

		throw new InvalidOperationException("Could not parse delete-app MCP result.");
	}

	public static ApplicationSectionContextResponseEnvelope ExtractSectionCreate(CallToolResult callResult) {
		if (TryExtract(callResult, IsValidSectionContextEnvelope, out ApplicationSectionContextResponseEnvelope? envelope)) {
			return envelope!;
		}

		throw new InvalidOperationException("Could not parse create-app-section MCP result.");
	}

	public static ApplicationSectionUpdateContextResponseEnvelope ExtractSectionUpdate(CallToolResult callResult) {
		if (TryExtract(callResult, IsValidSectionUpdateContextEnvelope, out ApplicationSectionUpdateContextResponseEnvelope? envelope)) {
			return envelope!;
		}

		throw new InvalidOperationException("Could not parse update-app-section MCP result.");
	}

	private static bool TryExtract<T>(CallToolResult callResult, Func<T?, bool> validator, out T? result) {
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent) &&
			TryDeserialize(structuredContent, validator, out result)) {
			return true;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content) &&
			TryDeserialize(content, validator, out result)) {
			return true;
		}

		result = default;
		return false;
	}

	private static bool TrySerializeToJsonElement(object? value, out JsonElement element) {
		if (value is null) {
			element = default;
			return false;
		}

		element = JsonSerializer.SerializeToElement(value);
		return true;
	}

	private static bool TryDeserialize<T>(JsonElement element, Func<T?, bool> validator, out T? result) {
		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryGetTextPayload(item, out string? textPayload) &&
					!string.IsNullOrWhiteSpace(textPayload) &&
					TryParseJson(textPayload, out JsonElement textPayloadElement) &&
					TryDeserializeRaw(textPayloadElement, validator, out result)) {
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.String) {
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload) &&
				TryParseJson(textPayload, out JsonElement textPayloadElement) &&
				TryDeserializeRaw(textPayloadElement, validator, out result)) {
				return true;
			}
		}

		if (TryDeserializeRaw(element, validator, out result)) {
			return true;
		}

		result = default;
		return false;
	}

	private static bool TryDeserializeRaw<T>(JsonElement element, Func<T?, bool> validator, out T? result) {
		try {
			result = JsonSerializer.Deserialize<T>(
				element.GetRawText(),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return validator(result);
		}
		catch (JsonException) {
			result = default;
			return false;
		}
	}

	private static bool IsValidListEnvelope(ApplicationListResponseEnvelope? envelope) {
		return envelope is not null &&
			(envelope.Success || !string.IsNullOrWhiteSpace(envelope.Error));
	}

	private static bool IsValidContextEnvelope(ApplicationContextResponseEnvelope? envelope) {
		return envelope is not null &&
			(envelope.Success || !string.IsNullOrWhiteSpace(envelope.Error));
	}

	private static bool IsValidDeleteEnvelope(ApplicationDeleteResponseEnvelope? envelope) {
		return envelope is not null &&
			(envelope.Success || !string.IsNullOrWhiteSpace(envelope.Error));
	}

	private static bool IsValidSectionContextEnvelope(ApplicationSectionContextResponseEnvelope? envelope) {
		return envelope is not null &&
			(envelope.Success || !string.IsNullOrWhiteSpace(envelope.Error));
	}

	private static bool IsValidSectionUpdateContextEnvelope(ApplicationSectionUpdateContextResponseEnvelope? envelope) {
		return envelope is not null &&
			(envelope.Success || !string.IsNullOrWhiteSpace(envelope.Error));
	}

	private static bool TryGetTextPayload(JsonElement element, out string? textPayload) {
		textPayload = null;
		if (element.ValueKind != JsonValueKind.Object) {
			return false;
		}

		if (element.TryGetProperty("text", out JsonElement textElement) &&
			textElement.ValueKind == JsonValueKind.String) {
			textPayload = textElement.GetString();
			return true;
		}

		return false;
	}

	private static bool TryParseJson(string value, out JsonElement element) {
		try {
			element = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(value));
			return true;
		}
		catch (JsonException) {
			element = default;
			return false;
		}
	}
}
