using System;
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
	private readonly EnvironmentSettings _settings;
	private readonly Lazy<HttpClient> _lazyHttpClient;
	private readonly CookieContainer _cookies = new();
	private readonly object _authLock = new();
	private bool _authenticated;
	private string? _csrfToken;

	public ODataPatchClient(EnvironmentSettings settings) {
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_lazyHttpClient = new Lazy<HttpClient>(CreateHttpClient);
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
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _csrfToken);
		} else {
			request.Headers.TryAddWithoutValidation("ForceUseSession", "true");
			if (!string.IsNullOrEmpty(_csrfToken)) {
				request.Headers.TryAddWithoutValidation("BPMCSRF", _csrfToken);
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
		string loginUrl = $"{baseUrl}/ServiceModel/AuthService.svc/Login";
		string payload = JsonSerializer.Serialize(new {
			UserName = _settings.Login,
			UserPassword = _settings.Password
		});
		using HttpRequestMessage request = new(HttpMethod.Post, loginUrl) {
			Content = new StringContent(payload, Encoding.UTF8, "application/json")
		};
		using CancellationTokenSource cts = new(30_000);
		using HttpResponseMessage response = _lazyHttpClient.Value.Send(request, cts.Token);
		string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
		if (!response.IsSuccessStatusCode) {
			throw new InvalidOperationException(
				$"Creatio Forms login failed ({(int)response.StatusCode}): {Truncate(body)}");
		}
		int code = ReadLoginCode(body);
		if (code != 0) {
			throw new InvalidOperationException($"Creatio Forms login rejected (Code {code}): {Truncate(body)}");
		}
		_csrfToken = ReadCsrfCookie(baseUrl);
		if (string.IsNullOrEmpty(_csrfToken)) {
			throw new InvalidOperationException("Creatio Forms login did not return a BPMCSRF cookie.");
		}
	}

	private static int ReadLoginCode(string body) {
		try {
			using JsonDocument doc = JsonDocument.Parse(body);
			return doc.RootElement.TryGetProperty("Code", out JsonElement codeEl)
				&& codeEl.ValueKind == JsonValueKind.Number
				? codeEl.GetInt32()
				: 0;
		} catch {
			return 0;
		}
	}

	private string? ReadCsrfCookie(string baseUrl) {
		CookieCollection cookies = _cookies.GetCookies(new Uri(baseUrl));
		foreach (Cookie cookie in cookies) {
			if (string.Equals(cookie.Name, "BPMCSRF", StringComparison.OrdinalIgnoreCase)) {
				return cookie.Value;
			}
		}
		return null;
	}

	private void AuthenticateOAuth() {
		string tokenUrl = $"{_settings.AuthAppUri?.TrimEnd('/')}/connect/token";
		using HttpRequestMessage request = new(HttpMethod.Post, tokenUrl) {
			Content = new FormUrlEncodedContent(new[] {
				new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "client_credentials"),
				new System.Collections.Generic.KeyValuePair<string, string>("client_id", _settings.ClientId),
				new System.Collections.Generic.KeyValuePair<string, string>("client_secret", _settings.ClientSecret)
			})
		};
		using CancellationTokenSource cts = new(30_000);
		using HttpResponseMessage response = _lazyHttpClient.Value.Send(request, cts.Token);
		string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
		if (!response.IsSuccessStatusCode) {
			throw new InvalidOperationException(
				$"Creatio OAuth token request failed ({(int)response.StatusCode}): {Truncate(body)}");
		}
		using JsonDocument doc = JsonDocument.Parse(body);
		_csrfToken = doc.RootElement.TryGetProperty("access_token", out JsonElement tokenEl)
			? tokenEl.GetString()
			: null;
		if (string.IsNullOrEmpty(_csrfToken)) {
			throw new InvalidOperationException("Creatio OAuth token response did not contain an access_token.");
		}
	}

	private static string Truncate(string value) =>
		string.IsNullOrEmpty(value) ? "<empty>" : value.Length > 500 ? value[..500] + "..." : value;

	public void Dispose() {
		if (_lazyHttpClient.IsValueCreated) {
			_lazyHttpClient.Value.Dispose();
		}
	}
}
