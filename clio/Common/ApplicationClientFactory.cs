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
	private readonly IOAuthAuthorizationCodeService _oauthService;
	private readonly IExternalAccessSessionProvider _externalAccessSessionProvider;

	#endregion

	#region Constructors: Public

	public ApplicationClientFactory(IReauthExecutor noReauthExecutor,
		IExternalAccessSessionProvider externalAccessSessionProvider, IOAuthAuthorizationCodeService oauthService = null) {
		_noReauthExecutor = noReauthExecutor ?? throw new ArgumentNullException(nameof(noReauthExecutor));
		_oauthService = oauthService;
		_externalAccessSessionProvider = externalAccessSessionProvider
			?? throw new ArgumentNullException(nameof(externalAccessSessionProvider));
	}

	#endregion

	#region Methods: Public

	public IApplicationClient CreateClient(EnvironmentSettings settings) {
		if (!string.IsNullOrEmpty(settings.ExternalAccessToken)) {
			return CreateExternalAccessClient(settings, serviceUrlBuilder: null);
		}

		if (settings.AuthFlow == OAuthFlow.AuthorizationCode) {
			return CreateAuthorizationCodeClient(settings);
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

		return CreateOAuthClientCredentialsAdapter(settings, serviceUrlBuilder: null);
	}

	public IApplicationClient CreateEnvironmentClient(EnvironmentSettings settings) {
		if (!string.IsNullOrEmpty(settings.ExternalAccessToken)) {
			return CreateExternalAccessClient(settings, new ServiceUrlBuilder(settings));
		}

		if (settings.AuthFlow == OAuthFlow.AuthorizationCode) {
			return CreateAuthorizationCodeClient(settings);
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

		return CreateOAuthClientCredentialsAdapter(settings, serviceUrlBuilder);
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
		GuardBearerSettings(new EnvironmentSettings {
			Uri = environment.Uri,
			IsNetCore = environment.IsNetCore,
			AccessToken = accessToken,
			AccessTokenType = AuthenticationScheme.Bearer
		});
		Lazy<CreatioClient> client = new(() => CreateBearerClient(environment, accessToken));
		return new CreatioClientAdapter(client, new ServiceUrlBuilder(environment), _noReauthExecutor,
			ownsClient: true);
	}

	#endregion

	#region Methods: Private

	// An authorization-code client outlives its access token whenever the container that holds it does
	// (the mcp-server per-session container stays alive for hours), so it cannot be wired to the
	// NoReauthExecutor: its executor renews the client with a current token before a call and after an
	// expired-session response. The token is still read only when the client is first built, so
	// constructing a client that is never used costs no token-store read and no refresh round-trip.
	private IOwnedApplicationClient CreateAuthorizationCodeClient(EnvironmentSettings environment) {
		CreatioClientTransport transport = null;
		OAuthAuthorizationCodeReauthExecutor executor = new(
			() => ResolveOAuthToken(environment).AccessToken,
			rejectedAccessToken => RefreshOAuthToken(environment, rejectedAccessToken).AccessToken,
			() => transport.Renew());
		transport = new CreatioClientTransport(() => CreateBearerClient(environment, executor.AcquireClientToken()));
		return new CreatioClientAdapter(transport, new ServiceUrlBuilder(environment), executor, ownsClient: true);
	}

	private static CreatioClient CreateBearerClient(EnvironmentSettings environment, string accessToken) {
		GuardBearerSettings(new EnvironmentSettings {
			Uri = environment.Uri,
			IsNetCore = environment.IsNetCore,
			AccessToken = accessToken,
			AccessTokenType = AuthenticationScheme.Bearer
		});
		return new CreatioClient(environment.Uri, accessToken, useUntrustedSsl: false, environment.IsNetCore);
	}

	// An OAuth client-credentials profile (ClientId/ClientSecret) is a token shape: it carries no
	// username/password, so a login-page response must never send it down CreatioClient.Login().
	// This is the rule BindingsModule.UsesTokenAuthentication used to apply at each inline wiring
	// site; now that both sites resolve through this factory, the factory owns it. Wiring the
	// adapter's default closure-based executor here would let an OAuth profile regain the
	// login-capable path (multi-tenant safety, ENG-93208 B1).
	private IApplicationClient CreateOAuthClientCredentialsAdapter(EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder) {
		Lazy<CreatioClient> client = new(() => CreatioClient.CreateOAuth20Client(settings.Uri,
			settings.AuthAppUri, settings.ClientId, settings.ClientSecret, settings.IsNetCore));
		return new CreatioClientAdapter(client, serviceUrlBuilder, _noReauthExecutor, ownsClient: true);
	}

	private OAuthTokenSet ResolveOAuthToken(EnvironmentSettings settings) {
		if (_oauthService is null) {
			throw new InvalidOperationException("OAuth authorization-code service is not registered.");
		}
		return _oauthService.ResolveAsync(settings).GetAwaiter().GetResult();
	}

	private OAuthTokenSet RefreshOAuthToken(EnvironmentSettings settings, string rejectedAccessToken) {
		if (_oauthService is null) {
			throw new InvalidOperationException("OAuth authorization-code service is not registered.");
		}
		return _oauthService.RefreshAsync(settings, rejectedAccessToken).GetAwaiter().GetResult();
	}

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
