using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Common;

namespace Clio.Command.OAuthAppConfiguration;

/// <summary>
/// Talks to a remote IdentityService (OAuth identity server) over HTTP for read-only probing and
/// server-to-server (<c>client_credentials</c>) token verification. IdentityService discovery and token
/// acquisition intentionally use an ordinary <see cref="HttpClient"/> because they target a different
/// host and require <c>application/x-www-form-urlencoded</c>. The Creatio DataService smoke request uses
/// <see cref="IApplicationClient"/> so origin validation and bearer transport remain owned by the shared
/// Creatio client.
/// </summary>
public interface IIdentityServerProbe
{
	/// <summary>
	/// Checks that OpenID discovery succeeds and contains an issuer and valid endpoint URLs.
	/// </summary>
	/// <param name="identityServerBaseUrl">IdentityService base URL.</param>
	/// <returns><see langword="true"/> when the discovery document is reachable.</returns>
	bool IsDiscoveryReachable(string identityServerBaseUrl);

	/// <summary>
	/// Acquires a <c>client_credentials</c> access token from the identity server token endpoint.
	/// </summary>
	/// <param name="identityServerBaseUrl">IdentityService base URL.</param>
	/// <param name="clientId">OAuth client identifier.</param>
	/// <param name="clientSecret">OAuth client secret.</param>
	/// <returns>The acquired access token, or an empty string when the token could not be acquired.</returns>
	string AcquireClientCredentialsToken(string identityServerBaseUrl, string clientId, string clientSecret);

	/// <summary>
	/// Performs a minimal bearer-authenticated DataService smoke request against Creatio and returns the
	/// HTTP status code so callers can confirm the freshly minted token is accepted end to end.
	/// </summary>
	/// <param name="selectQueryUrl">
	/// Fully resolved DataService SelectQuery URL. Build it with
	/// <see cref="IServiceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute)"/> for
	/// <see cref="ServiceUrlBuilder.KnownRoute.Select"/> so the environment-specific <c>0/</c> prefix is
	/// applied by the single source of truth rather than hand-rolled here.
	/// </param>
	/// <param name="accessToken">Bearer access token to present.</param>
	/// <returns>The HTTP status code returned by the DataService SelectQuery.</returns>
	int RunBearerDataServiceSmokeTest(string selectQueryUrl, string accessToken);

	/// <summary>Runs the bearer smoke test with the explicit target-environment shape.</summary>
	int RunBearerDataServiceSmokeTest(EnvironmentSettings environmentSettings, string selectQueryUrl,
		string accessToken) => RunBearerDataServiceSmokeTest(selectQueryUrl, accessToken);
}

/// <inheritdoc />
public sealed class IdentityServerProbe : IIdentityServerProbe
{
	private const string ContactTop1SelectQuery =
		"""{"rootSchemaName":"Contact","operationType":0,"allColumns":false,"rowCount":1,"columns":{"items":{"Id":{"expression":{"expressionType":0,"columnPath":"Id"}}}}}""";

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IApplicationClientFactory _applicationClientFactory;

	/// <summary>Initializes the probe with the authenticated Creatio client factory.</summary>
	public IdentityServerProbe(IHttpClientFactory httpClientFactory,
		IApplicationClientFactory applicationClientFactory) {
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_applicationClientFactory = applicationClientFactory
			?? throw new ArgumentNullException(nameof(applicationClientFactory));
	}

	/// <summary>Retains the historical constructor while routing Creatio calls through CreatioClient.</summary>
	[Obsolete("Use the overload that accepts IApplicationClientFactory.")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "S1133:Deprecated code should be removed",
		Justification = "This public constructor is retained intentionally for binary compatibility.")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Architecture", "CLIO001:Resolve behavior through DI",
		Justification = "The compatibility constructor must retain its historical public signature.")]
	public IdentityServerProbe(IHttpClientFactory httpClientFactory)
		: this(httpClientFactory, new ApplicationClientFactory(new NoReauthExecutor())) { }

	/// <inheritdoc />
	public bool IsDiscoveryReachable(string identityServerBaseUrl) {
		if (string.IsNullOrWhiteSpace(identityServerBaseUrl)) {
			return false;
		}
		try {
			using HttpClient client = _httpClientFactory.CreateClient();
			using HttpResponseMessage response = Task.Run(() =>
					client.GetAsync($"{identityServerBaseUrl.TrimEnd('/')}/.well-known/openid-configuration"))
				.GetAwaiter()
				.GetResult();
			if (!response.IsSuccessStatusCode) {
				return false;
			}
			using JsonDocument document = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
			return document.RootElement.ValueKind == JsonValueKind.Object
				&& document.RootElement.TryGetProperty("issuer", out JsonElement issuer)
				&& issuer.ValueKind == JsonValueKind.String
				&& !string.IsNullOrWhiteSpace(issuer.GetString())
				&& new[] { "token_endpoint", "jwks_uri", "authorization_endpoint" }.All(name =>
					document.RootElement.TryGetProperty(name, out JsonElement value)
					&& value.ValueKind == JsonValueKind.String
					&& Uri.TryCreate(value.GetString(), UriKind.Absolute, out Uri uri)
					&& uri.Scheme is "http" or "https"
					&& string.IsNullOrEmpty(uri.UserInfo));
		}
		catch (JsonException) {
			return false;
		}
		catch (HttpRequestException) {
			return false;
		}
		catch (TaskCanceledException) {
			return false;
		}
	}

	/// <inheritdoc />
	public string AcquireClientCredentialsToken(string identityServerBaseUrl, string clientId, string clientSecret) {
		if (string.IsNullOrWhiteSpace(identityServerBaseUrl)
			|| string.IsNullOrWhiteSpace(clientId)
			|| string.IsNullOrWhiteSpace(clientSecret)) {
			return string.Empty;
		}
		try {
			using HttpClient client = _httpClientFactory.CreateClient();
			using FormUrlEncodedContent content = new(new Dictionary<string, string> {
				["grant_type"] = "client_credentials",
				["client_id"] = clientId,
				["client_secret"] = clientSecret
			});
			content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
			using HttpResponseMessage response = Task.Run(() =>
					client.PostAsync($"{identityServerBaseUrl.TrimEnd('/')}/connect/token", content))
				.GetAwaiter()
				.GetResult();
			if (!response.IsSuccessStatusCode) {
				return string.Empty;
			}
			string body = Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
			return ExtractAccessToken(body);
		}
		catch (HttpRequestException) {
			throw new InvalidOperationException("OAuth token request failed. Check the IdentityService URL and connectivity.");
		}
		catch (TaskCanceledException) {
			throw new InvalidOperationException("OAuth token request timed out. Check IdentityService connectivity.");
		}
	}

	/// <inheritdoc />
	public int RunBearerDataServiceSmokeTest(string selectQueryUrl, string accessToken) {
		if (!Uri.TryCreate(selectQueryUrl, UriKind.Absolute, out Uri selectUri)) {
			return 0;
		}
		return RunBearerDataServiceSmokeTest(new EnvironmentSettings {
			Uri = selectUri.GetLeftPart(UriPartial.Authority)
		}, selectQueryUrl, accessToken);
	}

	/// <inheritdoc />
	public int RunBearerDataServiceSmokeTest(EnvironmentSettings environmentSettings,
		string selectQueryUrl, string accessToken) {
		if (string.IsNullOrWhiteSpace(selectQueryUrl) || string.IsNullOrWhiteSpace(accessToken)) {
			return 0;
		}
		ArgumentNullException.ThrowIfNull(environmentSettings);
		try {
			using IOwnedApplicationClient client = _applicationClientFactory.CreateBearerEnvironmentClient(
				environmentSettings, accessToken);
			using HttpResponseMessage response = client.ExecutePostRequestAsync(
				selectQueryUrl, ContactTop1SelectQuery).GetAwaiter().GetResult();
			if (response.StatusCode == System.Net.HttpStatusCode.OK) {
				using JsonDocument document = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
				if (document.RootElement.ValueKind != JsonValueKind.Object
					|| !document.RootElement.TryGetProperty("success", out JsonElement success)
					|| success.ValueKind != JsonValueKind.True) {
					throw new InvalidOperationException("CRM OAuth smoke request did not return a successful DataService response.");
				}
			}
			return (int)response.StatusCode;
		}
		catch (JsonException) {
			throw new InvalidOperationException("CRM OAuth smoke request returned invalid JSON.");
		}
		catch (HttpRequestException) {
			throw new InvalidOperationException("CRM OAuth smoke request failed. Check the target URL and connectivity.");
		}
		catch (TaskCanceledException) {
			throw new InvalidOperationException("CRM OAuth smoke request timed out. Check target connectivity.");
		}
	}

	private static string ExtractAccessToken(string tokenResponseJson) {
		if (string.IsNullOrWhiteSpace(tokenResponseJson)) {
			return string.Empty;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(tokenResponseJson);
			if (document.RootElement.ValueKind != JsonValueKind.Object
				|| !document.RootElement.TryGetProperty("token_type", out JsonElement type)
				|| type.ValueKind != JsonValueKind.String
				|| !string.Equals(type.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase)
				|| !document.RootElement.TryGetProperty("access_token", out JsonElement token)
				|| token.ValueKind != JsonValueKind.String) {
				return string.Empty;
			}
			string value = token.GetString();
			return string.IsNullOrWhiteSpace(value) || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ? string.Empty : value;
		}
		catch (JsonException) {
			return string.Empty;
		}
	}
}
