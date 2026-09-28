using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common;

/// <summary>OAuth token response used by the authorization-code flow.</summary>
public sealed record OAuthTokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt,
    string TokenEndpoint, string ClientId, DateTimeOffset ObtainedAt, string Identity = null);

/// <summary>Persisted OAuth token cache contract.</summary>
public interface IOAuthTokenStore
{
    string BuildKey(EnvironmentSettings environment);
    bool TryRead(EnvironmentSettings environment, out OAuthTokenSet tokenSet);
    void Write(EnvironmentSettings environment, OAuthTokenSet tokenSet);
    void Delete(EnvironmentSettings environment);
}

/// <summary>Owner-only, atomic OAuth token storage under the clio home directory.</summary>
public sealed class OAuthTokenStore : IOAuthTokenStore
{
    private readonly IFileSystem _fileSystem;
    private readonly IFileSecurityHardening _hardening;
    // Resolved lazily so a CLIO_HOME override (honored by AppSettingsFolderPath) takes effect,
    // which is also how tests point the store at a temp directory.
    private static string Root => Path.Combine(SettingsRepository.AppSettingsFolderPath, "tokens");

    public OAuthTokenStore(IFileSystem fileSystem, IFileSecurityHardening hardening)
    {
        _fileSystem = fileSystem;
        _hardening = hardening;
    }

    public string BuildKey(EnvironmentSettings environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        string stem = Sanitize(StripScheme(environment.Uri ?? throw new ArgumentException("Environment url is required.", nameof(environment))));
        // Credentials stay out of the key: editing a stored login or password must not orphan a live
        // session, and a filename must not carry a hash a weak password could be recovered from.
        string discriminator = string.Concat(environment.ClientId ?? string.Empty, "|", environment.IsNetCore);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(discriminator)))[..16].ToLowerInvariant();
        return $"{stem}_{hash}";
    }

    public bool TryRead(EnvironmentSettings environment, out OAuthTokenSet tokenSet)
    {
        string path = GetPath(environment);
        tokenSet = null;
        if (!_fileSystem.ExistsFile(path)) return false;
        if (!_hardening.IsOwnerOnly(path))
        {
            throw new UnauthorizedAccessException("OAuth token file has permissions wider than owner-only access.");
        }
        try
        {
            PersistedToken token = JsonSerializer.Deserialize<PersistedToken>(_fileSystem.ReadAllText(path));
            if (token is null || string.IsNullOrWhiteSpace(token.access_token) || string.IsNullOrWhiteSpace(token.refresh_token)
                || !string.Equals(token.client_id, environment.ClientId, StringComparison.Ordinal)
                || !Uri.TryCreate(token.token_endpoint, UriKind.Absolute, out _)) return false;
            tokenSet = new(token.access_token, token.refresh_token, token.expires_at, token.token_endpoint,
                token.client_id, token.obtained_at, token.identity);
            return true;
        }
        catch (JsonException) { return false; }
    }

    public void Write(EnvironmentSettings environment, OAuthTokenSet tokenSet)
    {
        _fileSystem.CreateDirectoryIfNotExists(Root);
        _hardening.HardenDirectory(Root);
        string target = GetPath(environment);
        PersistedToken value = new(tokenSet.AccessToken, tokenSet.RefreshToken, tokenSet.ExpiresAt,
            tokenSet.TokenEndpoint, tokenSet.ClientId, tokenSet.ObtainedAt, tokenSet.Identity);
        // Owner-only at creation time and replaced in one indivisible step: a concurrent reader sees
        // either the whole old token or the whole new one, never a truncated prefix.
        _fileSystem.WriteOwnerOnlyTextToFileAtomic(target, JsonSerializer.Serialize(value));
        _hardening.HardenFile(target);
    }

    public void Delete(EnvironmentSettings environment) => _fileSystem.DeleteFileIfExists(GetPath(environment));
    private string GetPath(EnvironmentSettings environment) => Path.Combine(Root, BuildKey(environment) + ".json");
    private static string StripScheme(string value)
    {
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return value[8..];
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return value[7..];
        return value;
    }
    private static string Sanitize(string value) => new string(value.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray()).Trim('-', '.').ToLowerInvariant();
    private sealed record PersistedToken(string access_token, string refresh_token, DateTimeOffset expires_at, string token_endpoint,
        string client_id, DateTimeOffset obtained_at, string identity);
}

/// <summary>Pure OAuth helpers for PKCE and callback validation.</summary>
public static class OAuthAuthorizationCodeProtocol
{
    public static string CreateCodeVerifier()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
        Span<byte> bytes = stackalloc byte[64]; RandomNumberGenerator.Fill(bytes);
        StringBuilder result = new(64);
        foreach (byte value in bytes)
        {
            result.Append(alphabet[value % alphabet.Length]);
        }
        return result.ToString();
    }
    public static string CreateCodeChallenge(string verifier)
    {
        if (string.IsNullOrWhiteSpace(verifier) || verifier.Length is < 43 or > 128 || verifier.Any(c => !"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~".Contains(c)))
            throw new ArgumentException("The PKCE code verifier must be 43-128 RFC 7636 characters.", nameof(verifier));
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static (string Code, string State) ParseCallback(string redirectUrl, string expectedState)
    {
        if (!Uri.TryCreate(redirectUrl, UriKind.Absolute, out Uri uri)) throw new InvalidOperationException("The redirect URL is not valid.");
        Dictionary<string, string> query = new(StringComparer.Ordinal);
        foreach (string part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pieces = part.Split('=', 2);
            if (pieces.Length == 2) query[DecodeQuery(pieces[0])] = DecodeQuery(pieces[1]);
        }
        if (query.TryGetValue("error", out string error) && !string.IsNullOrWhiteSpace(error))
        {
            query.TryGetValue("error_description", out string description);
            string safeDescription = SensitiveErrorTextRedactor.RedactForConsoleOrNull(description);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(safeDescription)
                ? $"OAuth authorization failed: {error}."
                : $"OAuth authorization failed: {error} ({safeDescription}).");
        }
		query.TryGetValue("state", out string state);
		if (!string.Equals(state, expectedState, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("OAuth callback state did not match.");
		}
        if (!query.TryGetValue("code", out string code) || string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("OAuth callback did not contain an authorization code.");
        }
        return (code, state);
    }
    private static string Base64Url(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string DecodeQuery(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
}

/// <summary>Runs OAuth discovery and authorization-code exchange for one environment.</summary>
public interface IOAuthAuthorizationCodeService
{
    Task<OAuthTokenSet> LoginAsync(EnvironmentSettings environment, bool noBrowser, int timeoutMs, CancellationToken cancellationToken = default);
    Task<OAuthTokenSet> ResolveAsync(EnvironmentSettings environment, CancellationToken cancellationToken = default);
    /// <summary>
    /// Refreshes the access token even when it has not reached its expiry window yet, because the
    /// server has just rejected <paramref name="rejectedAccessToken"/>. A token other than the rejected
    /// one that is already cached or stored (another caller or process refreshed first) is returned as is.
    /// </summary>
    Task<OAuthTokenSet> RefreshAsync(EnvironmentSettings environment, string rejectedAccessToken,
        CancellationToken cancellationToken = default);
    Task LogoutAsync(EnvironmentSettings environment, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class OAuthAuthorizationCodeService : IOAuthAuthorizationCodeService
{
	private const string ClientIdParameter = "client_id";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOAuthTokenStore _store;
    private readonly ILogger _logger;
    private readonly IServiceUrlBuilder _serviceUrlBuilder;
    private readonly IProcessExecutor _processExecutor;
    private readonly ConcurrentDictionary<string, DiscoveryDocument> _discovery = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, OAuthTokenSet> _tokens = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    public OAuthAuthorizationCodeService(IHttpClientFactory httpClientFactory, IOAuthTokenStore store, ILogger logger,
        IServiceUrlBuilder serviceUrlBuilder, IProcessExecutor processExecutor)
    {
        _httpClientFactory = httpClientFactory; _store = store; _logger = logger; _serviceUrlBuilder = serviceUrlBuilder; _processExecutor = processExecutor;
    }

    public async Task<OAuthTokenSet> ResolveAsync(EnvironmentSettings environment, CancellationToken cancellationToken = default)
    {
        string cacheKey = _store.BuildKey(environment);
        if (_tokens.TryGetValue(cacheKey, out OAuthTokenSet cached) && IsFresh(cached)) return cached;
        // One refresh at a time: N concurrent resolutions would otherwise mean N token round-trips and
        // N file writes, and with refresh-token rotation the losers persist a token the server rotated away.
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (_tokens.TryGetValue(cacheKey, out cached) && IsFresh(cached)) return cached;
            OAuthTokenSet resolved = await ResolveCoreAsync(environment, cancellationToken);
            _tokens[cacheKey] = resolved;
            return resolved;
        }
        finally { _refreshGate.Release(); }
    }

    /// <inheritdoc />
    public async Task<OAuthTokenSet> RefreshAsync(EnvironmentSettings environment, string rejectedAccessToken,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = _store.BuildKey(environment);
        // Same gate as ResolveAsync: a burst of rejected requests must share one refresh, and the ones
        // that arrive after it find the new token below instead of rotating the refresh token again.
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (_tokens.TryGetValue(cacheKey, out OAuthTokenSet cached) && IsUsable(cached, rejectedAccessToken)) return cached;
            if (!_store.TryRead(environment, out OAuthTokenSet token)) throw MissingToken(environment);
            OAuthTokenSet resolved = IsUsable(token, rejectedAccessToken)
                ? token
                : await RefreshTokenAsync(environment, token, cancellationToken);
            _tokens[cacheKey] = resolved;
            return resolved;
        }
        finally { _refreshGate.Release(); }
    }

    private static bool IsFresh(OAuthTokenSet token) => token.ExpiresAt - DateTimeOffset.UtcNow >= TimeSpan.FromSeconds(60);

    private static bool IsUsable(OAuthTokenSet token, string rejectedAccessToken) =>
        IsFresh(token) && !string.Equals(token.AccessToken, rejectedAccessToken, StringComparison.Ordinal);

    private async Task<OAuthTokenSet> ResolveCoreAsync(EnvironmentSettings environment, CancellationToken cancellationToken)
    {
        if (!_store.TryRead(environment, out OAuthTokenSet token)) throw MissingToken(environment);
        if (IsFresh(token)) return token;
        return await RefreshTokenAsync(environment, token, cancellationToken);
    }

    private async Task<OAuthTokenSet> RefreshTokenAsync(EnvironmentSettings environment, OAuthTokenSet token,
        CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, token.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "refresh_token", token.RefreshToken }, { ClientIdParameter, token.ClientId } })
        };
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (IsTerminalRefreshFailure(response.StatusCode, body))
            {
                // The gate above is per process. Another clio process may have refreshed with the same
                // one-time refresh token first; its rotated session is in the store and must survive.
                if (_store.TryRead(environment, out OAuthTokenSet current)
                    && !string.Equals(current.RefreshToken, token.RefreshToken, StringComparison.Ordinal))
                {
                    return current;
                }
                DeleteToken(environment);
            }
            throw new InvalidOperationException(DescribeOAuthFailure("OAuth token refresh failed", body));
        }
        OAuthResponse refreshed = JsonSerializer.Deserialize<OAuthResponse>(body) ?? throw MissingToken(environment);
        if (string.IsNullOrWhiteSpace(refreshed.access_token) || refreshed.expires_in < 0) { DeleteToken(environment); throw MissingToken(environment); }
        OAuthTokenSet updated = ToToken(refreshed, token.TokenEndpoint, token.ClientId, token.RefreshToken);
        _store.Write(environment, updated); return updated;
    }

    public async Task<OAuthTokenSet> LoginAsync(EnvironmentSettings environment, bool noBrowser, int timeoutMs, CancellationToken cancellationToken = default)
    {
        EnsureAuthorizationCodeEnvironment(environment);
        DiscoveryDocument discovery = await DiscoverAsync(environment, cancellationToken);
        string verifier = OAuthAuthorizationCodeProtocol.CreateCodeVerifier(); string state = OAuthAuthorizationCodeProtocol.CreateState();
        string configuredRedirect = environment.RedirectUri;
        bool useLoopback = !noBrowser && (string.IsNullOrWhiteSpace(configuredRedirect) || IsLoopbackRedirect(configuredRedirect));
        int callbackPort = environment.RedirectPort ?? (Uri.TryCreate(configuredRedirect, UriKind.Absolute, out Uri configuredUri) && configuredUri.Port > 0 ? configuredUri.Port : 0);
        using TcpListener listener = useLoopback ? new TcpListener(IPAddress.Loopback, callbackPort) : null;
        listener?.Start();
        string redirect = ResolveRedirect(environment, listener, useLoopback, configuredRedirect);
        string auth = BuildAuthorizationUrl(discovery.authorization_endpoint, environment.ClientId, redirect, state, verifier);
        if (!noBrowser && TryOpenBrowser(auth)) _logger.WriteInfo("Complete sign-in in the browser; waiting for the callback..."); else _logger.WriteInfo($"Open this authorization URL: {auth}");
        string callback = await ReadCallbackAsync(listener, useLoopback, noBrowser, redirect, timeoutMs, cancellationToken);
        (string code, _) = OAuthAuthorizationCodeProtocol.ParseCallback(callback, state);
        OAuthTokenSet result = await ExchangeCodeAsync(discovery.token_endpoint, environment.ClientId, code, redirect, verifier, cancellationToken);
        _store.Write(environment, result);
        _tokens[_store.BuildKey(environment)] = result;
        return result;
    }

    private static string ResolveRedirect(EnvironmentSettings environment, TcpListener listener, bool useLoopback,
        string configuredRedirect)
    {
        if (useLoopback)
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return string.IsNullOrWhiteSpace(configuredRedirect)
                ? $"http://127.0.0.1:{port}/callback"
                : ReplacePort(configuredRedirect, port);
        }
        if (!string.IsNullOrWhiteSpace(configuredRedirect))
        {
            return configuredRedirect;
        }
        if (environment.RedirectPort is > 0)
        {
            // The documented registration is --redirect-port only, so derive the same loopback
            // redirect here instead of hard-failing on the paste-back path.
            return $"http://127.0.0.1:{environment.RedirectPort.Value}/callback";
        }
        throw new InvalidOperationException("OAuth authorization requires a registered --redirect-uri or --redirect-port.");
    }

    private static async Task<string> ReadCallbackAsync(TcpListener listener, bool useLoopback, bool noBrowser, string redirect,
        int timeoutMs, CancellationToken cancellationToken)
    {
        if (!useLoopback && !noBrowser)
        {
            throw new InvalidOperationException(
                $"Redirect '{redirect}' is not a loopback address, so clio cannot receive the callback. Retry with --no-browser and paste the full redirect URL.");
        }
        if (!useLoopback)
        {
            return Console.ReadLine() ?? string.Empty;
        }
        try
        {
            return await ReceiveCallbackAsync(listener, timeoutMs, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("OAuth callback timed out. Retry with --no-browser and paste the full redirect URL.");
        }
    }

    private static void EnsureAuthorizationCodeEnvironment(EnvironmentSettings environment)
    {
        if (environment.AuthFlow != OAuthFlow.AuthorizationCode || string.IsNullOrWhiteSpace(environment.ClientId))
        {
            throw new InvalidOperationException("Authorization-code sign-in requires a registered environment with --clientId.");
        }
    }

    private async Task<OAuthTokenSet> ExchangeCodeAsync(string endpoint, string clientId, string code, string redirect,
        string verifier, CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient();
        using HttpResponseMessage response = await client.PostAsync(endpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" }, { "code", code }, { "redirect_uri", redirect },
                { ClientIdParameter, clientId }, { "code_verifier", verifier }
            }), cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(DescribeOAuthFailure("OAuth token exchange failed", body));
        }
        OAuthResponse token = JsonSerializer.Deserialize<OAuthResponse>(body)
            ?? throw new InvalidOperationException("OAuth token exchange returned an invalid response.");
        ValidateTokenResponse(token);
        return ToToken(token, endpoint, clientId);
    }

    public async Task LogoutAsync(EnvironmentSettings environment, CancellationToken cancellationToken = default)
    {
        OAuthTokenSet token;
        try
        {
            if (!_store.TryRead(environment, out token)) return;
        }
        catch (UnauthorizedAccessException)
        {
            // A token file readable by others is not trusted enough to send to the server, but it is
            // still a local session the user asked to remove.
            DeleteToken(environment);
            throw new InvalidOperationException("The OAuth token file had permissions wider than owner-only access, so it was deleted without revoking the session on the server.");
        }
        try
        {
            DiscoveryDocument discovery = await DiscoverAsync(environment, cancellationToken);
            if (string.IsNullOrWhiteSpace(discovery.revocation_endpoint)) throw new InvalidOperationException("OpenID configuration lacks revocation_endpoint.");
            using HttpClient client = _httpClientFactory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(discovery.revocation_endpoint,
                new FormUrlEncodedContent(new Dictionary<string, string> { { "token", token.RefreshToken }, { "token_type_hint", "refresh_token" }, { ClientIdParameter, token.ClientId } }), cancellationToken);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("OAuth token revocation failed.");
        }
        finally { DeleteToken(environment); }
    }

    private void DeleteToken(EnvironmentSettings environment)
    {
        _tokens.TryRemove(_store.BuildKey(environment), out _);
        _store.Delete(environment);
    }

    private async Task<DiscoveryDocument> DiscoverAsync(EnvironmentSettings environment, CancellationToken ct)
    {
        string key = environment.Uri + "|" + environment.IsNetCore; if (_discovery.TryGetValue(key, out DiscoveryDocument cached)) return cached;
        using HttpClient client = _httpClientFactory.CreateClient(); string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.OpenIdConfiguration, environment);
        string body = await client.GetStringAsync(url, ct); DiscoveryDocument doc = JsonSerializer.Deserialize<DiscoveryDocument>(body) ?? throw new InvalidOperationException("OpenID configuration is invalid.");
        if (string.IsNullOrWhiteSpace(doc.authorization_endpoint) || string.IsNullOrWhiteSpace(doc.token_endpoint)) throw new InvalidOperationException("OpenID configuration lacks authorization_endpoint or token_endpoint.");
        EnsureHttpsEndpoint(doc.authorization_endpoint, nameof(doc.authorization_endpoint));
        EnsureHttpsEndpoint(doc.token_endpoint, nameof(doc.token_endpoint));
        if (!string.IsNullOrWhiteSpace(doc.revocation_endpoint)) EnsureHttpsEndpoint(doc.revocation_endpoint, nameof(doc.revocation_endpoint));
        _discovery[key] = doc; return doc;
    }
	internal static void EnsureHttpsEndpoint(string endpoint, string name)
	{
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidOperationException($"OpenID configuration {name} must be an absolute https URL.");
		}
	}
	private static bool IsTerminalRefreshFailure(HttpStatusCode statusCode, string body)
	{
		if (statusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)) return false;
		string error = ReadError(body);
		return string.Equals(error, "invalid_grant", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(error, "invalid_client", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(error, "unauthorized_client", StringComparison.OrdinalIgnoreCase);
	}
	private static string DescribeOAuthFailure(string prefix, string body)
	{
		string error = ReadError(body);
		string description = SensitiveErrorTextRedactor.RedactForConsoleOrNull(ReadErrorDescription(body));
		if (string.IsNullOrWhiteSpace(error)) return prefix + ".";
		if (string.IsNullOrWhiteSpace(description)) return $"{prefix}: {error}.";
		return $"{prefix}: {error} ({description}).";
	}
	private static string ReadError(string body) => ReadOAuthProperty(body, "error");
	private static string ReadErrorDescription(string body) => ReadOAuthProperty(body, "error_description");
	private static string ReadOAuthProperty(string body, string propertyName) {
		try {
			using JsonDocument document = JsonDocument.Parse(body);
			return document.RootElement.TryGetProperty(propertyName, out JsonElement value) ? value.GetString() : null;
		} catch (JsonException) { return null; }
	}
    private static OAuthTokenSet ToToken(OAuthResponse response, string endpoint, string clientId, string fallbackRefreshToken = null) => new(response.access_token, response.refresh_token ?? fallbackRefreshToken, DateTimeOffset.UtcNow.AddSeconds(response.expires_in), endpoint, clientId, DateTimeOffset.UtcNow);
    private static void ValidateTokenResponse(OAuthResponse response) { if (string.IsNullOrWhiteSpace(response.access_token) || string.IsNullOrWhiteSpace(response.refresh_token) || response.expires_in < 0) throw new InvalidOperationException("OAuth token exchange returned an incomplete token response."); }
	private static string BuildAuthorizationUrl(string endpoint, string clientId, string redirect, string state, string verifier) => endpoint + "?" + string.Join("&", new Dictionary<string, string> { { "response_type", "code" }, { ClientIdParameter, clientId }, { "redirect_uri", redirect }, { "state", state }, { "code_challenge", OAuthAuthorizationCodeProtocol.CreateCodeChallenge(verifier) }, { "code_challenge_method", "S256" }, { "scope", "offline_access" } }.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    private static bool IsLoopbackRedirect(string redirect) => Uri.TryCreate(redirect, UriKind.Absolute, out Uri uri)
        && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    // The redirect is matched exactly by the server, so the result keeps a trailing slash only when the
    // configured redirect had one.
    internal static string ReplacePort(string redirect, int port)
    {
        UriBuilder builder = new(redirect) { Port = port };
        string result = builder.Uri.ToString();
        return redirect.EndsWith('/') ? result : result.TrimEnd('/');
    }
	// The url reaches the platform handler as one opaque operand: routing it through cmd.exe /c start
	// would let an unquoted '&' in a discovery-supplied authorization endpoint start a second command.
	private bool TryOpenBrowser(string url) => _processExecutor.OpenWithDefaultHandler(url);
    internal static async Task<string> ReceiveCallbackAsync(TcpListener listener, int timeout, CancellationToken ct,
        int connectionTimeout = CallbackConnectionTimeoutMs)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout <= 0 ? 120000 : timeout);
        while (true)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(timeoutSource.Token);
            string target = await TryReadCallbackTargetAsync(client, connectionTimeout, timeoutSource.Token);
            if (target is not null && (target.Contains("code=", StringComparison.OrdinalIgnoreCase)
                || target.Contains("error=", StringComparison.OrdinalIgnoreCase)))
            {
                return "http://127.0.0.1/" + target.TrimStart('/');
            }
        }
    }

    private const int CallbackConnectionTimeoutMs = 5000;

    // A browser opens speculative connections that may never send a request, and a socket can be reset
    // mid-read. Each connection gets its own short deadline and its failures are dropped, so one idle or
    // broken socket cannot hold up the real redirect waiting in the backlog.
    private static async Task<string> TryReadCallbackTargetAsync(TcpClient client, int connectionTimeout,
        CancellationToken overall)
    {
        using CancellationTokenSource connectionSource = CancellationTokenSource.CreateLinkedTokenSource(overall);
        connectionSource.CancelAfter(connectionTimeout);
        try
        {
            using NetworkStream stream = client.GetStream();
            using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            string request = await reader.ReadLineAsync(connectionSource.Token) ?? string.Empty;
            string target = request.StartsWith("GET ", StringComparison.Ordinal) ? request[4..].Split(' ')[0] : string.Empty;
            string body = "<html><body>You can close this tab.</body></html>";
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            byte[] bytes = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n{body}");
            await stream.WriteAsync(bytes, connectionSource.Token);
            return target;
        }
        catch (OperationCanceledException) when (!overall.IsCancellationRequested) { return null; }
        catch (IOException) { return null; }
    }
    private static InvalidOperationException MissingToken(EnvironmentSettings e) { string name = string.IsNullOrWhiteSpace(e.EnvironmentName) ? e.Uri : e.EnvironmentName; return new($"Environment '{name}' uses SSO sign-in and has no valid session. Run: clio login -e {name}"); }
    private sealed record DiscoveryDocument(string authorization_endpoint, string token_endpoint, string revocation_endpoint);
    private sealed record OAuthResponse(string access_token, string refresh_token, int expires_in, string id_token);
}
