using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record ShowWebAppListEnvironmentEnvelope(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("uri")] string Uri,
	[property: JsonPropertyName("login")] string Login,
	[property: JsonPropertyName("password")] string Password,
	[property: JsonPropertyName("clientSecret")] string ClientSecret);

internal static class ShowWebAppListResultParser
{
	public static IReadOnlyList<ShowWebAppListEnvironmentEnvelope> Extract(CallToolResult callResult)
	{
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent) &&
			TryExtract(structuredContent, out IReadOnlyList<ShowWebAppListEnvironmentEnvelope> structuredResult))
		{
			return structuredResult;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content) &&
			TryExtract(content, out IReadOnlyList<ShowWebAppListEnvironmentEnvelope> contentResult))
		{
			return contentResult;
		}

		throw new InvalidOperationException("Could not parse show-webApp-list MCP result.");
	}

	/// <summary>
	/// Returns the tool payload as raw JSON so a test can assert on wrapper fields
	/// (<c>settingsFilePath</c>, <c>warnings</c>) that carry no environment data.
	/// </summary>
	public static string ExtractRawJson(CallToolResult callResult)
	{
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent))
		{
			return structuredContent.GetRawText();
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content))
		{
			return content.GetRawText();
		}

		throw new InvalidOperationException("Could not read the show-webApp-list MCP result payload.");
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

	private static bool TryExtract(JsonElement element, out IReadOnlyList<ShowWebAppListEnvironmentEnvelope> environments)
	{
		if (TryDeserialize(element, out environments))
		{
			return true;
		}

		// list-environments returns {environments, settingsFilePath, warnings} (ENG-94529): the list is
		// read from appsettings.json at call time and a read problem travels as a warning next to it.
		if (element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty("environments", out JsonElement wrapped) &&
			TryDeserialize(wrapped, out environments))
		{
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in element.EnumerateArray())
			{
				if (TryDeserialize(item, out environments))
				{
					return true;
				}

				if (!TryGetTextPayload(item, out string? textPayload) || string.IsNullOrWhiteSpace(textPayload))
				{
					continue;
				}

				if (TryParseJson(textPayload, out JsonElement textPayloadElement) &&
					TryExtract(textPayloadElement, out environments))
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
				TryExtract(textPayloadElement, out environments))
			{
				return true;
			}
		}

		environments = [];
		return false;
	}

	private static bool TryDeserialize(JsonElement element, out IReadOnlyList<ShowWebAppListEnvironmentEnvelope> environments)
	{
		try
		{
			ShowWebAppListEnvironmentEnvelope[]? deserialized = JsonSerializer.Deserialize<ShowWebAppListEnvironmentEnvelope[]>(
				element.GetRawText(),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (deserialized is { Length: > 0 } &&
				deserialized.Any(environment =>
					!string.IsNullOrWhiteSpace(environment.Name) ||
					!string.IsNullOrWhiteSpace(environment.Uri)))
			{
				environments = deserialized;
				return true;
			}
		}
		catch (JsonException)
		{
		}

		environments = [];
		return false;
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
