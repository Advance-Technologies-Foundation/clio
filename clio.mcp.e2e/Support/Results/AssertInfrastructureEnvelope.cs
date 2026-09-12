using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record AssertInfrastructureEnvelope(
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("exit-code")] int ExitCode,
	[property: JsonPropertyName("summary")] string Summary,
	[property: JsonPropertyName("sections")] AssertInfrastructureSectionsEnvelope Sections,
	[property: JsonPropertyName("database-candidates")] IReadOnlyList<AssertInfrastructureDatabaseCandidateEnvelope> DatabaseCandidates);

internal sealed record AssertInfrastructureSectionsEnvelope(
	[property: JsonPropertyName("k8")] AssertionResultEnvelope K8,
	[property: JsonPropertyName("local")] AssertionResultEnvelope Local,
	[property: JsonPropertyName("filesystem")] AssertionResultEnvelope Filesystem);

internal sealed record AssertionResultEnvelope(
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("scope")] string? Scope,
	[property: JsonPropertyName("failedAt")] string? FailedAt,
	[property: JsonPropertyName("reason")] string? Reason,
	[property: JsonPropertyName("details")] JsonElement? Details,
	[property: JsonPropertyName("resolved")] JsonElement? Resolved,
	[property: JsonPropertyName("context")] JsonElement? Context);

internal sealed record AssertInfrastructureDatabaseCandidateEnvelope(
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("engine")] string Engine,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("host")] string Host,
	[property: JsonPropertyName("port")] int Port,
	[property: JsonPropertyName("version")] string? Version,
	[property: JsonPropertyName("is-connectable")] bool? IsConnectable);

internal static class AssertInfrastructureResultParser
{
	public static AssertInfrastructureEnvelope Extract(CallToolResult callResult)
	{
		McpParseDiagnostics diagnostics = new();
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent) &&
			TryExtractEnvelope(structuredContent, out AssertInfrastructureEnvelope? structuredEnvelope, diagnostics))
		{
			return structuredEnvelope!;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content) &&
			TryExtractEnvelope(content, out AssertInfrastructureEnvelope? contentEnvelope, diagnostics))
		{
			return contentEnvelope!;
		}

		throw new InvalidOperationException($"Could not parse assert-infrastructure structured MCP result: {McpResultDiagnostics.Describe(callResult, diagnostics)}");
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

	private static bool TryExtractEnvelope(JsonElement element, out AssertInfrastructureEnvelope? envelope, McpParseDiagnostics diagnostics)
	{
		if (TryDeserialize(element, out envelope, diagnostics))
		{
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in element.EnumerateArray())
			{
				if (TryDeserialize(item, out envelope, diagnostics))
				{
					return true;
				}

				if (!TryGetTextPayload(item, out string? textPayload) || string.IsNullOrWhiteSpace(textPayload))
				{
					continue;
				}

				if (TryParseJson(textPayload, out JsonElement textPayloadElement, diagnostics) &&
					TryDeserialize(textPayloadElement, out envelope, diagnostics))
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
				TryDeserialize(textPayloadElement, out envelope, diagnostics))
			{
				return true;
			}
		}

		envelope = null;
		return false;
	}

	private static bool TryDeserialize(JsonElement element, out AssertInfrastructureEnvelope? envelope, McpParseDiagnostics diagnostics)
	{
		// The array-wrapper rule lives in McpParseDiagnostics.RecordDeserializeAttempt: a bare MCP
		// content-item array reaching this last-resort attempt must not be recorded as "JSON was present"
		// nor contribute its always-doomed exception to the failure message.
		bool isMeaningfulJsonCandidate = diagnostics.RecordDeserializeAttempt(element);
		try
		{
			envelope = JsonSerializer.Deserialize<AssertInfrastructureEnvelope>(
				element.GetRawText(),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return envelope is not null
				&& !string.IsNullOrWhiteSpace(envelope.Status)
				&& envelope.Sections is not null
				&& envelope.DatabaseCandidates is not null;
		}
		catch (JsonException exception)
		{
			diagnostics.RecordJsonException(exception, isMeaningfulJsonCandidate);
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

		if (TryGetProperty(element, "text", out JsonElement textElement) &&
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

	private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
	{
		if (element.TryGetProperty(propertyName, out propertyValue))
		{
			return true;
		}

		propertyValue = default;
		return false;
	}
}
