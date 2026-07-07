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
[FeatureToggle("deploy-identity")]
public sealed class VerifyOAuthAppOptions : RemoteCommandOptions
{
	/// <summary>
	/// Gets or sets the OAuth client identifier to verify.
	/// </summary>
	[Option("client-id", Required = true, HelpText = "OAuth client id to verify")]
	public string ClientId { get; set; }

	/// <summary>
	/// Gets or sets the OAuth client secret to verify.
	/// </summary>
	[Option("client-secret", Required = true, HelpText = "OAuth client secret to verify")]
	public string ClientSecret { get; set; }

	/// <summary>
	/// Gets or sets an explicit IdentityService base URL. When empty it is read from the
	/// <c>OAuth20IdentityServerUrl</c> system setting, then derived from the Creatio host.
	/// </summary>
	[Option("identity-server-url", Required = false,
		HelpText = "Explicit IdentityService base URL. Defaults to the OAuth20IdentityServerUrl system setting, then a derived -is host")]
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
		catch (Exception exception) {
			_logger.WriteError(exception.Message);
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
		if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret)) {
			throw new ArgumentException("Both --client-id and --client-secret are required.");
		}

		string identityServerUrl = ResolveIdentityServerUrl(options);
		if (string.IsNullOrWhiteSpace(identityServerUrl)) {
			throw new InvalidOperationException(
				"Could not determine the IdentityService URL. Pass --identity-server-url explicitly.");
		}

		string accessToken = _identityServerProbe.AcquireClientCredentialsToken(
			identityServerUrl, options.ClientId, options.ClientSecret);
		bool tokenAcquired = !string.IsNullOrWhiteSpace(accessToken);

		int dataServiceStatus = 0;
		if (tokenAcquired) {
			// Build the DataService URL through the single source of truth so the environment-specific
			// 0/ prefix (.NET Framework) vs no-prefix (.NET Core) is applied consistently with every
			// other Creatio call rather than hand-rolled in the probe.
			string selectQueryUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
			dataServiceStatus = _identityServerProbe.RunBearerDataServiceSmokeTest(selectQueryUrl, accessToken);
		}

		bool ok = tokenAcquired && dataServiceStatus == HttpOk;
		return new VerifyOAuthAppResult(tokenAcquired, dataServiceStatus, ok, identityServerUrl);
	}

	private string ResolveIdentityServerUrl(VerifyOAuthAppOptions options) {
		if (!string.IsNullOrWhiteSpace(options.IdentityServerUrl)) {
			return options.IdentityServerUrl.TrimEnd('/');
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
		catch (Exception ex) {
			_logger.WriteWarning($"Could not read system setting '{code}': {ex.Message}");
			return string.Empty;
		}
	}
}
