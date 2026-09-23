using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Creatio.Client;

namespace Clio.Common.ExternalAccess;

/// <summary>
/// Exchanges a support external-access token for a Creatio session.
/// </summary>
/// <remarks>
/// The token is minted by the grantor site (work.creatio.com) for one <c>ExternalAccess</c> grant and
/// is accepted by exactly one endpoint on the target site — <c>AuthService.svc/OAuthTokenLogin</c>.
/// It is NOT an API bearer token: the target site's <c>OAuthAuthorizationHelper</c> requires an
/// <c>OAuthClientApp</c> row for the token's client id, which this token does not have.
/// See spec/external-access-login/external-access-login-spec.md.
/// </remarks>
public interface IExternalAccessSessionProvider {
	/// <summary>Exchanges the token for the session cookies of <paramref name="environment"/>.</summary>
	/// <param name="environment">The target Creatio environment.</param>
	/// <param name="externalAccessToken">The external-access token, with or without a "Bearer " prefix.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The issued session cookies, always including the authentication cookie.</returns>
	/// <exception cref="ExternalAccessLoginException">
	/// The site refused the token, the feature is off, or the response carried no session cookie.
	/// </exception>
	Task<IReadOnlyList<CreatioSessionCookie>> ExchangeAsync(EnvironmentSettings environment,
		string externalAccessToken, CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns a live Creatio session for <paramref name="environment"/>, reusing the cached one when
	/// it is still alive and exchanging <paramref name="externalAccessToken"/> only when it is not.
	/// </summary>
	/// <remarks>
	/// This is what callers want, not <see cref="Exchange"/>. An external-access token is valid for
	/// 120 seconds while the session it buys lasts as long as the site's session timeout, so
	/// exchanging on every clio invocation would force a fresh token per command. Reusing the cached
	/// session also means the SAME token keeps working for as long as the session does.
	/// </remarks>
	/// <param name="environment">The target Creatio environment.</param>
	/// <param name="externalAccessToken">The external-access token, with or without a "Bearer " prefix.</param>
	/// <returns>The session cookies, always including the authentication cookie.</returns>
	/// <exception cref="ExternalAccessLoginException">
	/// No cached session is usable and the token could not be exchanged.
	/// </exception>
	IReadOnlyList<CreatioSessionCookie> GetSession(EnvironmentSettings environment, string externalAccessToken);

	/// <summary>Synchronous counterpart of <see cref="ExchangeAsync"/>.</summary>
	/// <param name="environment">The target Creatio environment.</param>
	/// <param name="externalAccessToken">The external-access token, with or without a "Bearer " prefix.</param>
	/// <returns>The issued session cookies, always including the authentication cookie.</returns>
	IReadOnlyList<CreatioSessionCookie> Exchange(EnvironmentSettings environment, string externalAccessToken);
}
