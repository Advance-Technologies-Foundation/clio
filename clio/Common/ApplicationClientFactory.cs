using System;
using Creatio.Client;

namespace Clio.Common;

// Implementation is internal: every consumer resolves the public IApplicationClientFactory
// interface from DI. Keeping the concrete class internal lets its constructor accept the
// internal IReauthExecutor (the NoReauthExecutor used by the credential-passthrough branch)
// and enforces at compile time that nothing constructs the factory outside DI / the e2e probe.
internal class ApplicationClientFactory : IApplicationClientFactory{
	#region Fields: Private

	private readonly IReauthExecutor _noReauthExecutor;

	#endregion

	#region Constructors: Public

	public ApplicationClientFactory(IReauthExecutor noReauthExecutor) {
		_noReauthExecutor = noReauthExecutor ?? throw new ArgumentNullException(nameof(noReauthExecutor));
	}

	#endregion

	#region Methods: Public

	public IApplicationClient CreateClient(EnvironmentSettings settings) {
		// Credential-passthrough bearer branch (FR-01/FR-18): an ephemeral EnvironmentSettings
		// carrying an opaque access token resolves to a pre-authenticated client that NEVER
		// re-logs-in (NoReauthExecutor). The login/password + OAuth branches below keep the
		// adapter's default internal closure-based ReauthExecutor.
		if (!string.IsNullOrEmpty(settings.AccessToken)) {
			GuardBearerSettings(settings);
			Lazy<CreatioClient> client = new(() =>
				new CreatioClient(settings.Uri, settings.AccessToken, settings.IsNetCore));
			return new CreatioClientAdapter(client, null, _noReauthExecutor, ownsClient: true);
		}

		if (!string.IsNullOrEmpty(settings.Cookie)) {
			throw new NotSupportedException(
				"A raw EnvironmentSettings.Cookie value is not a supported structured Creatio session; " +
				"use an access token or import typed session cookies on a forms-auth client.");
		}

		if (string.IsNullOrEmpty(settings.ClientId)) {
			return new CreatioClientAdapter(settings.Uri, settings.Login, settings.Password,
				settings.IsNetCore);
		}

		return new CreatioClientAdapter(settings.Uri, settings.ClientId,
			settings.ClientSecret, settings.AuthAppUri, settings.IsNetCore);
	}

	public IApplicationClient CreateEnvironmentClient(EnvironmentSettings settings) {
		// Credential-passthrough bearer branch (FR-01/FR-18): see CreateClient. The service-url
		// builder is still wired so environment-relative routes resolve; only the reauth path
		// differs (NoReauthExecutor instead of the default closure-based ReauthExecutor).
		if (!string.IsNullOrEmpty(settings.AccessToken)) {
			GuardBearerSettings(settings);
			Lazy<CreatioClient> client = new(() =>
				new CreatioClient(settings.Uri, settings.AccessToken, settings.IsNetCore));
			return new CreatioClientAdapter(client, new ServiceUrlBuilder(settings), _noReauthExecutor,
				ownsClient: true);
		}

		if (!string.IsNullOrEmpty(settings.Cookie)) {
			throw new NotSupportedException(
				"A raw EnvironmentSettings.Cookie value is not a supported structured Creatio session; " +
				"use an access token or import typed session cookies on a forms-auth client.");
		}

		ServiceUrlBuilder serviceUrlBuilder = new(settings);
		if (string.IsNullOrEmpty(settings.ClientId)) {
			return new CreatioClientAdapter(settings.Uri, settings.Login, settings.Password,
				settings.IsNetCore, serviceUrlBuilder);
		}

		return new CreatioClientAdapter(settings.Uri, settings.ClientId,
			settings.ClientSecret, settings.AuthAppUri, settings.IsNetCore, serviceUrlBuilder);
	}

	/// <inheritdoc />
	public IOwnedApplicationClient CreateFormsEnvironmentClient(EnvironmentSettings settings,
		bool useUntrustedSsl) {
		ArgumentNullException.ThrowIfNull(settings);
		return new CreatioClientAdapter(settings.Uri, settings.Login, settings.Password,
			useUntrustedSsl, settings.IsNetCore, new ServiceUrlBuilder(settings));
	}

	/// <inheritdoc />
	public IOwnedApplicationClient CreateBearerEnvironmentClient(EnvironmentSettings settings,
		string accessToken, bool useUntrustedSsl) {
		ArgumentNullException.ThrowIfNull(settings);
		EnvironmentSettings bearerSettings = new() {
			Uri = settings.Uri,
			IsNetCore = settings.IsNetCore,
			AccessToken = accessToken,
			AccessTokenType = AuthenticationScheme.Bearer
		};
		GuardBearerSettings(bearerSettings);
		Lazy<CreatioClient> client = new(() => new CreatioClient(settings.Uri, accessToken,
			useUntrustedSsl, settings.IsNetCore));
		return new CreatioClientAdapter(client, new ServiceUrlBuilder(settings), _noReauthExecutor,
			ownsClient: true);
	}

	#endregion

	#region Methods: Private

	// Validates the bearer-passthrough settings. Errors are caller-actionable and NEVER echo the
	// secret token value (FR-12): a blank url is named explicitly, and an unsupported token type
	// is reported by type name only.
	private static void GuardBearerSettings(EnvironmentSettings settings) {
		if (string.IsNullOrWhiteSpace(settings.Uri)) {
			throw new ArgumentException(
				"An access token was supplied but the environment url is missing; provide a non-empty url.",
				nameof(settings));
		}

		if (!string.IsNullOrEmpty(settings.AccessTokenType)
			&& !string.Equals(settings.AccessTokenType, AuthenticationScheme.Bearer, StringComparison.OrdinalIgnoreCase)) {
			throw new NotSupportedException(
				$"Access-token type '{settings.AccessTokenType}' is not supported; " +
				"only 'Bearer' is supported in v1.");
		}
	}

	#endregion
}
