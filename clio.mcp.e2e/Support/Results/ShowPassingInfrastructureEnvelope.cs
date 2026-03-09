using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record ShowPassingInfrastructureEnvelope(
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("summary")] string Summary,
	[property: JsonPropertyName("kubernetes")] ShowPassingInfrastructureKubernetesEnvelope Kubernetes,
	[property: JsonPropertyName("local")] ShowPassingInfrastructureLocalEnvelope Local,
	[property: JsonPropertyName("filesystem")] ShowPassingInfrastructureFilesystemEnvelope Filesystem,
	[property: JsonPropertyName("recommendedDeployment")] ShowPassingInfrastructureRecommendationEnvelope? RecommendedDeployment,
	[property: JsonPropertyName("recommendedByEngine")] ShowPassingInfrastructureRecommendationsByEngineEnvelope RecommendedByEngine);

internal sealed record ShowPassingInfrastructureKubernetesEnvelope(
	[property: JsonPropertyName("isAvailable")] bool IsAvailable,
	[property: JsonPropertyName("databases")] IReadOnlyList<ShowPassingInfrastructureDatabaseCandidateEnvelope> Databases,
	[property: JsonPropertyName("redis")] ShowPassingInfrastructureRedisCandidateEnvelope? Redis);

internal sealed record ShowPassingInfrastructureLocalEnvelope(
	[property: JsonPropertyName("databases")] IReadOnlyList<ShowPassingInfrastructureDatabaseCandidateEnvelope> Databases,
	[property: JsonPropertyName("redisServers")] IReadOnlyList<ShowPassingInfrastructureRedisCandidateEnvelope> RedisServers);

internal sealed record ShowPassingInfrastructureFilesystemEnvelope(
	[property: JsonPropertyName("isAvailable")] bool IsAvailable,
	[property: JsonPropertyName("path")] string? Path,
	[property: JsonPropertyName("userIdentity")] string? UserIdentity,
	[property: JsonPropertyName("permission")] string? Permission);

internal sealed record ShowPassingInfrastructureDatabaseCandidateEnvelope(
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("engine")] string Engine,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("host")] string Host,
	[property: JsonPropertyName("port")] int Port,
	[property: JsonPropertyName("version")] string? Version,
	[property: JsonPropertyName("dbServerName")] string? DbServerName);

internal sealed record ShowPassingInfrastructureRedisCandidateEnvelope(
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("host")] string Host,
	[property: JsonPropertyName("port")] int Port,
	[property: JsonPropertyName("firstAvailableDb")] int FirstAvailableDb,
	[property: JsonPropertyName("redisServerName")] string? RedisServerName);

internal sealed record ShowPassingInfrastructureRecommendationsByEngineEnvelope(
	[property: JsonPropertyName("postgres")] ShowPassingInfrastructureRecommendationEnvelope? Postgres,
	[property: JsonPropertyName("mssql")] ShowPassingInfrastructureRecommendationEnvelope? Mssql);

internal sealed record ShowPassingInfrastructureRecommendationEnvelope(
	[property: JsonPropertyName("deploymentMode")] string DeploymentMode,
	[property: JsonPropertyName("dbEngine")] string DbEngine,
	[property: JsonPropertyName("dbServerName")] string? DbServerName,
	[property: JsonPropertyName("redisServerName")] string? RedisServerName,
	[property: JsonPropertyName("redisDb")] int RedisDb,
	[property: JsonPropertyName("deployCreatioArguments")] ShowPassingInfrastructureDeployCreatioArgumentsEnvelope DeployCreatioArguments);

internal sealed record ShowPassingInfrastructureDeployCreatioArgumentsEnvelope(
	[property: JsonPropertyName("db")] string Db,
	[property: JsonPropertyName("dbServerName")] string? DbServerName,
	[property: JsonPropertyName("redisServerName")] string? RedisServerName,
	[property: JsonPropertyName("redisDb")] int RedisDb);

internal static class ShowPassingInfrastructureResultParser
{
	public static ShowPassingInfrastructureEnvelope Extract(CallToolResult callResult)
	{
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent) &&
			TryExtractEnvelope(structuredContent, out ShowPassingInfrastructureEnvelope? structuredEnvelope))
		{
			return structuredEnvelope!;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content) &&
			TryExtractEnvelope(content, out ShowPassingInfrastructureEnvelope? contentEnvelope))
		{
			return contentEnvelope!;
		}

		throw new InvalidOperationException("Could not parse show-passing-infrastructure MCP result.");
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

	private static bool TryExtractEnvelope(JsonElement element, out ShowPassingInfrastructureEnvelope? envelope)
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

	private static bool TryDeserialize(JsonElement element, out ShowPassingInfrastructureEnvelope? envelope)
	{
		try
		{
			envelope = JsonSerializer.Deserialize<ShowPassingInfrastructureEnvelope>(
				element.GetRawText(),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return envelope is not null &&
				!string.IsNullOrWhiteSpace(envelope.Status) &&
				envelope.Kubernetes is not null &&
				envelope.Local is not null &&
				envelope.Filesystem is not null &&
				envelope.RecommendedByEngine is not null;
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
