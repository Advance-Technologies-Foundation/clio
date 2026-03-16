using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal static class EntitySchemaStructuredResultParser {
	public static T Extract<T>(CallToolResult callResult) {
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent) &&
			TryExtractEnvelope(structuredContent, out T? structuredEnvelope)) {
			return structuredEnvelope!;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content) &&
			TryExtractEnvelope(content, out T? contentEnvelope)) {
			return contentEnvelope!;
		}

		throw new InvalidOperationException($"Could not parse {typeof(T).Name} MCP result.");
	}

	private static bool TrySerializeToJsonElement(object? value, out JsonElement element) {
		if (value is null) {
			element = default;
			return false;
		}

		element = JsonSerializer.SerializeToElement(value);
		return true;
	}

	private static bool TryExtractEnvelope<T>(JsonElement element, out T? envelope) {
		if (TryDeserializeEnvelope(element, out envelope)) {
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryGetTextPayload(item, out string? textPayload) &&
					!string.IsNullOrWhiteSpace(textPayload) &&
					TryParseJson(textPayload, out JsonElement parsedPayload) &&
					TryDeserializeEnvelope(parsedPayload, out envelope)) {
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.String) {
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload) &&
				TryParseJson(textPayload, out JsonElement parsedPayload) &&
				TryDeserializeEnvelope(parsedPayload, out envelope)) {
				return true;
			}
		}

		envelope = default;
		return false;
	}

	private static bool TryDeserializeEnvelope<T>(JsonElement element, out T? envelope) {
		try {
			envelope = JsonSerializer.Deserialize<T>(
				element.GetRawText(),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return envelope is not null;
		}
		catch (JsonException) {
			envelope = default;
			return false;
		}
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
