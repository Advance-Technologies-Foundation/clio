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
	[property: JsonPropertyName("entities")] IReadOnlyList<ApplicationEntityEnvelope>? Entities,
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

internal static class ApplicationResultParser {
	public static ApplicationListResponseEnvelope ExtractList(CallToolResult callResult) {
		if (TryExtract(callResult, IsValidListEnvelope, out ApplicationListResponseEnvelope? envelope)) {
			return envelope!;
		}

		throw new InvalidOperationException("Could not parse application-get-list MCP result.");
	}

	public static ApplicationContextResponseEnvelope ExtractInfo(CallToolResult callResult) {
		if (TryExtract(callResult, IsValidContextEnvelope, out ApplicationContextResponseEnvelope? envelope)) {
			return envelope!;
		}

		throw new InvalidOperationException("Could not parse application-get-info MCP result.");
	}

	public static ApplicationDeleteResponseEnvelope ExtractDelete(CallToolResult callResult) {
		if (TryExtract(callResult, IsValidDeleteEnvelope, out ApplicationDeleteResponseEnvelope? envelope)) {
			return envelope!;
		}

		throw new InvalidOperationException("Could not parse application-delete MCP result.");
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
