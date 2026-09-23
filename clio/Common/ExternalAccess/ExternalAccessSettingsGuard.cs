using System;

namespace Clio.Common.ExternalAccess;

/// <summary>
/// The single rule about what an external-access token may be combined with.
/// </summary>
/// <remarks>
/// clio builds its Creatio client at three separate sites (the application-client factory, the DI
/// registration in <c>BindingsModule</c>, and the remote-command path in <c>Program</c>). The rule
/// used to live inside the factory, so the same command line was refused on one dispatch path and
/// silently authenticated under the support grant on the other two — which is the wrong-identity
/// failure the rule exists to prevent. It lives here so every site asks the same question.
/// </remarks>
public static class ExternalAccessSettingsGuard {

	/// <summary>
	/// Refuses an external-access token combined with another authentication model.
	/// </summary>
	/// <param name="settings">The resolved environment.</param>
	/// <exception cref="NotSupportedException">
	/// Thrown when the environment carries an external-access token together with an API access token
	/// or an OAuth client. Choosing one silently would connect as an identity the caller did not name.
	/// </exception>
	public static void Validate(EnvironmentSettings settings) {
		if (settings is null || string.IsNullOrEmpty(settings.ExternalAccessToken)) {
			return;
		}
		if (!string.IsNullOrEmpty(settings.AccessToken)) {
			throw new NotSupportedException(
				"An external-access token and an access token cannot be used together: the first is "
				+ "exchanged for a session, the second is sent on every request. Supply exactly one.");
		}
		// A registered environment can carry a stored ClientId, so this also fires when the OAuth
		// client was inherited rather than typed on the command line. That is the point: the two
		// identities are different, and the caller has to say which one they mean.
		if (!string.IsNullOrEmpty(settings.ClientId) || !string.IsNullOrEmpty(settings.ClientSecret)) {
			throw new NotSupportedException(
				"An external-access token and an OAuth client cannot be used together: the first is "
				+ "exchanged for a support-grant session, the second authenticates as the client's own "
				+ "identity. Supply exactly one, or register the environment without --clientId.");
		}
	}
}
