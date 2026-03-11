using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record GetPkgListEnvelope(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("version")] string Version,
	[property: JsonPropertyName("maintainer")] string Maintainer);

internal static class GetPkgListResultParser {
	public static IReadOnlyList<GetPkgListEnvelope> Extract(CallToolResult callResult) {
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent) &&
			TryExtractEnvelopeList(structuredContent, out IReadOnlyList<GetPkgListEnvelope>? structuredEnvelopes)) {
			return structuredEnvelopes!;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content) &&
			TryExtractEnvelopeList(content, out IReadOnlyList<GetPkgListEnvelope>? contentEnvelopes)) {
			return contentEnvelopes!;
		}

		throw new InvalidOperationException("Could not parse get-pkg-list MCP result.");
	}

	private static bool TrySerializeToJsonElement(object? value, out JsonElement element) {
		if (value is null) {
			element = default;
			return false;
		}

		element = JsonSerializer.SerializeToElement(value);
		return true;
	}

	private static bool TryExtractEnvelopeList(JsonElement element, out IReadOnlyList<GetPkgListEnvelope>? envelopes) {
		if (TryDeserializeList(element, out envelopes)) {
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryGetTextPayload(item, out string? textPayload) &&
					!string.IsNullOrWhiteSpace(textPayload) &&
					TryParseJson(textPayload, out JsonElement textPayloadElement) &&
					TryDeserializeList(textPayloadElement, out envelopes)) {
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.String) {
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload) &&
				TryParseJson(textPayload, out JsonElement textPayloadElement) &&
				TryDeserializeList(textPayloadElement, out envelopes)) {
				return true;
			}
		}

		envelopes = null;
		return false;
	}

	private static bool TryDeserializeList(JsonElement element, out IReadOnlyList<GetPkgListEnvelope>? envelopes) {
		try {
			GetPkgListEnvelope[]? items = JsonSerializer.Deserialize<GetPkgListEnvelope[]>(
				element.GetRawText(),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (items is null || items.Length == 0 || !ContainsUsableEnvelope(items)) {
				envelopes = null;
				return false;
			}

			envelopes = items;
			return true;
		}
		catch (JsonException) {
			envelopes = null;
			return false;
		}
	}

	private static bool ContainsUsableEnvelope(IEnumerable<GetPkgListEnvelope> items) {
		return items.Any(item =>
			!string.IsNullOrWhiteSpace(item.Name) ||
			!string.IsNullOrWhiteSpace(item.Version) ||
			item.Maintainer is not null);
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
