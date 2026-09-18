using System;
using CommandLine;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>Options for <c>login</c>.</summary>
[Verb("login", Aliases = ["signin"], HelpText = "Sign in to an SSO-enabled Creatio environment")]
public sealed class LoginOptions : EnvironmentNameOptions
{
    [Option("no-browser", HelpText = "Print the authorization URL and paste the full redirect URL")]
    public bool NoBrowser { get; set; }
    [Option("timeout", Default = 120000, HelpText = "Callback timeout in milliseconds")]
    public int Timeout { get; set; }
    [Option("force", HelpText = "Run authorization even when a cached token exists")]
    public bool Force { get; set; }
}

/// <summary>Runs interactive authorization-code sign-in.</summary>
public sealed class LoginCommand : Command<LoginOptions>
{
    private readonly ISettingsRepository _settings; private readonly IOAuthAuthorizationCodeService _service; private readonly IOAuthTokenStore _store; private readonly ILogger _logger;
    public LoginCommand(ISettingsRepository settings, IOAuthAuthorizationCodeService service, IOAuthTokenStore store, ILogger logger) { _settings = settings; _service = service; _store = store; _logger = logger; }
    public override int Execute(LoginOptions options)
    {
        try
        {
            EnvironmentSettings environment = _settings.GetEnvironment(options);
            if (!options.Force && _store.TryRead(environment, out OAuthTokenSet cached))
            {
                if (cached.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(60))
                {
                    _logger.WriteInfo("A usable OAuth session is already cached.");
                    return 0;
                }
                try
                {
                    _service.ResolveAsync(environment).GetAwaiter().GetResult();
                    _logger.WriteInfo("A usable OAuth session is already cached.");
                    return 0;
                }
                catch (InvalidOperationException)
                {
                    // An expired or rejected refresh token requires interactive sign-in.
                }
            }
            _service.LoginAsync(environment, options.NoBrowser, options.Timeout, default).GetAwaiter().GetResult();
            _logger.WriteInfo("Login successful.");
            return 0;
        }
        catch (Exception exception)
        {
            _logger.WriteError(exception.Message);
            return 1;
        }
    }
}

/// <summary>Options for <c>logout</c>.</summary>
[Verb("logout", Aliases = ["signout"], HelpText = "Revoke the cached SSO session")]
public sealed class LogoutOptions : EnvironmentNameOptions { }

/// <summary>Revokes and removes a cached OAuth session.</summary>
public sealed class LogoutCommand : Command<LogoutOptions>
{
    private readonly ISettingsRepository _settings; private readonly IOAuthAuthorizationCodeService _service; private readonly ILogger _logger;
    public LogoutCommand(ISettingsRepository settings, IOAuthAuthorizationCodeService service, ILogger logger) { _settings = settings; _service = service; _logger = logger; }
    public override int Execute(LogoutOptions options)
    {
        EnvironmentSettings environment;
        try { environment = _settings.GetEnvironment(options); }
        catch (Exception e) { _logger.WriteError(e.Message); return 1; }
        try { _service.LogoutAsync(environment).GetAwaiter().GetResult(); _logger.WriteInfo("Logged out."); return 0; }
        catch (Exception e) { _logger.WriteWarning($"Local OAuth session was removed, but revocation failed: {e.Message}"); return 0; }
    }
}

/// <summary>Options for <c>auth-status</c>.</summary>
[Verb("auth-status", Aliases = ["whoami"], HelpText = "Show the cached SSO authentication status")]
public sealed class AuthStatusOptions : EnvironmentNameOptions { }

/// <summary>Reports cached OAuth state without opening a browser or refreshing it.</summary>
public sealed class AuthStatusCommand : Command<AuthStatusOptions>
{
    private readonly ISettingsRepository _settings; private readonly IOAuthTokenStore _store; private readonly ILogger _logger;
    public AuthStatusCommand(ISettingsRepository settings, IOAuthTokenStore store, ILogger logger) { _settings = settings; _store = store; _logger = logger; }
    public override int Execute(AuthStatusOptions options)
    {
        try
        {
            EnvironmentSettings environment = _settings.GetEnvironment(options);
            if (environment.AuthFlow != OAuthFlow.AuthorizationCode)
            {
                _logger.WriteInfo("Authentication flow: client-credentials");
                return 0;
            }
            if (!_store.TryRead(environment, out OAuthTokenSet token))
            {
                _logger.WriteError("No cached OAuth session. Run: clio login -e " + options.Environment);
                return 1;
            }
            bool usable = token.ExpiresAt > DateTimeOffset.UtcNow || !string.IsNullOrWhiteSpace(token.RefreshToken);
            _logger.WriteInfo($"Authentication flow: authorization-code; cached token: {(usable ? "yes" : "expired")}; access-token expiry: {token.ExpiresAt:O}");
            return usable ? 0 : 1;
        }
        catch (Exception exception)
        {
            _logger.WriteError(exception.Message);
            return 1;
        }
    }
}
