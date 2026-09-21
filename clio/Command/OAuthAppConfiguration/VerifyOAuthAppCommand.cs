using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Common;
using CommandLine;

namespace Clio.Command.OAuthAppConfiguration;

/// <summary>
/// CLI options for verifying a server-to-server OAuth app end to end over REST.
/// </summary>
[Verb("verify-oauth-app",
	HelpText = "Verify a server-to-server OAuth app: acquire a client_credentials token and run a bearer DataService smoke test")]
public sealed class VerifyOAuthAppOptions : RemoteCommandOptions
{
	/// <summary>
	/// Gets or sets the OAuth client identifier to verify.
	/// </summary>
	[Option("client-id", Required = false, HelpText = "OAuth client id to verify. Defaults to the registered environment credentials")]
	public string ClientId { get; set; }

	/// <summary>
	/// Gets or sets the OAuth client secret to verify.
	/// </summary>
	[Option("client-secret", Required = false, HelpText = "OAuth client secret to verify. Supply together with --client-id to override registered credentials")]
	public string ClientSecret { get; set; }

	/// <summary>
	/// Gets or sets an explicit IdentityService base URL. When empty it is read from the
	/// <c>OAuth20IdentityServerUrl</c> system setting, then derived from the Creatio host.
	/// </summary>
	[Option("identity-server-url", Required = false,
		HelpText = "Explicit IdentityService base URL. Defaults to registered AuthAppUri, then OAuth20IdentityServerUrl, then a derived -is host")]
	public string IdentityServerUrl { get; set; }
}

/// <summary>
/// Structured result of verifying a server-to-server OAuth app. The access token text is never returned
/// or logged — only whether it was acquired.
/// </summary>
/// <param name="TokenAcquired">Whether a <c>client_credentials</c> access token was acquired.</param>
/// <param name="DataServiceStatus">HTTP status returned by the bearer DataService smoke request (0 when skipped).</param>
/// <param name="Ok">Whether the token was acquired AND the DataService smoke request returned HTTP 200.</param>
/// <param name="IdentityServerUrl">The IdentityService base URL used for the token request.</param>
public sealed record VerifyOAuthAppResult(
	[property: JsonPropertyName("tokenAcquired")] bool TokenAcquired,
	[property: JsonPropertyName("dataServiceStatus")] int DataServiceStatus,
	[property: JsonPropertyName("ok")] bool Ok,
	[property: JsonPropertyName("identityServerUrl")] string IdentityServerUrl);

/// <summary>
/// Verifies a server-to-server OAuth app end to end over REST: acquires a <c>client_credentials</c>
/// access token from the IdentityService token endpoint, then runs a minimal bearer-authenticated
/// Creatio DataService smoke request with that token. The token text is never returned or logged.
/// </summary>
public class VerifyOAuthAppCommand : Command<VerifyOAuthAppOptions>
{
	internal const string VerificationFailureMessage = "OAuth verification failed. Check the IdentityService URL and CRM connectivity; configure OAuth credentials or supply both --client-id and --client-secret.";
	private const string IdentityServerUrlSettingCode = "OAuth20IdentityServerUrl";
	private const int HttpOk = 200;

	private static readonly JsonSerializerOptions WriteIndentedOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly ISysSettingsManager _sysSettingsManager;
	private readonly IIdentityServerUrlResolver _urlResolver;
	private readonly IIdentityServerProbe _identityServerProbe;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly EnvironmentSettings _environmentSettings;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="VerifyOAuthAppCommand"/> class.
	/// </summary>
	public VerifyOAuthAppCommand(
		ISysSettingsManager sysSettingsManager,
		IIdentityServerUrlResolver urlResolver,
		IIdentityServerProbe identityServerProbe,
		IServiceUrlBuilder serviceUrlBuilder,
		EnvironmentSettings environmentSettings,
		ILogger logger) {
		_sysSettingsManager = sysSettingsManager;
		_urlResolver = urlResolver;
		_identityServerProbe = identityServerProbe;
		_serviceUrlBuilder = serviceUrlBuilder;
		_environmentSettings = environmentSettings;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(VerifyOAuthAppOptions options) {
		try {
			VerifyOAuthAppResult result = Verify(options);
			_logger.WriteInfo(JsonSerializer.Serialize(result, WriteIndentedOptions));
			return result.Ok ? 0 : 1;
		}
		catch (Exception) {
			_logger.WriteError(VerificationFailureMessage);
			return 1;
		}
	}

	/// <summary>
	/// Acquires a <c>client_credentials</c> token and runs a bearer DataService smoke test for the supplied
	/// OAuth app credentials.
	/// </summary>
	/// <param name="options">Verification criteria and environment settings.</param>
	/// <returns>The structured verification result. The access token text is never surfaced.</returns>
	public virtual VerifyOAuthAppResult Verify(VerifyOAuthAppOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		bool explicitCredentials = options.ClientId is not null || options.ClientSecret is not null;
		string clientId = explicitCredentials ? options.ClientId : _environmentSettings.ClientId;
		string clientSecret = explicitCredentials ? options.ClientSecret : _environmentSettings.ClientSecret;
		if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)) {
			throw new ArgumentException("OAuth credentials are missing. Configure the environment or supply both --client-id and --client-secret.");
		}

		string identityServerUrl = ResolveIdentityServerUrl(options);
		if (!Uri.TryCreate(identityServerUrl, UriKind.Absolute, out Uri identityUri)
			|| identityUri.Scheme is not ("http" or "https")
			|| !string.IsNullOrEmpty(identityUri.UserInfo)
			|| !string.IsNullOrEmpty(identityUri.Query)
			|| !string.IsNullOrEmpty(identityUri.Fragment)) {
			throw new InvalidOperationException(
				"A valid IdentityService base URL is required. Pass --identity-server-url explicitly.");
		}

		string accessToken = _identityServerProbe.AcquireClientCredentialsToken(
			identityServerUrl, clientId, clientSecret);
		bool tokenAcquired = !string.IsNullOrWhiteSpace(accessToken);

		int dataServiceStatus = 0;
		if (tokenAcquired) {
			// Build the DataService URL through the single source of truth so the environment-specific
			// 0/ prefix (.NET Framework) vs no-prefix (.NET Core) is applied consistently with every
			// other Creatio call rather than hand-rolled in the probe.
			string selectQueryUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
			dataServiceStatus = _identityServerProbe.RunBearerDataServiceSmokeTest(
				_environmentSettings, selectQueryUrl, accessToken);
		}

		bool ok = tokenAcquired && dataServiceStatus == HttpOk;
		return new VerifyOAuthAppResult(tokenAcquired, dataServiceStatus, ok, identityServerUrl);
	}

	private string ResolveIdentityServerUrl(VerifyOAuthAppOptions options) {
		if (!string.IsNullOrWhiteSpace(options.IdentityServerUrl)) {
			return options.IdentityServerUrl.TrimEnd('/');
		}
		if (!string.IsNullOrWhiteSpace(_environmentSettings.AuthAppUri)) {
			// Registered OAuth settings store the token endpoint, while the probe accepts the base URL.
			const string tokenPath = "/connect/token";
			string savedUrl = _environmentSettings.AuthAppUri.TrimEnd('/');
			return savedUrl.EndsWith(tokenPath, StringComparison.OrdinalIgnoreCase)
				? savedUrl[..^tokenPath.Length]
				: savedUrl;
		}
		string settingUrl = TryReadSetting(IdentityServerUrlSettingCode);
		if (!string.IsNullOrWhiteSpace(settingUrl)) {
			return settingUrl.TrimEnd('/');
		}
		return _urlResolver.DeriveIdentityServerUrl(_environmentSettings?.Uri);
	}

	private string TryReadSetting(string code) {
		try {
			return _sysSettingsManager.GetSysSettingValueByCode(code);
		}
		catch (Exception) {
			_logger.WriteWarning($"Could not read system setting '{code}'.");
			return string.Empty;
		}
	}
}
