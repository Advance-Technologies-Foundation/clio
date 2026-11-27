using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using CommandLine;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Command;

#region Class: GetSsoSecurityEventsOptions

/// <summary>
///     Options for the <c>get-sso-security-events</c> command.
/// </summary>
[Verb("get-sso-security-events",
	HelpText = "Read the security events state of an SSO provider (SsoSecurityEvents, Creatio 10.2+)")]
[FeatureToggle(SsoSecurityEventsRequirement.FeatureName)]
public class GetSsoSecurityEventsOptions : SsoSecurityEventsOptions
{
}

#endregion

#region Class: GetSsoSecurityEventsCommand

/// <summary>
///     Reads the security events state of one SSO provider through <c>GetProviderState</c>.
/// </summary>
public class GetSsoSecurityEventsCommand : SsoSecurityEventsCommand<GetSsoSecurityEventsOptions>
{

	/// <summary>
	///     Initializes a new instance of the <see cref="GetSsoSecurityEventsCommand" /> class.
	/// </summary>
	public GetSsoSecurityEventsCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings, serviceUrlBuilder) { }

	/// <inheritdoc />
	protected override ServiceUrlBuilder.KnownRoute Route =>
		ServiceUrlBuilder.KnownRoute.SsoSecurityEventsGetProviderState;

	/// <inheritdoc />
	protected override bool ChangesSite => false;

	/// <inheritdoc />
	protected override IDictionary<string, object> BuildBody(GetSsoSecurityEventsOptions options) =>
		new Dictionary<string, object> { ["providerCode"] = options.Code.Trim() };

}

#endregion

#region Class: EnableSsoSecurityEventsOptions

/// <summary>
///     Options for the <c>enable-sso-security-events</c> command.
/// </summary>
[Verb("enable-sso-security-events",
	HelpText = "Turn security events on for an SSO provider; the card records it as a Creatio support change "
		+ "(SsoSecurityEvents, Creatio 10.2+)")]
[FeatureToggle(SsoSecurityEventsRequirement.FeatureName)]
public class EnableSsoSecurityEventsOptions : SsoSecurityEventsOptions
{

	/// <summary>
	///     Public https address of the site, where the provider delivers the events.
	/// </summary>
	[Option("site-address", Required = false,
		HelpText = "Public https address of the site the provider delivers events to. Without it the stored address "
			+ "is kept, or, when none is stored, the address the call came in on is used")]
	public string SiteAddress { get; set; }

	/// <summary>
	///     SSF discovery URL of the provider.
	/// </summary>
	[Option("ssf-discovery-url", Required = false,
		HelpText = "SSF discovery URL of the provider. Default: <provider URL>/.well-known/ssf-configuration "
			+ "(always used for Creatio ID)")]
	public string SsfDiscoveryUrl { get; set; }

	/// <summary>
	///     Confirms an SSF discovery URL on another host than the provider URL.
	/// </summary>
	[Option("confirm-other-host", Required = false, Default = false,
		HelpText = "Confirm an SSF discovery URL on another host than the provider URL; the site sends the "
			+ "provider's client secret to that host")]
	public bool ConfirmOtherHost { get; set; }

}

#endregion

#region Class: EnableSsoSecurityEventsCommand

/// <summary>
///     Turns security events on for one SSO provider through <c>EnableEvents</c>.
/// </summary>
public class EnableSsoSecurityEventsCommand : SsoSecurityEventsCommand<EnableSsoSecurityEventsOptions>
{

	/// <summary>
	///     Initializes a new instance of the <see cref="EnableSsoSecurityEventsCommand" /> class.
	/// </summary>
	public EnableSsoSecurityEventsCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings, serviceUrlBuilder) { }

	/// <inheritdoc />
	protected override ServiceUrlBuilder.KnownRoute Route => ServiceUrlBuilder.KnownRoute.SsoSecurityEventsEnableEvents;

	/// <inheritdoc />
	protected override IDictionary<string, object> BuildBody(EnableSsoSecurityEventsOptions options) =>
		new Dictionary<string, object> {
			["providerCode"] = options.Code.Trim(),
			["siteAddress"] = NullIfBlank(options.SiteAddress),
			["discoveryUrl"] = NullIfBlank(options.SsfDiscoveryUrl),
			["hostConfirmed"] = options.ConfirmOtherHost
		};

}

#endregion

#region Class: DisableSsoSecurityEventsOptions

/// <summary>
///     Options for the <c>disable-sso-security-events</c> command.
/// </summary>
[Verb("disable-sso-security-events",
	HelpText = "Turn security events off for an SSO provider; the card records it as a Creatio support change "
		+ "(SsoSecurityEvents, Creatio 10.2+)")]
[FeatureToggle(SsoSecurityEventsRequirement.FeatureName)]
public class DisableSsoSecurityEventsOptions : SsoSecurityEventsOptions
{
}

#endregion

#region Class: DisableSsoSecurityEventsCommand

/// <summary>
///     Turns security events off for one SSO provider through <c>DisableEvents</c>.
/// </summary>
public class DisableSsoSecurityEventsCommand : SsoSecurityEventsCommand<DisableSsoSecurityEventsOptions>
{

	/// <summary>
	///     Initializes a new instance of the <see cref="DisableSsoSecurityEventsCommand" /> class.
	/// </summary>
	public DisableSsoSecurityEventsCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings, serviceUrlBuilder) { }

	/// <inheritdoc />
	protected override ServiceUrlBuilder.KnownRoute Route =>
		ServiceUrlBuilder.KnownRoute.SsoSecurityEventsDisableEvents;

	/// <inheritdoc />
	protected override IDictionary<string, object> BuildBody(DisableSsoSecurityEventsOptions options) =>
		new Dictionary<string, object> { ["providerCode"] = options.Code.Trim() };

}

#endregion

#region Class: SendSsoSecurityTestEventOptions

/// <summary>
///     Options for the <c>send-sso-security-test-event</c> command.
/// </summary>
[Verb("send-sso-security-test-event",
	HelpText = "Ask the provider for a security events test event and optionally wait for it (SsoSecurityEvents, "
		+ "Creatio 10.2+)")]
[FeatureToggle(SsoSecurityEventsRequirement.FeatureName)]
public class SendSsoSecurityTestEventOptions : SsoSecurityEventsOptions
{

	/// <summary>
	///     Seconds the site waits for the test event before it answers, 0 to 60.
	/// </summary>
	[Option("wait", Required = false, Default = 0,
		HelpText = "Seconds the site waits for the test event before it answers (0-60). Default: 0, do not wait")]
	public int WaitSeconds { get; set; }

}

#endregion

#region Class: SendSsoSecurityTestEventCommand

/// <summary>
///     Asks the provider for a verification event through <c>SendTestEvent</c>.
/// </summary>
public class SendSsoSecurityTestEventCommand : SsoSecurityEventsCommand<SendSsoSecurityTestEventOptions>
{

	/// <summary>
	///     The longest wait the site accepts, in seconds.
	/// </summary>
	public const int MaxWaitSeconds = 60;

	// The site polls for the result after its own request to the provider, so its answer comes some seconds after
	// the wait: the request must outlive both.
	private const int ResponseMarginSeconds = 30;

	/// <summary>
	///     Initializes a new instance of the <see cref="SendSsoSecurityTestEventCommand" /> class.
	/// </summary>
	public SendSsoSecurityTestEventCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings, serviceUrlBuilder) { }

	/// <inheritdoc />
	protected override ServiceUrlBuilder.KnownRoute Route =>
		ServiceUrlBuilder.KnownRoute.SsoSecurityEventsSendTestEvent;

	/// <inheritdoc />
	protected override string Validate(SendSsoSecurityTestEventOptions options) =>
		options.WaitSeconds is < 0 or > MaxWaitSeconds
			? $"--wait must be between 0 and {MaxWaitSeconds} seconds."
			: null;

	/// <inheritdoc />
	protected override int GetRequestTimeout(SendSsoSecurityTestEventOptions options) =>
		Math.Max(RequestTimeout, (options.WaitSeconds + ResponseMarginSeconds) * 1000);

	/// <inheritdoc />
	protected override IDictionary<string, object> BuildBody(SendSsoSecurityTestEventOptions options) =>
		new Dictionary<string, object> {
			["providerCode"] = options.Code.Trim(),
			["waitSeconds"] = options.WaitSeconds
		};

}

#endregion

#region Class: SetSsoSecurityEventsAddressOptions

/// <summary>
///     Options for the <c>set-sso-security-events-address</c> command.
/// </summary>
[Verb("set-sso-security-events-address",
	HelpText = "Store a new site address for the security events of an SSO provider and optionally move its stream "
		+ "(SsoSecurityEvents, Creatio 10.2+)")]
[FeatureToggle(SsoSecurityEventsRequirement.FeatureName)]
public class SetSsoSecurityEventsAddressOptions : SsoSecurityEventsOptions
{

	/// <summary>
	///     The new public https address of the site.
	/// </summary>
	[Option("site-address", Required = true, HelpText = "New public https address of the site")]
	public string SiteAddress { get; set; }

	/// <summary>
	///     Moves the site's own stream at the provider to the new address.
	/// </summary>
	[Option("move-stream", Required = false, Default = false,
		HelpText = "Also move the site's own stream at the provider to the new address. A stream that delivers to "
			+ "another address is never moved (ForeignAddress)")]
	public bool MoveStream { get; set; }

}

#endregion

#region Class: SetSsoSecurityEventsAddressCommand

/// <summary>
///     Changes the site address of one SSO provider's security events through <c>SetSiteAddress</c>.
/// </summary>
public class SetSsoSecurityEventsAddressCommand : SsoSecurityEventsCommand<SetSsoSecurityEventsAddressOptions>
{

	/// <summary>
	///     Initializes a new instance of the <see cref="SetSsoSecurityEventsAddressCommand" /> class.
	/// </summary>
	public SetSsoSecurityEventsAddressCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings, serviceUrlBuilder) { }

	/// <inheritdoc />
	protected override ServiceUrlBuilder.KnownRoute Route =>
		ServiceUrlBuilder.KnownRoute.SsoSecurityEventsSetSiteAddress;

	/// <inheritdoc />
	protected override string Validate(SetSsoSecurityEventsAddressOptions options) =>
		string.IsNullOrWhiteSpace(options.SiteAddress) ? "--site-address must not be empty." : null;

	/// <inheritdoc />
	protected override IDictionary<string, object> BuildBody(SetSsoSecurityEventsAddressOptions options) =>
		new Dictionary<string, object> {
			["providerCode"] = options.Code.Trim(),
			["siteAddress"] = options.SiteAddress.Trim(),
			["moveStream"] = options.MoveStream
		};

}

#endregion

#region Class: UpdateSsoProviderOptions

/// <summary>
///     Options for the <c>update-sso-provider</c> command.
/// </summary>
[Verb("update-sso-provider",
	HelpText = "Update an SSO provider in place, keeping its row and its security events stream; creates it when "
		+ "no provider has the code (SsoSecurityEvents, Creatio 10.2+)")]
[FeatureToggle(SsoSecurityEventsRequirement.FeatureName)]
public class UpdateSsoProviderOptions : SsoSecurityEventsOptions
{

	/// <summary>
	///     New display name; the stored one is kept when it is not given.
	/// </summary>
	[Option("name", Required = false, HelpText = "Display name. Kept when not given")]
	public string Name { get; set; }

	/// <summary>
	///     New issuer URL; the stored one is kept when it is not given.
	/// </summary>
	[Option("issuer-url", Required = false, HelpText = "OIDC issuer / authority URL. Kept when not given")]
	public string Url { get; set; }

	/// <summary>
	///     OAuth client ID of the provider; the site requires it on every update.
	/// </summary>
	[Option("oidc-client-id", Required = true,
		HelpText = "OAuth client ID registered with the provider. A changed one moves the security events stream "
			+ "to the new client")]
	public string OidcClientId { get; set; }

	/// <summary>
	///     New client secret, as plain text.
	/// </summary>
	[Option("oidc-client-secret", Required = false,
		HelpText = "OAuth client secret. Kept when no secret is given. Prefer --oidc-client-secret-file to avoid "
			+ "shell-history exposure")]
	public string OidcClientSecret { get; set; }

	/// <summary>
	///     Path of a file holding the new client secret.
	/// </summary>
	[Option("oidc-client-secret-file", Required = false,
		HelpText = "Path to a file containing the OIDC client secret. Mutually exclusive with --oidc-client-secret")]
	public string OidcClientSecretFile { get; set; }

	/// <summary>
	///     New OIDC discovery URL; ignored for Creatio ID.
	/// </summary>
	[Option("discovery-url", Required = false,
		HelpText = "OIDC discovery endpoint. Kept when not given; ignored for Creatio ID")]
	public string DiscoveryUrl { get; set; }

	/// <summary>
	///     Marks the provider as Creatio ID, which always has the code <c>identity-v3</c>.
	/// </summary>
	[Option("creatio-id", Required = false, Default = false,
		HelpText = "The provider is Creatio ID; requires --code identity-v3, which is always treated as Creatio ID")]
	public bool IsCreatioId { get; set; }

}

#endregion

#region Class: UpdateSsoProviderCommand

/// <summary>
///     Updates an SSO provider in place through <c>UpsertProvider</c>: same row id, new client or secret, the security
///     events stream reconciled when events are on. Unlike <c>register-sso-provider</c> it never refuses an existing
///     code, and it creates the provider when none has the code (a new one needs a client secret).
/// </summary>
public class UpdateSsoProviderCommand : SsoSecurityEventsCommand<UpdateSsoProviderOptions>
{

	private const string CreatioIdCode = "identity-v3";

	private readonly IFileSystem _fileSystem;

	/// <summary>
	///     Initializes a new instance of the <see cref="UpdateSsoProviderCommand" /> class.
	/// </summary>
	public UpdateSsoProviderCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder, IFileSystem fileSystem)
		: base(applicationClient, settings, serviceUrlBuilder) {
		_fileSystem = fileSystem;
	}

	/// <inheritdoc />
	protected override ServiceUrlBuilder.KnownRoute Route =>
		ServiceUrlBuilder.KnownRoute.SsoSecurityEventsUpsertProvider;

	/// <inheritdoc />
	protected override string Validate(UpdateSsoProviderOptions options) {
		if (string.IsNullOrWhiteSpace(options.OidcClientId)) {
			return "--oidc-client-id must not be empty.";
		}
		if (!string.IsNullOrEmpty(options.OidcClientSecret) && !string.IsNullOrEmpty(options.OidcClientSecretFile)) {
			return "--oidc-client-secret and --oidc-client-secret-file are mutually exclusive.";
		}
		// The site stores Creatio ID under identity-v3 whatever code it is sent, so another code here would update
		// a provider the caller did not name.
		if (options.IsCreatioId && !IsCreatioIdCode(options.Code)) {
			return $"Creatio ID always has the code {CreatioIdCode}; pass --code {CreatioIdCode} with --creatio-id.";
		}
		return null;
	}

	/// <inheritdoc />
	protected override IDictionary<string, object> BuildBody(UpdateSsoProviderOptions options) =>
		new Dictionary<string, object> {
			["providerCode"] = options.Code.Trim(),
			["name"] = NullIfBlank(options.Name),
			["url"] = NullIfBlank(options.Url),
			["clientId"] = options.OidcClientId.Trim(),
			["clientSecret"] = ResolveClientSecret(options),
			["discoveryUrl"] = NullIfBlank(options.DiscoveryUrl),
			["isCreatioId"] = options.IsCreatioId || IsCreatioIdCode(options.Code)
		};

	// The site keeps Creatio ID under identity-v3 and a new provider under that code without the flag would get the
	// Custom OpenID template: a secret rotation that forgot --creatio-id must not create or reshape it that way.
	private static bool IsCreatioIdCode(string code) =>
		string.Equals(code.Trim(), CreatioIdCode, StringComparison.Ordinal);

	/// <summary>
	///     Resolves the client secret: <c>--oidc-client-secret-file</c>, then <c>--oidc-client-secret</c>;
	///     <see langword="null" /> keeps the stored secret.
	/// </summary>
	/// <remarks>
	///     Unlike <c>register-sso-provider</c>, the <c>CLIO_OIDC_CLIENT_SECRET</c> environment variable is not read:
	///     here no secret means "keep the stored one", and a variable left over from registering another provider
	///     would silently replace this provider's secret and break its sign-in.
	/// </remarks>
	private string ResolveClientSecret(UpdateSsoProviderOptions options) {
		if (!string.IsNullOrEmpty(options.OidcClientSecretFile)) {
			if (!_fileSystem.ExistsFile(options.OidcClientSecretFile)) {
				throw new FileNotFoundException(
					$"--oidc-client-secret-file not found: {options.OidcClientSecretFile}");
			}
			string fileSecret = _fileSystem.ReadAllText(options.OidcClientSecretFile).Trim();
			if (string.IsNullOrEmpty(fileSecret)) {
				throw new InvalidOperationException(
					$"--oidc-client-secret-file is empty: {options.OidcClientSecretFile}");
			}
			return fileSecret;
		}
		return string.IsNullOrEmpty(options.OidcClientSecret) ? null : options.OidcClientSecret;
	}

}

#endregion

#region Class: RemoveSsoProviderOptions

/// <summary>
///     Options for the <c>remove-sso-provider</c> command.
/// </summary>
[Verb("remove-sso-provider",
	HelpText = "Remove an SSO provider and its security events stream; --local-only leaves the stream at the "
		+ "provider (a copied site) (SsoSecurityEvents, Creatio 10.2+)")]
[FeatureToggle(SsoSecurityEventsRequirement.FeatureName)]
public class RemoveSsoProviderOptions : SsoSecurityEventsOptions
{

	/// <summary>
	///     Leaves the stream at the provider untouched, for a copy whose stream record belongs to its source.
	/// </summary>
	[Option("local-only", Required = false, Default = false,
		HelpText = "Remove the provider on the site only and call nothing at the provider: on a copied site the "
			+ "stream belongs to the source site")]
	public bool LocalOnly { get; set; }

}

#endregion

#region Class: RemoveSsoProviderCommand

/// <summary>
///     Removes an SSO provider through <c>RemoveProvider</c>.
/// </summary>
public class RemoveSsoProviderCommand : SsoSecurityEventsCommand<RemoveSsoProviderOptions>
{

	/// <summary>
	///     Initializes a new instance of the <see cref="RemoveSsoProviderCommand" /> class.
	/// </summary>
	public RemoveSsoProviderCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings, serviceUrlBuilder) { }

	/// <inheritdoc />
	protected override ServiceUrlBuilder.KnownRoute Route =>
		ServiceUrlBuilder.KnownRoute.SsoSecurityEventsRemoveProvider;

	/// <inheritdoc />
	protected override IDictionary<string, object> BuildBody(RemoveSsoProviderOptions options) =>
		new Dictionary<string, object> {
			["providerCode"] = options.Code.Trim(),
			["localOnly"] = options.LocalOnly
		};

}

#endregion
