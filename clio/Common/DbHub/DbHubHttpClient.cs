using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.DbHub;

/// <summary>Verifies dbHub health and source discovery through its local HTTP transport.</summary>
public interface IDbHubHttpClient {
	/// <summary>Verifies server health and MCP initialization.</summary>
	DbHubVerificationResult VerifyServer(DbHubSettings settings);

	/// <summary>Verifies whether a source is discoverable after hot reload.</summary>
	DbHubVerificationResult VerifySource(DbHubSettings settings, string sourceId, bool expectedPresent,
		bool waitForReload);
}

/// <inheritdoc />
public sealed class DbHubHttpClient(IHttpClientFactory httpClientFactory) : IDbHubHttpClient {
	private const string OfflineCode = "DBHUB_LIVE_VERIFICATION_SKIPPED";
	private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

	/// <inheritdoc />
	public DbHubVerificationResult VerifyServer(DbHubSettings settings) => VerifyServerAsync(settings)
		.GetAwaiter().GetResult();

	/// <inheritdoc />
	public DbHubVerificationResult VerifySource(DbHubSettings settings, string sourceId, bool expectedPresent,
		bool waitForReload) => VerifySourceAsync(settings, sourceId, expectedPresent, waitForReload)
		.GetAwaiter().GetResult();

	private async Task<DbHubVerificationResult> VerifyServerAsync(DbHubSettings settings) {
		try {
			using HttpClient client = CreateClient(settings);
			using HttpResponseMessage health = await client.GetAsync("healthz");
			if (!health.IsSuccessStatusCode) {
				return Offline("dbHub health verification failed.");
			}
			using HttpResponseMessage initialize = await PostMcp(client,
				"{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"clio\",\"version\":\"1\"}}}");
			return initialize.IsSuccessStatusCode
				? new DbHubVerificationResult(true, true)
				: Offline("dbHub MCP verification failed.");
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) {
			return Offline("dbHub is offline or did not respond in time.");
		}
	}

	private async Task<DbHubVerificationResult> VerifySourceAsync(DbHubSettings settings, string sourceId,
		bool expectedPresent, bool waitForReload) {
		if (waitForReload) {
			await Task.Delay(TimeSpan.FromMilliseconds(650));
		}
		try {
			using HttpClient client = CreateClient(settings);
			for (int attempt = 0; attempt < 5; attempt++) {
				using HttpResponseMessage inventoryResponse = await client.GetAsync("api/sources");
				if (!inventoryResponse.IsSuccessStatusCode) {
					return Offline("dbHub source inventory verification failed.");
				}
				string inventoryJson = await inventoryResponse.Content.ReadAsStringAsync();
				bool present = ContainsSourceInventory(inventoryJson, sourceId);
				if (present != expectedPresent) {
					await Task.Delay(TimeSpan.FromMilliseconds(250));
					continue;
				}
				if (!expectedPresent) {
					return new DbHubVerificationResult(true, true);
				}
				using HttpResponseMessage response = await PostMcp(client,
					"{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");
				if (!response.IsSuccessStatusCode) {
					return Offline("dbHub MCP source verification failed.");
				}
				string json = await response.Content.ReadAsStringAsync();
				if (ContainsAnyTool(json)) {
					return new DbHubVerificationResult(true, true);
				}
				await Task.Delay(TimeSpan.FromMilliseconds(250));
			}
			return new DbHubVerificationResult(true, false,
				new DbHubWarning("dbHub did not observe the source change in time.",
					"The TOML update is valid; dbHub may still be reloading it.", OfflineCode));
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException) {
			return Offline("dbHub is offline; live source verification was skipped.");
		}
	}

	private HttpClient CreateClient(DbHubSettings settings) {
		HttpClient client = _httpClientFactory.CreateClient();
		client.BaseAddress = new Uri($"http://{settings.Host}:{settings.Port}/", UriKind.Absolute);
		client.Timeout = TimeSpan.FromSeconds(3);
		return client;
	}

	private static async Task<HttpResponseMessage> PostMcp(HttpClient client, string json) {
		using HttpRequestMessage request = new(HttpMethod.Post, "mcp") {
			Content = new StringContent(json, Encoding.UTF8, "application/json")
		};
		request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
		request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
		return await client.SendAsync(request);
	}

	private static bool ContainsAnyTool(string json) {
		using JsonDocument document = JsonDocument.Parse(json);
		if (!document.RootElement.TryGetProperty("result", out JsonElement result)
			|| !result.TryGetProperty("tools", out JsonElement tools)) {
			return false;
		}
		return tools.EnumerateArray().Any();
	}

	private static bool ContainsSourceInventory(string json, string sourceId) {
		using JsonDocument document = JsonDocument.Parse(json);
		return document.RootElement.ValueKind == JsonValueKind.Array
			&& document.RootElement.EnumerateArray().Any(source =>
				source.TryGetProperty("id", out JsonElement id)
				&& string.Equals(id.GetString(), sourceId, StringComparison.Ordinal));
	}

	private static DbHubVerificationResult Offline(string detail) => new(false, false,
		new DbHubWarning("dbHub live verification was skipped.", detail, OfflineCode));
}
