using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.DataForge;

public interface IDataForgeClient {
	Task<DataForgeHealthResult> CheckHealthAsync(
		DataForgeConfigRequest? configRequest = null,
		CancellationToken cancellationToken = default);

	Task<IReadOnlyList<SimilarTableResult>> FindSimilarTablesAsync(
		string query,
		int? limit = null,
		DataForgeConfigRequest? configRequest = null,
		CancellationToken cancellationToken = default);

	Task<IReadOnlyList<SimilarLookupResult>> FindSimilarLookupsAsync(
		string query,
		string? schemaName = null,
		int? limit = null,
		DataForgeConfigRequest? configRequest = null,
		CancellationToken cancellationToken = default);

	Task<IReadOnlyList<string>> GetTableRelationshipsAsync(
		string sourceTable,
		string targetTable,
		int? limit = null,
		DataForgeConfigRequest? configRequest = null,
		CancellationToken cancellationToken = default);
}

public sealed class DataForgeClient(
	HttpClient httpClient,
	IDataForgeConfigResolver configResolver,
	ILogger logger)
	: IDataForgeClient {
	private readonly JsonSerializerOptions _jsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	private OAuthTokenResponse? _cachedToken;
	private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

	public async Task<DataForgeHealthResult> CheckHealthAsync(
		DataForgeConfigRequest? configRequest = null,
		CancellationToken cancellationToken = default) {
		DataForgeResolvedConfig config = configResolver.Resolve(configRequest ?? new DataForgeConfigRequest());
		string correlationId = CreateCorrelationId();

		using HttpRequestMessage livenessRequest = await CreateRequestAsync(
			HttpMethod.Get,
			new Uri(new Uri(config.ServiceUrl), "liveness"),
			config,
			correlationId,
			cancellationToken);
		using HttpRequestMessage readinessRequest = await CreateRequestAsync(
			HttpMethod.Get,
			new Uri(new Uri(config.ServiceUrl), "readiness"),
			config,
			correlationId,
			cancellationToken);

		using HttpResponseMessage livenessResponse = await SendProbeAsync(
			livenessRequest,
			config.TimeoutMs,
			cancellationToken);
		bool liveness = livenessResponse.IsSuccessStatusCode;
		using HttpResponseMessage readinessResponse = await SendProbeAsync(
			readinessRequest,
			config.TimeoutMs,
			cancellationToken);
		bool readiness = readinessResponse.IsSuccessStatusCode;

		bool dataStructuresReady = false;
		bool lookupsReady = false;
		if (readiness) {
			using HttpRequestMessage dataStructureRequest = await CreateRequestAsync(
				HttpMethod.Get,
				new Uri(new Uri(config.ServiceUrl), "api/v1/dataStructure/readiness"),
				config,
				correlationId,
				cancellationToken);
			using HttpResponseMessage dataStructureResponse = await SendProbeAsync(
				dataStructureRequest,
				config.TimeoutMs,
				cancellationToken);
			dataStructuresReady = dataStructureResponse.IsSuccessStatusCode;

			using HttpRequestMessage lookupsRequest = await CreateRequestAsync(
				HttpMethod.Get,
				new Uri(new Uri(config.ServiceUrl), "api/v1/lookups/readiness"),
				config,
				correlationId,
				cancellationToken);
			using HttpResponseMessage lookupsResponse = await SendProbeAsync(
				lookupsRequest,
				config.TimeoutMs,
				cancellationToken);
			lookupsReady = lookupsResponse.IsSuccessStatusCode;
		}

		return new DataForgeHealthResult(
			liveness,
			readiness,
			dataStructuresReady,
			lookupsReady,
			correlationId);
	}

	public async Task<IReadOnlyList<SimilarTableResult>> FindSimilarTablesAsync(
		string query,
		int? limit = null,
		DataForgeConfigRequest? configRequest = null,
		CancellationToken cancellationToken = default) {
		DataForgeResolvedConfig config = configResolver.Resolve(configRequest ?? new DataForgeConfigRequest());
		string correlationId = CreateCorrelationId();
		Uri uri = BuildUri(
			config.ServiceUrl,
			"api/v1/dataStructure/tables/similarDetails",
			new Dictionary<string, string?> {
				["query"] = query,
				["limit"] = (limit ?? config.SimilarTablesLimit).ToString()
			});
		using HttpRequestMessage request = await CreateRequestAsync(
			HttpMethod.Get,
			uri,
			config,
			correlationId,
			cancellationToken);
		using HttpResponseMessage response = await SendAsync(request, config.TimeoutMs, cancellationToken);
		string payload = await response.Content.ReadAsStringAsync(cancellationToken);
		return JsonSerializer.Deserialize<List<SimilarTableResult>>(payload, _jsonOptions) ?? [];
	}

	public async Task<IReadOnlyList<SimilarLookupResult>> FindSimilarLookupsAsync(
		string query,
		string? schemaName = null,
		int? limit = null,
		DataForgeConfigRequest? configRequest = null,
		CancellationToken cancellationToken = default) {
		DataForgeResolvedConfig config = configResolver.Resolve(configRequest ?? new DataForgeConfigRequest());
		string correlationId = CreateCorrelationId();
		Dictionary<string, string?> queryParams = new() {
			["query"] = query,
			["limit"] = (limit ?? config.LookupResultLimit).ToString()
		};
		if (!string.IsNullOrWhiteSpace(schemaName)) {
			queryParams["lookupValueSchemaName"] = schemaName;
		}

		Uri uri = BuildUri(config.ServiceUrl, "api/v1/lookups/similar", queryParams);
		using HttpRequestMessage request = await CreateRequestAsync(
			HttpMethod.Get,
			uri,
			config,
			correlationId,
			cancellationToken);
		using HttpResponseMessage response = await SendAsync(request, config.TimeoutMs, cancellationToken);
		string payload = await response.Content.ReadAsStringAsync(cancellationToken);
		List<SimilarLookupServiceModel> serviceResult =
			JsonSerializer.Deserialize<List<SimilarLookupServiceModel>>(payload, _jsonOptions) ?? [];
		return serviceResult
			.Select(lookup => new SimilarLookupResult(
				lookup.ValueId ?? lookup.Id ?? string.Empty,
				lookup.ReferenceSchemaName ?? lookup.Name ?? string.Empty,
				lookup.ValueName ?? string.Empty,
				lookup.VectorSimilarityScore))
			.ToList();
	}

	public async Task<IReadOnlyList<string>> GetTableRelationshipsAsync(
		string sourceTable,
		string targetTable,
		int? limit = null,
		DataForgeConfigRequest? configRequest = null,
		CancellationToken cancellationToken = default) {
		DataForgeResolvedConfig config = configResolver.Resolve(configRequest ?? new DataForgeConfigRequest());
		string correlationId = CreateCorrelationId();
		Uri uri = BuildUri(
			config.ServiceUrl,
			"api/v1/dataStructure/tables/relations/cypher",
			new Dictionary<string, string?> {
				["sourceTable"] = sourceTable,
				["targetTable"] = targetTable,
				["limit"] = (limit ?? config.TableRelationshipsLimit).ToString()
			});
		using HttpRequestMessage request = await CreateRequestAsync(
			HttpMethod.Get,
			uri,
			config,
			correlationId,
			cancellationToken);
		using HttpResponseMessage response = await SendAsync(request, config.TimeoutMs, cancellationToken);
		string payload = await response.Content.ReadAsStringAsync(cancellationToken);
		return JsonSerializer.Deserialize<List<string>>(payload, _jsonOptions) ?? [];
	}

	private async Task<HttpRequestMessage> CreateRequestAsync(
		HttpMethod method,
		Uri uri,
		DataForgeResolvedConfig config,
		string correlationId,
		CancellationToken cancellationToken) {
		HttpRequestMessage request = new(method, uri);
		request.Headers.Add("X-Correlation-ID", correlationId);

		switch (config.AuthMode) {
			case DataForgeAuthMode.OAuthClientCredentials:
				string accessToken = await GetAccessTokenAsync(config, cancellationToken);
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
				break;
		}

		return request;
	}

	private async Task<string> GetAccessTokenAsync(DataForgeResolvedConfig config, CancellationToken cancellationToken) {
		if (_cachedToken is not null && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) {
			return _cachedToken.AccessToken;
		}

		if (string.IsNullOrWhiteSpace(config.TokenUrl)
			|| string.IsNullOrWhiteSpace(config.ClientId)
			|| string.IsNullOrWhiteSpace(config.ClientSecret)) {
			throw new InvalidOperationException("OAuth client-credentials configuration is incomplete for dataforge-service.");
		}

		Dictionary<string, string> form = new() {
			["grant_type"] = "client_credentials",
			["client_id"] = config.ClientId,
			["client_secret"] = config.ClientSecret
		};
		if (!string.IsNullOrWhiteSpace(config.Scope)) {
			form["scope"] = config.Scope;
		}

		using HttpRequestMessage request = new(HttpMethod.Post, config.TokenUrl) {
			Content = new FormUrlEncodedContent(form)
		};
		using HttpResponseMessage response = await SendAsync(request, config.TimeoutMs, cancellationToken);
		string payload = await response.Content.ReadAsStringAsync(cancellationToken);
		OAuthTokenResponse token = JsonSerializer.Deserialize<OAuthTokenResponse>(payload, _jsonOptions)
			?? throw new InvalidOperationException("Identity server returned an empty token response.");
		_cachedToken = token;
		_tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn <= 0 ? 3600 : token.ExpiresIn);
		return token.AccessToken;
	}

	private async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		int timeoutMs,
		CancellationToken cancellationToken) {
		return await SendCoreAsync(request, timeoutMs, cancellationToken, ensureSuccessStatusCode: true);
	}

	private async Task<HttpResponseMessage> SendProbeAsync(
		HttpRequestMessage request,
		int timeoutMs,
		CancellationToken cancellationToken) {
		return await SendCoreAsync(request, timeoutMs, cancellationToken, ensureSuccessStatusCode: false);
	}

	private async Task<HttpResponseMessage> SendCoreAsync(
		HttpRequestMessage request,
		int timeoutMs,
		CancellationToken cancellationToken,
		bool ensureSuccessStatusCode) {
		using CancellationTokenSource timeoutCts = timeoutMs > 0
			? new CancellationTokenSource(timeoutMs)
			: new CancellationTokenSource();
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken,
			timeoutCts.Token);
		try {
			HttpResponseMessage response = await httpClient.SendAsync(request, linked.Token);
			if (ensureSuccessStatusCode) {
				response.EnsureSuccessStatusCode();
			}
			return response;
		} catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutMs > 0) {
			throw new TimeoutException("DataForge request timed out.");
		} catch (Exception ex) {
			logger.WriteError(ex.Message);
			throw;
		}
	}

	private static Uri BuildUri(string serviceUrl, string relativePath, IDictionary<string, string?> query) {
		Uri baseUri = new(serviceUrl, UriKind.Absolute);
		UriBuilder builder = new(new Uri(baseUri, relativePath));
		string queryString = string.Join(
			"&",
			query
				.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
				.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
		builder.Query = queryString;
		return builder.Uri;
	}

	private static string CreateCorrelationId() => Guid.NewGuid().ToString("N");

	private sealed record OAuthTokenResponse(
		[property: JsonPropertyName("access_token")] string AccessToken,
		[property: JsonPropertyName("expires_in")] int ExpiresIn
	);

	private sealed record SimilarLookupServiceModel(
		[property: JsonPropertyName("id")] string? Id,
		[property: JsonPropertyName("name")] string? Name,
		[property: JsonPropertyName("referenceSchemaName")] string? ReferenceSchemaName,
		[property: JsonPropertyName("valueId")] string? ValueId,
		[property: JsonPropertyName("valueName")] string? ValueName,
		[property: JsonPropertyName("vectorSimilarityScore")] decimal? VectorSimilarityScore
	);
}
