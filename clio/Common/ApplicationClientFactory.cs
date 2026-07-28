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
			return new CreatioClientAdapter(client, _noReauthExecutor);
		}

		if (!string.IsNullOrEmpty(settings.Cookie)) {
			throw new NotSupportedException(
				"Cookie-based authentication is not supported in v1 (no supported CreatioClient " +
				"cookie-injection path); use an access token.");
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
			return new CreatioClientAdapter(client, new ServiceUrlBuilder(settings), _noReauthExecutor);
		}

		if (!string.IsNullOrEmpty(settings.Cookie)) {
			throw new NotSupportedException(
				"Cookie-based authentication is not supported in v1 (no supported CreatioClient " +
				"cookie-injection path); use an access token.");
		}

		ServiceUrlBuilder serviceUrlBuilder = new(settings);
		if (string.IsNullOrEmpty(settings.ClientId)) {
			return new CreatioClientAdapter(settings.Uri, settings.Login, settings.Password,
				settings.IsNetCore, serviceUrlBuilder);
		}

		return new CreatioClientAdapter(settings.Uri, settings.ClientId,
			settings.ClientSecret, settings.AuthAppUri, settings.IsNetCore, serviceUrlBuilder);
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
