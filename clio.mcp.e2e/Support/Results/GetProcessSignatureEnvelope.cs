using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record ProcessSignatureParameterEnvelope(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")] string? Caption,
	[property: JsonPropertyName("clrType")] string? ClrType,
	[property: JsonPropertyName("dataValueTypeId")] string? DataValueTypeId,
	[property: JsonPropertyName("direction")] string? Direction,
	[property: JsonPropertyName("isLookup")] bool IsLookup,
	[property: JsonPropertyName("referenceSchemaUId")] string? ReferenceSchemaUId);

internal sealed record GetProcessSignatureEnvelope(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("processCode")] string? ProcessCode,
	[property: JsonPropertyName("processCaption")] string? ProcessCaption,
	[property: JsonPropertyName("processId")] string? ProcessId,
	[property: JsonPropertyName("parameters")] IReadOnlyList<ProcessSignatureParameterEnvelope>? Parameters,
	[property: JsonPropertyName("error")] string? Error);

internal static class GetProcessSignatureResultParser {
	public static GetProcessSignatureEnvelope Extract(CallToolResult callResult) {
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent) &&
			TryExtractEnvelope(structuredContent, out GetProcessSignatureEnvelope? structuredEnvelope)) {
			return structuredEnvelope!;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content) &&
			TryExtractEnvelope(content, out GetProcessSignatureEnvelope? contentEnvelope)) {
			return contentEnvelope!;
		}

		throw new InvalidOperationException("Could not parse get-process-signature MCP result.");
	}

	private static bool TrySerializeToJsonElement(object? value, out JsonElement element) {
		if (value is null) {
			element = default;
			return false;
		}

		element = JsonSerializer.SerializeToElement(value);
		return true;
	}

	private static bool TryExtractEnvelope(JsonElement element, out GetProcessSignatureEnvelope? envelope) {
		if (TryDeserialize(element, out envelope)) {
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryGetTextPayload(item, out string? textPayload) &&
					!string.IsNullOrWhiteSpace(textPayload) &&
					TryParseJson(textPayload!, out JsonElement textPayloadElement) &&
					TryDeserialize(textPayloadElement, out envelope)) {
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.String) {
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload) &&
				TryParseJson(textPayload!, out JsonElement textPayloadElement) &&
				TryDeserialize(textPayloadElement, out envelope)) {
				return true;
			}
		}

		envelope = null;
		return false;
	}

	private static bool TryDeserialize(JsonElement element, out GetProcessSignatureEnvelope? envelope) {
		try {
			if (element.ValueKind != JsonValueKind.Object) {
				envelope = null;
				return false;
			}

			GetProcessSignatureEnvelope? item = JsonSerializer.Deserialize<GetProcessSignatureEnvelope>(
				element.GetRawText(),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (item is null || (string.IsNullOrWhiteSpace(item.ProcessCode) && string.IsNullOrWhiteSpace(item.Error))) {
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
