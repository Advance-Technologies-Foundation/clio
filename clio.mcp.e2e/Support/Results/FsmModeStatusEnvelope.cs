using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record FsmModeStatusEnvelope(
	[property: JsonPropertyName("environmentName")] string EnvironmentName,
	[property: JsonPropertyName("mode")] string Mode,
	[property: JsonPropertyName("useStaticFileContent")] bool UseStaticFileContent,
	[property: JsonPropertyName("staticFileContent")] StaticFileContentEnvelope? StaticFileContent);

internal sealed record StaticFileContentEnvelope(
	[property: JsonPropertyName("schemasRuntimePath")] string? SchemasRuntimePath,
	[property: JsonPropertyName("resourcesRuntimePath")] string? ResourcesRuntimePath);

internal static class FsmModeStatusResultParser
{
	public static FsmModeStatusEnvelope Extract(CallToolResult callResult)
	{
		McpParseDiagnostics diagnostics = new();
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent) &&
			TryExtract(structuredContent, out FsmModeStatusEnvelope? structuredResult, diagnostics))
		{
			return structuredResult!;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content) &&
			TryExtract(content, out FsmModeStatusEnvelope? contentResult, diagnostics))
		{
			return contentResult!;
		}

		throw new InvalidOperationException($"Could not parse get-fsm-mode MCP result: {McpResultDiagnostics.Describe(callResult, diagnostics)}");
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

	private static bool TryExtract(JsonElement element, out FsmModeStatusEnvelope? status, McpParseDiagnostics diagnostics)
	{
		if (TryDeserialize(element, out status, diagnostics))
		{
			return true;
		}

		if (TryGetTextPayload(element, out string? objectTextPayload) &&
			!string.IsNullOrWhiteSpace(objectTextPayload) &&
			TryParseJson(objectTextPayload, out JsonElement objectTextPayloadElement, diagnostics) &&
			TryDeserialize(objectTextPayloadElement, out status, diagnostics))
		{
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in element.EnumerateArray())
			{
				if (TryDeserialize(item, out status, diagnostics))
				{
					return true;
				}

				if (!TryGetTextPayload(item, out string? textPayload) || string.IsNullOrWhiteSpace(textPayload))
				{
					continue;
				}

				if (TryParseJson(textPayload, out JsonElement textPayloadElement, diagnostics) &&
					TryDeserialize(textPayloadElement, out status, diagnostics))
				{
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty property in element.EnumerateObject())
			{
				if (TryExtract(property.Value, out status, diagnostics))
				{
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.String)
		{
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload) &&
				TryParseJson(textPayload, out JsonElement textPayloadElement, diagnostics) &&
				TryDeserialize(textPayloadElement, out status, diagnostics))
			{
				return true;
			}
		}

		status = null;
		return false;
	}

	private static bool TryDeserialize(JsonElement element, out FsmModeStatusEnvelope? status, McpParseDiagnostics diagnostics)
	{
		// The array-wrapper rule lives in McpParseDiagnostics.RecordDeserializeAttempt: a bare MCP
		// content-item array reaching this last-resort attempt must not be recorded as "JSON was present"
		// nor contribute its always-doomed exception to the failure message.
		bool isMeaningfulJsonCandidate = diagnostics.RecordDeserializeAttempt(element);
		try
		{
			status = JsonSerializer.Deserialize<FsmModeStatusEnvelope>(
				element.GetRawText(),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return status is not null &&
				!string.IsNullOrWhiteSpace(status.EnvironmentName) &&
				!string.IsNullOrWhiteSpace(status.Mode);
		}
		catch (JsonException exception)
		{
			diagnostics.RecordJsonException(exception, isMeaningfulJsonCandidate);
			status = null;
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

	private static bool TryParseJson(string value, out JsonElement element, McpParseDiagnostics diagnostics)
	{
		try
		{
			element = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(value));
			return true;
		}
		catch (JsonException exception)
		{
			diagnostics.RecordJsonException(exception);
			element = default;
			return false;
		}
	}
}
