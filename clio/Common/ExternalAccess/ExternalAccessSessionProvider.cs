using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.BrowserSession;
using Creatio.Client;
using Newtonsoft.Json.Linq;

namespace Clio.Common.ExternalAccess;

/// <inheritdoc cref="IExternalAccessSessionProvider" />
public sealed class ExternalAccessSessionProvider : IExternalAccessSessionProvider {
	// The authentication service is served from the SITE ROOT on both hosts, with no "0/" web-app
	// prefix - which is why this route is deliberately absent from ServiceUrlBuilder.KnownRoutes:
	// ServiceUrlBuilder.Build prepends "0/" on .NET Framework and would produce a url that does not
	// exist. creatio.client builds AuthService.svc/Login from the site root for the same reason
	// (CreatioAuthenticationHandler.PasswordLoginAsync), and AGENTS.md names the site-root
	// authentication route as the documented exception to route registration.
	internal const string OAuthTokenLoginPath = "ServiceModel/AuthService.svc/OAuthTokenLogin";

	// Creatio's authentication cookie on both .NET Framework and .NET
	// (Terrasoft.Web.Common/AuthConsts.cs, AuthCookieName). creatio.client's session check keys on
	// this exact name, so a session without it would silently send the client back to a forms login.
	internal const string AuthCookieName = ".ASPXAUTH";

	// A relative Uri resolves against the base only when the base ends in a separator, so BuildBaseUri
	// always appends one.
	private const char UriPathSeparator = '/';

	private const int ExchangeTimeoutSeconds = 60;

	private readonly Func<CookieContainer, HttpMessageHandler> _handlerFactory;
	private readonly IBrowserSessionCache _cache;
	private readonly IFileSystem _fileSystem;

	/// <summary>Creates a provider that talks to the target site over the network.</summary>
	/// <param name="cache">Store for the exchanged session.</param>
	/// <param name="fileSystem">Reader for the cached session file.</param>
	public ExternalAccessSessionProvider(IBrowserSessionCache cache, IFileSystem fileSystem)
		: this(DefaultHandlerFactory, cache, fileSystem) { }

	/// <summary>
	/// Builds a provider with its own dependencies, for the few call sites that construct an
	/// application-client factory outside DI (obsolete compatibility constructors and the e2e probe).
	/// </summary>
	/// <returns>A ready provider.</returns>
	/// <remarks>Everything else resolves <see cref="IExternalAccessSessionProvider"/> from the container.</remarks>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Architecture", "CLIO001:Resolve behavior through DI",
		Justification = "Serves call sites that have no container to resolve from; see the summary.")]
	public static ExternalAccessSessionProvider CreateDefault() {
		Clio.Common.FileSystem fileSystem = new(new System.IO.Abstractions.FileSystem());
		return new ExternalAccessSessionProvider(
			new BrowserSessionCache(fileSystem, new FileSecurityHardening()), fileSystem);
	}

	// Test seam. The handler is built PER EXCHANGE around a fresh cookie container, because the
	// container is how the issued session leaves the response - a shared, factory-pooled handler would
	// mix sessions between environments. A null cache disables reuse and makes every call exchange,
	// which is what the exchange-level tests want.
	internal ExternalAccessSessionProvider(Func<CookieContainer, HttpMessageHandler> handlerFactory,
		IBrowserSessionCache cache = null, IFileSystem fileSystem = null) {
		_handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
		_cache = cache;
		_fileSystem = fileSystem;
	}

	/// <inheritdoc />
	public IReadOnlyList<CreatioSessionCookie> GetSession(EnvironmentSettings environment,
		string externalAccessToken) {
		ArgumentNullException.ThrowIfNull(environment);
		ExternalAccessGrant grant = ExternalAccessTokenReader.Read(externalAccessToken);
		ReportGrant(environment, grant);
		// An unreadable token cannot key a cache entry, so it goes straight to the exchange, which is
		// also where it will be rejected with a message that names the real problem.
		string cacheKey = grant is null ? null : BuildCacheKey(environment, grant);
		if (cacheKey is not null && TryReuseCachedSession(environment, cacheKey, out var cached)) {
			return cached;
		}
		IReadOnlyList<CreatioSessionCookie> session = Exchange(environment, externalAccessToken);
		if (cacheKey is not null) {
			StoreSession(cacheKey, session);
		}
		return session;
	}

	// The grant's restrictions decide whether half the commands can work at all, and the failures they
	// cause look like ordinary permission errors. The token states them, so name them once up front -
	// otherwise a refused push-pkg reads as a clio defect rather than as the terms of the grant.
	private static void ReportGrant(EnvironmentSettings environment, ExternalAccessGrant grant) {
		if (grant is null) {
			return;
		}
		string restrictions = grant.IsDataIsolationEnabled || grant.IsSystemOperationsRestricted
			? "data access denied: " + YesNo(grant.IsDataIsolationEnabled)
				+ ", configuration denied: " + YesNo(grant.IsSystemOperationsRestricted)
			: "no restrictions";
		string until = grant.GrantExpiresOnUtc is null
			? string.Empty
			: $", valid until {grant.GrantExpiresOnUtc:yyyy-MM-dd}";
		ConsoleLogger.Instance.WriteInfo(
			$"External access grant {grant.AccessId} on {environment.Uri} - {restrictions}{until}.");
	}

	private static string YesNo(bool value) => value ? "yes" : "no";

	// One cache entry per (environment, grant): two grants on the same site can carry different
	// restrictions, so their sessions are not interchangeable and must never share a file.
	private string BuildCacheKey(EnvironmentSettings environment, ExternalAccessGrant grant) =>
		_cache is null ? null : $"{_cache.BuildKey(environment)}_ea_{grant.AccessId}";

	private bool TryReuseCachedSession(EnvironmentSettings environment, string cacheKey,
		out IReadOnlyList<CreatioSessionCookie> session) {
		session = null;
		if (_cache is null || _fileSystem is null || !_cache.TryRead(cacheKey, out string path)) {
			return false;
		}
		IReadOnlyList<CreatioSessionCookie> cached;
		try {
			cached = StorageStateJson.ParseCookies(_fileSystem.ReadAllText(path))
				.Select(cookie => ToSessionCookie(cookie, environment))
				.ToList();
		} catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) {
			return false;
		}
		if (!cached.Any(cookie => cookie.Name.Equals(AuthCookieName, StringComparison.OrdinalIgnoreCase))
			|| !IsSessionAlive(environment, cached)) {
			_cache.Delete(cacheKey);
			return false;
		}
		session = cached;
		return true;
	}

	// The cookie's own expiry is NOT evidence: the grant can be revoked or the server session dropped
	// while the cookie still looks live, so aliveness is settled by an actual request.
	private bool IsSessionAlive(EnvironmentSettings environment,
		IReadOnlyList<CreatioSessionCookie> cookies) {
		try {
			Uri baseUri = BuildBaseUri(environment.Uri);
			CookieContainer container = new();
			foreach (CreatioSessionCookie cookie in cookies) {
				container.Add(baseUri, new Cookie(cookie.Name, cookie.Value, "/", baseUri.Host));
			}
			using HttpMessageHandler handler = _handlerFactory(container);
			using HttpClient client = new(handler) { Timeout = TimeSpan.FromSeconds(ExchangeTimeoutSeconds) };
			using HttpResponseMessage response = client
				.GetAsync(AuthenticatedBrowserLauncher.BuildShellUrl(environment)).GetAwaiter().GetResult();
			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
				|| (int)response.StatusCode is >= 300 and < 400) {
				return false;
			}
			string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
			return !ReauthExecutor.IsSessionExpiredResponse(body);
		} catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
			or ArgumentException or CookieException or UriFormatException) {
			return false;
		}
	}

	private void StoreSession(string cacheKey, IReadOnlyList<CreatioSessionCookie> session) {
		try {
			_cache.Write(cacheKey, StorageStateJson.Serialize(new StorageStateResult(
				session.Select(ToBrowserCookie).ToList())));
		} catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) {
			// A session that cannot be cached is still a usable session: the next command simply pays
			// for another exchange. Failing the command here would trade a working session for nothing.
		}
	}

	private static CreatioSessionCookie ToSessionCookie(BrowserCookie cookie,
		EnvironmentSettings environment) => new(
		cookie.Name,
		cookie.Value,
		string.IsNullOrEmpty(cookie.Domain) ? BuildBaseUri(environment.Uri).Host : cookie.Domain,
		string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
		cookie.HttpOnly,
		cookie.Secure,
		cookie.SameSite,
		cookie.Expires < 0
			? DateTime.MinValue
			: DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires).UtcDateTime);

	private static CreatioSessionCookie ToSessionCookie(Cookie cookie, Uri baseUri) => new(
		cookie.Name,
		cookie.Value,
		string.IsNullOrEmpty(cookie.Domain) ? baseUri.Host : cookie.Domain,
		string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
		cookie.HttpOnly,
		cookie.Secure,
		sameSite: null,
		cookie.Expires);

	private static BrowserCookie ToBrowserCookie(CreatioSessionCookie cookie) => new(
		cookie.Name,
		cookie.Value,
		cookie.Domain,
		string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
		cookie.HttpOnly,
		cookie.Secure,
		cookie.SameSite,
		cookie.Expires == DateTime.MinValue
			? -1
			: new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds());

	private static HttpMessageHandler DefaultHandlerFactory(CookieContainer cookies) => new HttpClientHandler {
		CookieContainer = cookies,
		UseCookies = true,
		AllowAutoRedirect = false
	};

	/// <inheritdoc />
	public IReadOnlyList<CreatioSessionCookie> Exchange(EnvironmentSettings environment,
		string externalAccessToken) =>
		ExchangeAsync(environment, externalAccessToken).GetAwaiter().GetResult();

	/// <inheritdoc />
	public async Task<IReadOnlyList<CreatioSessionCookie>> ExchangeAsync(EnvironmentSettings environment,
		string externalAccessToken, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(environment);
		if (string.IsNullOrWhiteSpace(environment.Uri)) {
			throw new ExternalAccessLoginException(
				"An external-access token was supplied but the environment url is missing; provide a non-empty url.");
		}
		string token = StripBearerPrefix(externalAccessToken);
		if (string.IsNullOrWhiteSpace(token)) {
			throw new ExternalAccessLoginException("The external-access token is empty.");
		}

		Uri baseUri = BuildBaseUri(environment.Uri);
		CookieContainer cookies = new();
		using HttpMessageHandler handler = _handlerFactory(cookies);
		using HttpClient client = new(handler) {
			Timeout = TimeSpan.FromSeconds(ExchangeTimeoutSeconds)
		};

		using HttpRequestMessage request = new(HttpMethod.Post, new Uri(baseUri, OAuthTokenLoginPath)) {
			// The service contract is BodyStyle.Bare over a single int parameter, so the body is a bare
			// JSON number - exactly what the platform's own ExternalAccessLogin page sends.
			Content = new StringContent(GetTimeZoneOffsetMinutes().ToString(
				System.Globalization.CultureInfo.InvariantCulture), Encoding.UTF8, "application/json")
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

		HttpResponseMessage response;
		try {
			response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
		} catch (HttpRequestException ex) {
			throw ExternalAccessLoginException.Connectivity(environment.Uri, ex);
		} catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
			throw ExternalAccessLoginException.Connectivity(environment.Uri, ex);
		}

		using (response) {
			string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) {
				throw ExternalAccessLoginException.Refused(environment.Uri,
					$"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
			}
			// A refusal is reported INSIDE a 200 response: LoginResponse carries a non-zero Code and a
			// Message. Reading only the status code would treat "grant expired" as a successful login
			// and fail later on an unrelated request instead.
			if (TryReadRefusal(body, out string reason)) {
				throw ExternalAccessLoginException.Refused(environment.Uri, reason);
			}
		}

		List<CreatioSessionCookie> issued = cookies.GetCookies(baseUri)
			.Cast<Cookie>()
			.Select(cookie => ToSessionCookie(cookie, baseUri))
			.ToList();
		if (!issued.Any(cookie => cookie.Name.Equals(AuthCookieName, StringComparison.OrdinalIgnoreCase))) {
			throw ExternalAccessLoginException.NoSessionCookie(environment.Uri);
		}
		return issued;
	}

	// Mirrors creatio.client's own forms login, which sends the offset of the local machine in minutes,
	// west of UTC positive - the sign convention of the browser's Date.getTimezoneOffset().
	private static int GetTimeZoneOffsetMinutes() => -(int)DateTimeOffset.Now.Offset.TotalMinutes;

	private static Uri BuildBaseUri(string environmentUri) {
		if (!Uri.TryCreate(environmentUri.TrimEnd(UriPathSeparator) + UriPathSeparator, UriKind.Absolute,
			out Uri baseUri)) {
			throw new ExternalAccessLoginException($"'{environmentUri}' is not a valid absolute url.");
		}
		return baseUri;
	}

	private static string StripBearerPrefix(string token) {
		if (string.IsNullOrWhiteSpace(token)) {
			return token;
		}
		const string prefix = "Bearer ";
		return token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			? token[prefix.Length..].Trim()
			: token.Trim();
	}

	private static bool TryReadRefusal(string body, out string reason) {
		reason = null;
		if (string.IsNullOrWhiteSpace(body)) {
			return false;
		}
		JObject parsed;
		try {
			parsed = JObject.Parse(body);
		} catch (Newtonsoft.Json.JsonException) {
			// An unparsable body is not a refusal on its own: the session-cookie check below is the
			// authoritative outcome, and it produces a better message than a JSON error would.
			return false;
		}
		int? code = parsed.Value<int?>("Code");
		if (code is null or 0) {
			return false;
		}
		string message = parsed.Value<string>("Message");
		reason = string.IsNullOrWhiteSpace(message) ? $"code {code}" : message;
		return true;
	}

}
