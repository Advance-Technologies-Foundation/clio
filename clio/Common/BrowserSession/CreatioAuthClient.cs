using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <inheritdoc cref="ICreatioAuthClient" />
public sealed class CreatioAuthClient : ICreatioAuthClient {
	/// <summary>Name of the dedicated <see cref="IHttpClientFactory"/> client (handler: no cookie jar, no auto-redirect).</summary>
	internal const string HttpClientName = "creatio-auth";

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ILogger _logger;

	/// <summary>Initializes the auth client.</summary>
	/// <param name="httpClientFactory">Factory for the dedicated auth HTTP client (see <see cref="HttpClientName"/>).</param>
	/// <param name="logger">Optional logger (cookie NAMES only are ever logged — never values).</param>
	public CreatioAuthClient(IHttpClientFactory httpClientFactory, ILogger logger = null) {
		_httpClientFactory = httpClientFactory;
		_logger = logger;
	}

	/// <inheritdoc />
	public async Task<StorageStateResult> LoginAsync(EnvironmentSettings env, CancellationToken ct = default) {
		ArgumentNullException.ThrowIfNull(env);

		// Forms-auth is the sole cookie-issuance path: the Story-11 spike confirmed there is no
		// OAuth token->cookie exchange for clio's token on either host. OAuth-only or incomplete
		// (login without password, or neither) environments fail closed — no request is attempted.
		if (string.IsNullOrEmpty(env.Login) || string.IsNullOrEmpty(env.Password)) {
			throw CreatioAuthenticationException.MissingFormsCredentials(env.Uri);
		}

		// AuthService.svc/Login is served at the SITE ROOT — NO "0/" WebAppAlias prefix — on BOTH
		// NetFW and NetCore. Live-confirmed 2026-06-10 against a NetFW studio instance:
		// POST {Uri}/0/ServiceModel/AuthService.svc/Login -> 401, but {Uri}/ServiceModel/AuthService.svc/Login
		// -> 200 {Code:0} + Set-Cookie. The "0/" alias is only for the Shell/data services, not auth.
		string loginUrl = $"{env.Uri.TrimEnd('/')}/ServiceModel/AuthService.svc/Login";

		HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
		string requestJson = JsonSerializer.Serialize(new { UserName = env.Login, UserPassword = env.Password });

		try {
			using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await http.PostAsync(loginUrl, content, ct).ConfigureAwait(false);
			string responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode || ReadAuthCode(responseBody) != 0) {
				throw CreatioAuthenticationException.InvalidCredentials(env.Uri);
			}
			IReadOnlyList<BrowserCookie> cookies = HarvestCookies(env.Uri, response);
			if (cookies.Count == 0) {
				throw CreatioAuthenticationException.NoCookies(env.Uri);
			}
			// Log cookie NAMES only — values are bearer secrets and must never reach a log sink (FR-10).
			_logger?.WriteDebug(
				$"Harvested {cookies.Count} Creatio session cookie(s): {string.Join(", ", cookies.Select(c => c.Name))}.");
			return new StorageStateResult(cookies);
		} catch (HttpRequestException) {
			throw CreatioAuthenticationException.Connectivity(env.Uri);
		} catch (TaskCanceledException) {
			// HttpClient surfaces timeouts as TaskCanceledException; distinguish a real caller-cancel.
			ct.ThrowIfCancellationRequested();
			throw CreatioAuthenticationException.Connectivity(env.Uri);
		}
	}

	// AuthService.svc/Login returns {"Code":0,...} on success. A non-JSON body (e.g. a login HTML
	// page from a wrong URL) or a non-zero Code is a failure.
	private static int ReadAuthCode(string responseBody) {
		if (string.IsNullOrWhiteSpace(responseBody)) {
			return -1;
		}
		try {
			JsonNode codeNode = JsonNode.Parse(responseBody)?["Code"];
			return codeNode?.GetValue<int>() ?? -1;
		} catch (JsonException) {
			return -1;
		} catch (InvalidOperationException) {
			return -1;
		} catch (FormatException) {
			return -1;
		}
	}

	// The dedicated HTTP client is configured with UseCookies=false, so Set-Cookie response headers
	// are visible here. Each header is parsed manually (not via CookieContainer.SetCookies, which
	// mis-splits Expires dates on their comma) into a Playwright-shaped BrowserCookie.
	private static IReadOnlyList<BrowserCookie> HarvestCookies(string envUri, HttpResponseMessage response) {
		if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> setCookieHeaders)) {
			return Array.Empty<BrowserCookie>();
		}
		string defaultDomain = Uri.TryCreate(envUri, UriKind.Absolute, out Uri parsed) ? parsed.Host : string.Empty;
		var cookies = new List<BrowserCookie>();
		foreach (string header in setCookieHeaders) {
			BrowserCookie cookie = ParseSetCookie(header, defaultDomain);
			if (cookie is not null) {
				cookies.Add(cookie);
			}
		}
		return cookies;
	}

	private static BrowserCookie ParseSetCookie(string header, string defaultDomain) {
		if (string.IsNullOrWhiteSpace(header)) {
			return null;
		}
		string[] parts = header.Split(';');
		string[] nameValue = parts[0].Split('=', 2);
		if (nameValue.Length != 2 || string.IsNullOrWhiteSpace(nameValue[0])) {
			return null;
		}
		string name = nameValue[0].Trim();
		string value = nameValue[1].Trim();
		string domain = defaultDomain;
		string path = "/";
		bool httpOnly = false;
		bool secure = false;
		string sameSite = "Lax";
		double expires = -1;

		for (int i = 1; i < parts.Length; i++) {
			string attribute = parts[i].Trim();
			if (attribute.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase)) {
				httpOnly = true;
			} else if (attribute.Equals("Secure", StringComparison.OrdinalIgnoreCase)) {
				secure = true;
			} else if (attribute.StartsWith("Domain=", StringComparison.OrdinalIgnoreCase)) {
				domain = attribute["Domain=".Length..].TrimStart('.');
			} else if (attribute.StartsWith("Path=", StringComparison.OrdinalIgnoreCase)) {
				path = attribute["Path=".Length..];
			} else if (attribute.StartsWith("SameSite=", StringComparison.OrdinalIgnoreCase)) {
				sameSite = NormalizeSameSite(attribute["SameSite=".Length..]);
			} else if (attribute.StartsWith("Expires=", StringComparison.OrdinalIgnoreCase)
				&& DateTimeOffset.TryParse(attribute["Expires=".Length..], CultureInfo.InvariantCulture,
					DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedExpiry)) {
				expires = parsedExpiry.ToUnixTimeSeconds();
			}
		}
		return new BrowserCookie(name, value, domain, path, httpOnly, secure, sameSite, expires);
	}

	private static string NormalizeSameSite(string raw) =>
		raw.Trim().ToLowerInvariant() switch {
			"strict" => "Strict",
			"none" => "None",
			_ => "Lax"
		};
}
