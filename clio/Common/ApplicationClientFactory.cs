using System;
using System.Collections.Generic;
using Clio.Common.ExternalAccess;
using Creatio.Client;

namespace Clio.Common;

// Implementation is internal: every consumer resolves the public IApplicationClientFactory
// interface from DI. Keeping the concrete class internal lets its constructor accept the
// internal IReauthExecutor (the NoReauthExecutor used by the credential-passthrough branch)
// and enforces at compile time that nothing constructs the factory outside DI / the e2e probe.
internal class ApplicationClientFactory : IApplicationClientFactory{
	#region Fields: Private

	private readonly IReauthExecutor _noReauthExecutor;
	private readonly IExternalAccessSessionProvider _externalAccessSessionProvider;

	#endregion

	#region Constructors: Public

	public ApplicationClientFactory(IReauthExecutor noReauthExecutor,
		IExternalAccessSessionProvider externalAccessSessionProvider) {
		_noReauthExecutor = noReauthExecutor ?? throw new ArgumentNullException(nameof(noReauthExecutor));
		_externalAccessSessionProvider = externalAccessSessionProvider
			?? throw new ArgumentNullException(nameof(externalAccessSessionProvider));
	}

	#endregion

	#region Methods: Public

	public IApplicationClient CreateClient(EnvironmentSettings settings) {
		if (!string.IsNullOrEmpty(settings.ExternalAccessToken)) {
			return CreateExternalAccessClient(settings, serviceUrlBuilder: null);
		}

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
		if (!string.IsNullOrEmpty(settings.ExternalAccessToken)) {
			return CreateExternalAccessClient(settings, new ServiceUrlBuilder(settings));
		}

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
	public IOwnedApplicationClient CreateFormsEnvironmentClient(EnvironmentSettings environment) {
		ArgumentNullException.ThrowIfNull(environment);
		if (string.IsNullOrWhiteSpace(environment.Login) || string.IsNullOrWhiteSpace(environment.Password)) {
			throw new ArgumentException(
				"Forms authentication requires non-empty login and password values.", nameof(environment));
		}
		return new CreatioClientAdapter(environment.Uri, environment.Login, environment.Password,
			useUntrustedSsl: false, environment.IsNetCore, new ServiceUrlBuilder(environment));
	}

	/// <inheritdoc />
	public IOwnedApplicationClient CreateBearerEnvironmentClient(EnvironmentSettings environment,
		string accessToken) {
		ArgumentNullException.ThrowIfNull(environment);
		EnvironmentSettings bearerSettings = new() {
			Uri = environment.Uri,
			IsNetCore = environment.IsNetCore,
			AccessToken = accessToken,
			AccessTokenType = AuthenticationScheme.Bearer
		};
		GuardBearerSettings(bearerSettings);
		Lazy<CreatioClient> client = new(() => new CreatioClient(environment.Uri, accessToken,
			useUntrustedSsl: false, environment.IsNetCore));
		return new CreatioClientAdapter(client, new ServiceUrlBuilder(environment), _noReauthExecutor,
			ownsClient: true);
	}

	#endregion

	#region Methods: Private

	/// <summary>
	/// Exchanges the environment's external-access token for a Creatio session and returns a client
	/// that runs on it.
	/// </summary>
	/// <remarks>
	/// The exchange happens eagerly rather than inside a <see cref="Lazy{T}" />: it is the whole point
	/// of this branch, and a token that the site refuses has to surface as an authentication error at
	/// the call that asked for a client, not as an unrelated failure on some later request.
	/// The client is built with NO credentials, so creatio.client's authentication handler sees the
	/// imported session cookie, treats the client as already authenticated, and never attempts a login;
	/// <see cref="NoReauthExecutor" /> keeps it that way when the session eventually ends, because
	/// nothing in clio can mint a replacement token.
	/// </remarks>
	/// <param name="settings">Environment carrying the external-access token.</param>
	/// <param name="serviceUrlBuilder">Environment-relative url builder, or <c>null</c>.</param>
	/// <returns>A pre-authenticated client that never re-logs-in.</returns>
	private IApplicationClient CreateExternalAccessClient(EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder) {
		GuardExternalAccessSettings(settings);
		IReadOnlyList<CreatioSessionCookie> session =
			_externalAccessSessionProvider.GetSession(settings, settings.ExternalAccessToken);
		Lazy<CreatioClient> client = new(() => {
			CreatioClient created = new(settings.Uri, userName: null, userPassword: null,
				useUntrustedSsl: false, settings.IsNetCore);
			created.ImportSessionCookies(session);
			return created;
		});
		return new CreatioClientAdapter(client, serviceUrlBuilder, _noReauthExecutor, ownsClient: true);
	}


	// The rule itself lives in ExternalAccessSettingsGuard so that BindingsModule and Program ask the
	// same question; this call stays as a cheap defence-in-depth assert on the programmatic path.
	private static void GuardExternalAccessSettings(EnvironmentSettings settings) =>
		Clio.Common.ExternalAccess.ExternalAccessSettingsGuard.Validate(settings);

	// Validates the bearer-passthrough settings. Errors are caller-actionable and NEVER echo the
	// secret token value (FR-12): a blank url is named explicitly, and an unsupported token type
	// is reported by type name only.
	private static void GuardBearerSettings(EnvironmentSettings settings) {
		if (string.IsNullOrWhiteSpace(settings.AccessToken)) {
			throw new ArgumentException("Bearer authentication requires a non-empty access token.", nameof(settings));
		}
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
