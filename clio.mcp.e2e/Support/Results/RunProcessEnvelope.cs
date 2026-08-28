using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record RunProcessEnvelope(
	[property: JsonPropertyName("status")] string? Status,
	[property: JsonPropertyName("processId")] string? ProcessId,
	[property: JsonPropertyName("resultParameterValues")] Dictionary<string, object>? ResultParameterValues,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string>? Warnings,
	[property: JsonPropertyName("error")] string? Error);

internal static class RunProcessResultParser {

	/// <summary>
	/// Extracts the <c>run-process</c> envelope from an MCP result. The tool lives on the lazy surface, so a
	/// call arrives through <c>clio-run</c>, whose payload nests the target tool's own JSON inside the text
	/// content.
	/// </summary>
	public static RunProcessEnvelope Extract(CallToolResult callResult) {
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent)
			&& TryExtractEnvelope(structuredContent, out RunProcessEnvelope? structuredEnvelope)) {
			return structuredEnvelope!;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content)
			&& TryExtractEnvelope(content, out RunProcessEnvelope? contentEnvelope)) {
			return contentEnvelope!;
		}

		throw new InvalidOperationException("Could not parse run-process MCP result.");
	}

	private static bool TrySerializeToJsonElement(object? value, out JsonElement element) {
		if (value is null) {
			element = default;
			return false;
		}

		element = JsonSerializer.SerializeToElement(value);
		return true;
	}

	private static bool TryExtractEnvelope(JsonElement element, out RunProcessEnvelope? envelope) {
		if (TryDeserialize(element, out envelope)) {
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryGetTextPayload(item, out string? textPayload)
					&& !string.IsNullOrWhiteSpace(textPayload)
					&& TryParseJson(textPayload!, out JsonElement textPayloadElement)
					&& TryDeserialize(textPayloadElement, out envelope)) {
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.String) {
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload)
				&& TryParseJson(textPayload!, out JsonElement textPayloadElement)
				&& TryDeserialize(textPayloadElement, out envelope)) {
				return true;
			}
		}

		envelope = null;
		return false;
	}

	private static bool TryDeserialize(JsonElement element, out RunProcessEnvelope? envelope) {
		try {
			if (element.ValueKind != JsonValueKind.Object) {
				envelope = null;
				return false;
			}

			RunProcessEnvelope? item = JsonSerializer.Deserialize<RunProcessEnvelope>(
				element.GetRawText(),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			// A run-process envelope always carries either a status or a failure reason; anything carrying
			// neither is some other tool's payload (or clio-run's own wrapper).
			if (item is null || (string.IsNullOrWhiteSpace(item.Status) && string.IsNullOrWhiteSpace(item.Error))) {
				envelope = null;
				return false;
			}

			envelope = item;
			return true;
		}
		catch (JsonException) {
			envelope = null;
			return false;
		}
	}

	private static bool TryGetTextPayload(JsonElement element, out string? textPayload) {
		textPayload = null;
		if (element.ValueKind != JsonValueKind.Object) {
			return false;
		}

		if (element.TryGetProperty("text", out JsonElement textElement)
			&& textElement.ValueKind == JsonValueKind.String) {
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
