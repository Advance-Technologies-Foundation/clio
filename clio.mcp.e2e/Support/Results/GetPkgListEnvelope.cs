using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record GetPkgListEnvelope(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("version")] string Version,
	[property: JsonPropertyName("maintainer")] string Maintainer,
	[property: JsonPropertyName("uId")] string UId);

internal sealed record GetPkgListResponseEnvelope(
	[property: JsonPropertyName("packages")] GetPkgListEnvelope[] Packages,
	[property: JsonPropertyName("count")] int Count,
	[property: JsonPropertyName("total")] int Total,
	[property: JsonPropertyName("offset")] int Offset,
	[property: JsonPropertyName("limit")] int Limit,
	[property: JsonPropertyName("truncated")] bool Truncated);

internal static class GetPkgListResultParser {
	public static IReadOnlyList<GetPkgListEnvelope> Extract(CallToolResult callResult) {
		return ExtractResponse(callResult).Packages;
	}

	public static GetPkgListResponseEnvelope ExtractResponse(CallToolResult callResult) {
		McpParseDiagnostics diagnostics = new();
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent) &&
			TryExtractResponse(structuredContent, out GetPkgListResponseEnvelope? structuredResponse, diagnostics)) {
			return structuredResponse!;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content) &&
			TryExtractResponse(content, out GetPkgListResponseEnvelope? contentResponse, diagnostics)) {
			return contentResponse!;
		}

		throw new InvalidOperationException($"Could not parse list-packages MCP result: {McpResultDiagnostics.Describe(callResult, diagnostics)}");
	}

	private static bool TrySerializeToJsonElement(object? value, out JsonElement element) {
		if (value is null) {
			element = default;
			return false;
		}

		element = JsonSerializer.SerializeToElement(value);
		return true;
	}

	private static bool TryExtractResponse(JsonElement element, out GetPkgListResponseEnvelope? response, McpParseDiagnostics diagnostics) {
		if (TryDeserializeResponse(element, out response, diagnostics)) {
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryGetTextPayload(item, out string? textPayload) &&
					!string.IsNullOrWhiteSpace(textPayload) &&
					TryParseJson(textPayload, out JsonElement textPayloadElement, diagnostics) &&
					TryDeserializeResponse(textPayloadElement, out response, diagnostics)) {
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.String) {
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload) &&
				TryParseJson(textPayload, out JsonElement textPayloadElement, diagnostics) &&
				TryDeserializeResponse(textPayloadElement, out response, diagnostics)) {
				return true;
			}
		}

		response = null;
		return false;
	}

	private static bool TryDeserializeResponse(JsonElement element, out GetPkgListResponseEnvelope? response, McpParseDiagnostics diagnostics) {
		// The array-wrapper rule lives in McpParseDiagnostics.RecordDeserializeAttempt: a bare MCP
		// content-item array reaching this last-resort attempt must not be recorded as "JSON was present"
		// nor contribute its always-doomed exception to the failure message.
		bool isMeaningfulJsonCandidate = diagnostics.RecordDeserializeAttempt(element);
		try {
			GetPkgListResponseEnvelope? parsed = JsonSerializer.Deserialize<GetPkgListResponseEnvelope>(
				element.GetRawText(),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (parsed?.Packages is null) {
				response = null;
				return false;
			}

			response = parsed;
			return true;
		}
		catch (JsonException exception) {
			diagnostics.RecordJsonException(exception, isMeaningfulJsonCandidate);
			response = null;
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

	private static bool TryParseJson(string value, out JsonElement element, McpParseDiagnostics diagnostics) {
		try {
			element = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(value));
			return true;
		}
		catch (JsonException exception) {
			diagnostics.RecordJsonException(exception);
			element = default;
			return false;
		}
	}
}
