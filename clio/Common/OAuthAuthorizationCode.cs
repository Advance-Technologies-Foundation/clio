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
    private readonly IFileSecurityHardening _hardening;
    private static string Root => Path.Combine(SettingsRepository.AppSettingsFolderPath, "tokens");

    public OAuthTokenStore(IFileSecurityHardening hardening) => _hardening = hardening;

    public string BuildKey(EnvironmentSettings environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        string stem = Sanitize(StripScheme(environment.Uri ?? throw new ArgumentException("Environment url is required.", nameof(environment))));
        string discriminator = string.Concat(environment.Login ?? string.Empty, "|", environment.Password ?? string.Empty,
            "|", environment.ClientId ?? string.Empty, "|", environment.IsNetCore);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(discriminator)))[..16].ToLowerInvariant();
        return $"{stem}_{hash}";
    }

    public bool TryRead(EnvironmentSettings environment, out OAuthTokenSet tokenSet)
    {
        string path = GetPath(environment);
        tokenSet = null;
        if (!File.Exists(path)) return false;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            {
                throw new UnauthorizedAccessException("OAuth token file has permissions wider than owner-only access.");
            }
        }
        try
        {
            PersistedToken token = JsonSerializer.Deserialize<PersistedToken>(File.ReadAllText(path));
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
        Directory.CreateDirectory(Root);
        _hardening.HardenDirectory(Root);
        string target = GetPath(environment);
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        PersistedToken value = new(tokenSet.AccessToken, tokenSet.RefreshToken, tokenSet.ExpiresAt,
            tokenSet.TokenEndpoint, tokenSet.ClientId, tokenSet.ObtainedAt, tokenSet.Identity);
        using (FileStream stream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.SequentialScan))
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            JsonSerializer.Serialize(stream, value);
        }
        try { File.Move(temp, target, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
        _hardening.HardenFile(target);
    }

    public void Delete(EnvironmentSettings environment) { string path = GetPath(environment); if (File.Exists(path)) File.Delete(path); }
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
        StringBuilder result = new(64); foreach (byte value in bytes) result.Append(alphabet[value % alphabet.Length]); return result.ToString();
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
		if (!string.Equals(state, expectedState, StringComparison.Ordinal)) throw new InvalidOperationException("OAuth callback state did not match.");
        if (!query.TryGetValue("code", out string code) || string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("OAuth callback did not contain an authorization code.");
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
    public OAuthAuthorizationCodeService(IHttpClientFactory httpClientFactory, IOAuthTokenStore store, ILogger logger,
        IServiceUrlBuilder serviceUrlBuilder, IProcessExecutor processExecutor)
    {
        _httpClientFactory = httpClientFactory; _store = store; _logger = logger; _serviceUrlBuilder = serviceUrlBuilder; _processExecutor = processExecutor;
    }

    public async Task<OAuthTokenSet> ResolveAsync(EnvironmentSettings environment, CancellationToken cancellationToken = default)
    {
        if (!_store.TryRead(environment, out OAuthTokenSet token)) throw MissingToken(environment);
        if (token.ExpiresAt - DateTimeOffset.UtcNow >= TimeSpan.FromSeconds(60)) return token;
        using HttpClient client = _httpClientFactory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, token.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "refresh_token", token.RefreshToken }, { ClientIdParameter, token.ClientId } })
        };
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (IsTerminalRefreshFailure(response.StatusCode, body)) _store.Delete(environment);
            throw new InvalidOperationException(DescribeOAuthFailure("OAuth token refresh failed", body));
        }
        OAuthResponse refreshed = JsonSerializer.Deserialize<OAuthResponse>(body) ?? throw MissingToken(environment);
        if (string.IsNullOrWhiteSpace(refreshed.access_token) || refreshed.expires_in < 0) { _store.Delete(environment); throw MissingToken(environment); }
        OAuthTokenSet updated = ToToken(refreshed, token.TokenEndpoint, token.ClientId, token.RefreshToken);
        _store.Write(environment, updated); return updated;
    }

    public async Task<OAuthTokenSet> LoginAsync(EnvironmentSettings environment, bool noBrowser, int timeoutMs, CancellationToken cancellationToken = default)
    {
        if (environment.AuthFlow != OAuthFlow.AuthorizationCode || string.IsNullOrWhiteSpace(environment.ClientId))
            throw new InvalidOperationException("Authorization-code sign-in requires a registered environment with --clientId.");
        DiscoveryDocument discovery = await DiscoverAsync(environment, cancellationToken);
        string verifier = OAuthAuthorizationCodeProtocol.CreateCodeVerifier(); string state = OAuthAuthorizationCodeProtocol.CreateState();
        string configuredRedirect = environment.RedirectUri;
        bool useLoopback = !noBrowser && (string.IsNullOrWhiteSpace(configuredRedirect) || IsLoopbackRedirect(configuredRedirect));
        int callbackPort = environment.RedirectPort ?? (Uri.TryCreate(configuredRedirect, UriKind.Absolute, out Uri configuredUri) && configuredUri.Port > 0 ? configuredUri.Port : 0);
        using TcpListener listener = useLoopback ? new TcpListener(IPAddress.Loopback, callbackPort) : null;
        listener?.Start();
        string redirect = configuredRedirect;
        if (useLoopback)
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            redirect = string.IsNullOrWhiteSpace(configuredRedirect)
                ? $"http://127.0.0.1:{port}/callback"
                : ReplacePort(configuredRedirect, port);
        }
        if (string.IsNullOrWhiteSpace(redirect)) throw new InvalidOperationException("OAuth authorization requires a registered --redirect-uri.");
        string auth = BuildAuthorizationUrl(discovery.authorization_endpoint, environment.ClientId, redirect, state, verifier);
        if (!noBrowser && TryOpenBrowser(auth)) _logger.WriteInfo("Complete sign-in in the browser; waiting for the callback..."); else _logger.WriteInfo($"Open this authorization URL: {auth}");
        string callback;
        try
        {
            callback = useLoopback ? await ReceiveCallbackAsync(listener, timeoutMs, cancellationToken) : (Console.ReadLine() ?? string.Empty);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("OAuth callback timed out. Retry with --no-browser and paste the full redirect URL.");
        }
        (string code, _) = OAuthAuthorizationCodeProtocol.ParseCallback(callback, state);
        using HttpClient client = _httpClientFactory.CreateClient();
        using HttpResponseMessage response = await client.PostAsync(discovery.token_endpoint,
            new FormUrlEncodedContent(new Dictionary<string, string> { { "grant_type", "authorization_code" }, { "code", code }, { "redirect_uri", redirect }, { ClientIdParameter, environment.ClientId }, { "code_verifier", verifier } }), cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(DescribeOAuthFailure("OAuth token exchange failed", body));
        OAuthResponse token = JsonSerializer.Deserialize<OAuthResponse>(body) ?? throw new InvalidOperationException("OAuth token exchange returned an invalid response.");
        ValidateTokenResponse(token);
        OAuthTokenSet result = ToToken(token, discovery.token_endpoint, environment.ClientId); _store.Write(environment, result); return result;
    }

    public async Task LogoutAsync(EnvironmentSettings environment, CancellationToken cancellationToken = default)
    {
        if (!_store.TryRead(environment, out OAuthTokenSet token)) return;
        try
        {
            DiscoveryDocument discovery = await DiscoverAsync(environment, cancellationToken);
            if (string.IsNullOrWhiteSpace(discovery.revocation_endpoint)) throw new InvalidOperationException("OpenID configuration lacks revocation_endpoint.");
            using HttpClient client = _httpClientFactory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(discovery.revocation_endpoint,
                new FormUrlEncodedContent(new Dictionary<string, string> { { "token", token.RefreshToken }, { "token_type_hint", "refresh_token" }, { ClientIdParameter, token.ClientId } }), cancellationToken);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("OAuth token revocation failed.");
        }
        finally { _store.Delete(environment); }
    }

    private async Task<DiscoveryDocument> DiscoverAsync(EnvironmentSettings environment, CancellationToken ct)
    {
        string key = environment.Uri + "|" + environment.IsNetCore; if (_discovery.TryGetValue(key, out DiscoveryDocument cached)) return cached;
        using HttpClient client = _httpClientFactory.CreateClient(); string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.OpenIdConfiguration, environment);
        string body = await client.GetStringAsync(url, ct); DiscoveryDocument doc = JsonSerializer.Deserialize<DiscoveryDocument>(body) ?? throw new InvalidOperationException("OpenID configuration is invalid.");
        if (string.IsNullOrWhiteSpace(doc.authorization_endpoint) || string.IsNullOrWhiteSpace(doc.token_endpoint)) throw new InvalidOperationException("OpenID configuration lacks authorization_endpoint or token_endpoint.");
        _discovery[key] = doc; return doc;
    }
	private static bool IsTerminalRefreshFailure(HttpStatusCode statusCode, string body) =>
		(statusCode >= HttpStatusCode.BadRequest && statusCode < HttpStatusCode.InternalServerError)
			&& (string.Equals(ReadError(body), "invalid_grant", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(ReadError(body), "invalid_client", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(ReadError(body), "unauthorized_client", StringComparison.OrdinalIgnoreCase)
				|| string.IsNullOrWhiteSpace(ReadError(body)));
	private static string DescribeOAuthFailure(string prefix, string body) {
		string error = ReadError(body);
		string description = SensitiveErrorTextRedactor.RedactForConsoleOrNull(ReadErrorDescription(body));
		return string.IsNullOrWhiteSpace(error) ? prefix + "." : string.IsNullOrWhiteSpace(description) ? $"{prefix}: {error}." : $"{prefix}: {error} ({description}).";
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
        && uri.Scheme == Uri.UriSchemeHttp && uri.Host == IPAddress.Loopback.ToString();
    private static string ReplacePort(string redirect, int port) { UriBuilder builder = new(redirect); builder.Port = port; return builder.Uri.ToString().TrimEnd('/'); }
    private bool TryOpenBrowser(string url) { try { string file = OperatingSystem.IsMacOS() ? "open" : OperatingSystem.IsWindows() ? "cmd.exe" : "xdg-open"; string[] args = OperatingSystem.IsWindows() ? ["/c", "start", "", url] : [url]; ProcessExecutionResult result = _processExecutor.ExecuteAndCaptureAsync(new ProcessExecutionOptions(file, string.Empty) { ArgumentList = args }).GetAwaiter().GetResult(); return result.Started; } catch { return false; } }
    private static async Task<string> ReceiveCallbackAsync(TcpListener listener, int timeout, CancellationToken ct)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout <= 0 ? 120000 : timeout);
        while (true)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(timeoutSource.Token);
            using NetworkStream stream = client.GetStream();
            using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            string request = await reader.ReadLineAsync(timeoutSource.Token) ?? string.Empty;
            string target = request.StartsWith("GET ", StringComparison.Ordinal) ? request[4..].Split(' ')[0] : string.Empty;
            string body = "<html><body>You can close this tab.</body></html>";
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            byte[] bytes = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n{body}");
            await stream.WriteAsync(bytes, timeoutSource.Token);
            if (target.Contains("code=", StringComparison.OrdinalIgnoreCase) || target.Contains("error=", StringComparison.OrdinalIgnoreCase))
            {
                return "http://127.0.0.1/" + target.TrimStart('/');
            }
        }
    }
    private static InvalidOperationException MissingToken(EnvironmentSettings e) { string name = string.IsNullOrWhiteSpace(e.EnvironmentName) ? e.Uri : e.EnvironmentName; return new($"Environment '{name}' uses SSO sign-in and has no valid session. Run: clio login -e {name}"); }
    private sealed record DiscoveryDocument(string authorization_endpoint, string token_endpoint, string revocation_endpoint);
    private sealed record OAuthResponse(string access_token, string refresh_token, int expires_in, string id_token);
}
