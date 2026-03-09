using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record FindAvailableIisPortEnvelope(
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("summary")] string Summary,
	[property: JsonPropertyName("rangeStart")] int RangeStart,
	[property: JsonPropertyName("rangeEnd")] int RangeEnd,
	[property: JsonPropertyName("firstAvailablePort")] int? FirstAvailablePort,
	[property: JsonPropertyName("iisBoundPortCount")] int IisBoundPortCount,
	[property: JsonPropertyName("activeTcpPortCount")] int ActiveTcpPortCount);

internal static class FindAvailableIisPortResultParser
{
	public static FindAvailableIisPortEnvelope Extract(CallToolResult callResult)
	{
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent) &&
			TryExtractEnvelope(structuredContent, out FindAvailableIisPortEnvelope? structuredEnvelope))
		{
			return structuredEnvelope!;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content) &&
			TryExtractEnvelope(content, out FindAvailableIisPortEnvelope? contentEnvelope))
		{
			return contentEnvelope!;
		}

		throw new InvalidOperationException("Could not parse find-empty-iis-port MCP result.");
	}

	private static bool TrySerializeToJsonElement(object? value, out JsonElement element)
	{
		if (value is null)
		{
			element = default;
			return false;
		}

		element = JsonSerializer.SerializeToElement(value);
		return true;
	}

	private static bool TryExtractEnvelope(JsonElement element, out FindAvailableIisPortEnvelope? envelope)
	{
		if (TryDeserialize(element, out envelope))
		{
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in element.EnumerateArray())
			{
				if (TryDeserialize(item, out envelope))
				{
					return true;
				}

				if (!TryGetTextPayload(item, out string? textPayload) || string.IsNullOrWhiteSpace(textPayload))
				{
					continue;
				}

				if (TryParseJson(textPayload, out JsonElement textPayloadElement) &&
					TryDeserialize(textPayloadElement, out envelope))
				{
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.String)
		{
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload) &&
				TryParseJson(textPayload, out JsonElement textPayloadElement) &&
				TryDeserialize(textPayloadElement, out envelope))
			{
				return true;
			}
		}

		envelope = null;
		return false;
	}

	private static bool TryDeserialize(JsonElement element, out FindAvailableIisPortEnvelope? envelope)
	{
		try
		{
			envelope = JsonSerializer.Deserialize<FindAvailableIisPortEnvelope>(
				element.GetRawText(),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return envelope is not null
				&& !string.IsNullOrWhiteSpace(envelope.Status)
				&& envelope.RangeStart > 0
				&& envelope.RangeEnd >= envelope.RangeStart
				&& envelope.IisBoundPortCount >= 0
				&& envelope.ActiveTcpPortCount >= 0;
		}
		catch (JsonException)
		{
			envelope = null;
			return false;
		}
	}

	private static bool TryGetTextPayload(JsonElement element, out string? textPayload)
	{
		textPayload = null;
		if (element.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		if (element.TryGetProperty("text", out JsonElement textElement) &&
			textElement.ValueKind == JsonValueKind.String)
		{
			textPayload = textElement.GetString();
			return true;
		}

		return false;
	}

	private static bool TryParseJson(string value, out JsonElement element)
	{
		try
		{
			element = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(value));
			return true;
		}
		catch (JsonException)
		{
			element = default;
			return false;
		}
	}
}
