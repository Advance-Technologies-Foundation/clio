using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Common;
using CommandLine;

namespace Clio.Command.OAuthAppConfiguration;

/// <summary>
/// CLI options for reading the OAuth IdentityService configuration of a Creatio environment over REST.
/// </summary>
[Verb("get-identity-service-config",
	HelpText = "Read (or derive) the OAuth IdentityService configuration of a Creatio environment over REST")]
[FeatureToggle("deploy-identity")]
public sealed class GetIdentityServiceConfigOptions : RemoteCommandOptions
{
}

/// <summary>
/// Structured OAuth IdentityService configuration for an environment.
/// </summary>
/// <param name="IdentityServerUrl">Resolved IdentityService base URL, or null when none could be determined.</param>
/// <param name="Source">How the IdentityService URL was determined.</param>
/// <param name="ClientId">The <c>OAuth20IdentityServerClientId</c> system setting value, or null when unset.</param>
/// <param name="TokenEndpoint">The <c>{base}/connect/token</c> endpoint, or null when no URL was determined.</param>
/// <param name="DiscoveryEndpoint">The OpenID discovery endpoint, or null when no URL was determined.</param>
/// <param name="Reachable">Whether the discovery document responded with a success status.</param>
public sealed record GetIdentityServiceConfigResult(
	[property: JsonPropertyName("identityServerUrl")] string IdentityServerUrl,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("clientId")] string ClientId,
	[property: JsonPropertyName("tokenEndpoint")] string TokenEndpoint,
	[property: JsonPropertyName("discoveryEndpoint")] string DiscoveryEndpoint,
	[property: JsonPropertyName("reachable")] bool Reachable);

/// <summary>
/// Reads the OAuth IdentityService configuration of a Creatio environment over REST. It reads the
/// <c>OAuth20IdentityServerUrl</c> / <c>OAuth20IdentityServerClientId</c> system settings through
/// <see cref="ISysSettingsManager"/>; when the URL is empty it derives the <c>-is</c> identity host from
/// the Creatio host, then reports the token / discovery endpoints and whether the discovery document is
/// reachable. No filesystem or database access is required.
/// </summary>
public class GetIdentityServiceConfigCommand : Command<GetIdentityServiceConfigOptions>
{
	private const string IdentityServerUrlSettingCode = "OAuth20IdentityServerUrl";
	private const string IdentityServerClientIdSettingCode = "OAuth20IdentityServerClientId";

	private static readonly JsonSerializerOptions WriteIndentedOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly ISysSettingsManager _sysSettingsManager;
	private readonly IIdentityServerUrlResolver _urlResolver;
	private readonly IIdentityServerProbe _identityServerProbe;
	private readonly EnvironmentSettings _environmentSettings;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="GetIdentityServiceConfigCommand"/> class.
	/// </summary>
	public GetIdentityServiceConfigCommand(
		ISysSettingsManager sysSettingsManager,
		IIdentityServerUrlResolver urlResolver,
		IIdentityServerProbe identityServerProbe,
		EnvironmentSettings environmentSettings,
		ILogger logger) {
		_sysSettingsManager = sysSettingsManager;
		_urlResolver = urlResolver;
		_identityServerProbe = identityServerProbe;
		_environmentSettings = environmentSettings;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(GetIdentityServiceConfigOptions options) {
		try {
			GetIdentityServiceConfigResult result = GetConfig(options);
			_logger.WriteInfo(JsonSerializer.Serialize(result, WriteIndentedOptions));
			return 0;
		}
		catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>
	/// Reads (or derives) the IdentityService configuration for the current environment and probes the
	/// discovery document for reachability.
	/// </summary>
	/// <param name="options">Environment settings.</param>
	/// <returns>The structured IdentityService configuration.</returns>
	public virtual GetIdentityServiceConfigResult GetConfig(GetIdentityServiceConfigOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		string settingUrl = TryReadSetting(IdentityServerUrlSettingCode);
		string clientId = TryReadSetting(IdentityServerClientIdSettingCode);

		string identityServerUrl;
		IdentityServerUrlSource source;
		if (!string.IsNullOrWhiteSpace(settingUrl)) {
			identityServerUrl = settingUrl.TrimEnd('/');
			source = IdentityServerUrlSource.Setting;
		}
		else {
			identityServerUrl = _urlResolver.DeriveIdentityServerUrl(_environmentSettings?.Uri);
			source = string.IsNullOrWhiteSpace(identityServerUrl)
				? IdentityServerUrlSource.None
				: IdentityServerUrlSource.Derived;
		}

		bool hasUrl = !string.IsNullOrWhiteSpace(identityServerUrl);
		string tokenEndpoint = hasUrl ? _urlResolver.GetTokenEndpoint(identityServerUrl) : null;
		string discoveryEndpoint = hasUrl ? _urlResolver.GetDiscoveryEndpoint(identityServerUrl) : null;
		bool reachable = hasUrl && _identityServerProbe.IsDiscoveryReachable(identityServerUrl);

		return new GetIdentityServiceConfigResult(
			hasUrl ? identityServerUrl : null,
			source.ToString().ToLowerInvariant(),
			string.IsNullOrWhiteSpace(clientId) ? null : clientId,
			tokenEndpoint,
			discoveryEndpoint,
			reachable);
	}

	// A missing/unconfigured sys-setting must degrade to "not set", not fail the whole read.
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
