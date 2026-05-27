using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Clio.Common;

/// <summary>
/// Issues OData v4 PATCH requests against a Creatio environment.
/// </summary>
/// <remarks>
/// The bundled <c>creatio.client</c> transport exposes only GET/POST/DELETE, and Creatio's
/// OData router rejects the <c>X-HTTP-Method-Override</c> tunnel, so partial updates require a
/// real PATCH issued here. This client authenticates independently of the shared
/// <see cref="IApplicationClient"/> session: Forms auth obtains cookies + BPMCSRF, OAuth obtains a
/// bearer token. Credentials come from the per-environment <see cref="EnvironmentSettings"/>.
/// </remarks>
public interface IODataPatchClient {
	/// <summary>Sends an OData PATCH to an absolute <paramref name="url"/> with a JSON body.</summary>
	/// <returns>The response body (empty for the typical 204 No Content).</returns>
	string ExecutePatch(string url, string requestData, int requestTimeout = 30_000);
}

/// <inheritdoc cref="IODataPatchClient"/>
public sealed class ODataPatchClient : IODataPatchClient, IDisposable {
	private const int AuthTimeoutMs = 30_000;

	private readonly EnvironmentSettings _settings;
	private readonly Lazy<HttpClient> _lazyHttpClient;
	private readonly CookieContainer _cookies = new();
	private readonly object _authLock = new();
	private volatile bool _authenticated;
	private string? _authToken;

	public ODataPatchClient(EnvironmentSettings settings) {
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_lazyHttpClient = new Lazy<HttpClient>(CreateHttpClient);
	}

	/// <summary>Test seam: injects a custom <see cref="HttpMessageHandler"/> so HTTP traffic can be stubbed.</summary>
	internal ODataPatchClient(EnvironmentSettings settings, HttpMessageHandler handler) {
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		ArgumentNullException.ThrowIfNull(handler);
		_lazyHttpClient = new Lazy<HttpClient>(() => new HttpClient(handler));
	}

	private bool IsOAuth => !string.IsNullOrWhiteSpace(_settings.ClientId);

	private HttpClient CreateHttpClient() {
		HttpClientHandler handler = new() {
			CookieContainer = _cookies,
			UseCookies = true,
			AllowAutoRedirect = false
		};
		return new HttpClient(handler);
	}

	public string ExecutePatch(string url, string requestData, int requestTimeout = 30_000) {
		if (string.IsNullOrWhiteSpace(url)) {
			throw new ArgumentException("PATCH url is required.", nameof(url));
		}
		EnsureAuthenticated(force: false);
		HttpResponseMessage response = SendPatch(url, requestData, requestTimeout);
		if (response.StatusCode == HttpStatusCode.Unauthorized) {
			// Session/token may have expired — re-authenticate once and retry.
			response.Dispose();
			EnsureAuthenticated(force: true);
			response = SendPatch(url, requestData, requestTimeout);
		}
		using (response) {
			string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
			if (!response.IsSuccessStatusCode) {
				throw new InvalidOperationException(
					$"OData PATCH failed ({(int)response.StatusCode} {response.ReasonPhrase}): {Truncate(body)}");
			}
			return body;
		}
	}

	private HttpResponseMessage SendPatch(string url, string requestData, int requestTimeout) {
		using HttpRequestMessage request = new(HttpMethod.Patch, url) {
			Content = new StringContent(requestData ?? "{}", Encoding.UTF8, "application/json")
		};
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		if (IsOAuth) {
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
		} else {
			request.Headers.TryAddWithoutValidation("ForceUseSession", "true");
			if (!string.IsNullOrEmpty(_authToken)) {
				request.Headers.TryAddWithoutValidation("BPMCSRF", _authToken);
			}
		}
		using CancellationTokenSource cts = new(requestTimeout);
		return _lazyHttpClient.Value.Send(request, cts.Token);
	}

	private void EnsureAuthenticated(bool force) {
		if (_authenticated && !force) {
			return;
		}
		lock (_authLock) {
			if (_authenticated && !force) {
				return;
			}
			if (IsOAuth) {
				AuthenticateOAuth();
			} else {
				AuthenticateForms();
			}
			_authenticated = true;
		}
	}

	private void AuthenticateForms() {
		string baseUrl = _settings.Uri?.TrimEnd('/')
			?? throw new InvalidOperationException("Environment Uri is required for OData PATCH.");
		// .NET Framework environments serve the web app (and AuthService) under the "0/" alias,
		// the same prefix IServiceUrlBuilder applies to the PATCH URL. Mirror it here so Forms
		// login and the subsequent PATCH target the same application.
		string alias = _settings.IsNetCore ? string.Empty : "0/";
		string loginUrl = $"{baseUrl}/{alias}ServiceModel/AuthService.svc/Login";
		string payload = JsonSerializer.Serialize(new {
			UserName = _settings.Login,
			UserPassword = _settings.Password
		});
		using HttpRequestMessage request = new(HttpMethod.Post, loginUrl) {
			Content = new StringContent(payload, Encoding.UTF8, "application/json")
		};
		string body = SendForAuth(request, "Creatio Forms login failed");
		if (!TryReadLoginCode(body, out int code)) {
			throw new InvalidOperationException(
				"Creatio Forms login response was not understood (no numeric Code field).");
		}
		if (code != 0) {
			throw new InvalidOperationException($"Creatio Forms login rejected (Code {code}).");
		}
		_authToken = ReadCsrfCookie(baseUrl);
		if (string.IsNullOrEmpty(_authToken)) {
			throw new InvalidOperationException("Creatio Forms login did not return a BPMCSRF cookie.");
		}
	}

	/// <summary>
	/// Parses the AuthService login <c>Code</c>. Returns <c>false</c> when the body is not JSON or has
	/// no numeric <c>Code</c>, so an unparseable response is never coerced to the success sentinel (0).
	/// </summary>
	private static bool TryReadLoginCode(string body, out int code) {
		code = 0;
		try {
			using JsonDocument doc = JsonDocument.Parse(body);
			if (doc.RootElement.TryGetProperty("Code", out JsonElement codeEl)
				&& codeEl.ValueKind == JsonValueKind.Number) {
				code = codeEl.GetInt32();
				return true;
			}
			return false;
		} catch (JsonException) {
			return false;
		}
	}

	private string? ReadCsrfCookie(string baseUrl) {
		return _cookies.GetCookies(new Uri(baseUrl))
			.Cast<Cookie>()
			.FirstOrDefault(cookie => string.Equals(cookie.Name, "BPMCSRF", StringComparison.OrdinalIgnoreCase))
			?.Value;
	}

	private void AuthenticateOAuth() {
		string authBase = _settings.AuthAppUri?.TrimEnd('/')
			?? throw new InvalidOperationException("Environment AuthAppUri is required for OData PATCH over OAuth.");
		string tokenUrl = $"{authBase}/connect/token";
		using HttpRequestMessage request = new(HttpMethod.Post, tokenUrl) {
			Content = new FormUrlEncodedContent(new[] {
				new KeyValuePair<string, string>("grant_type", "client_credentials"),
				new KeyValuePair<string, string>("client_id", _settings.ClientId),
				new KeyValuePair<string, string>("client_secret", _settings.ClientSecret)
			})
		};
		string body = SendForAuth(request, "Creatio OAuth token request failed");
		using JsonDocument doc = JsonDocument.Parse(body);
		_authToken = doc.RootElement.TryGetProperty("access_token", out JsonElement tokenEl)
			? tokenEl.GetString()
			: null;
		if (string.IsNullOrEmpty(_authToken)) {
			throw new InvalidOperationException("Creatio OAuth token response did not contain an access_token.");
		}
	}

	/// <summary>
	/// Sends an authentication request and returns the response body, throwing with
	/// <paramref name="failureContext"/> (and the HTTP status, never the body) on a non-success status.
	/// </summary>
	private string SendForAuth(HttpRequestMessage request, string failureContext) {
		using CancellationTokenSource cts = new(AuthTimeoutMs);
		using HttpResponseMessage response = _lazyHttpClient.Value.Send(request, cts.Token);
		string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
		if (!response.IsSuccessStatusCode) {
			throw new InvalidOperationException(
				$"{failureContext} (HTTP {(int)response.StatusCode} {response.ReasonPhrase}).");
		}
		return body;
	}

	private static string Truncate(string value) {
		if (string.IsNullOrEmpty(value)) {
			return "<empty>";
		}
		return value.Length > 500 ? value[..500] + "..." : value;
	}

	public void Dispose() {
		if (_lazyHttpClient.IsValueCreated) {
			_lazyHttpClient.Value.Dispose();
		}
	}
}
