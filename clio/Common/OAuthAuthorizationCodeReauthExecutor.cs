using System;

namespace Clio.Common;

/// <summary>
/// <see cref="IReauthExecutor"/> for an OAuth authorization-code environment: keeps the bearer token the
/// client carries current, so a long-lived client (the <c>clio mcp-server</c> per-session container) keeps
/// working after the access token it was built with expires.
/// </summary>
/// <remarks>
/// <para>
/// Two paths, both ending in a new client built with a new token (the token is fixed for the lifetime of a
/// <see cref="Creatio.Client.CreatioClient"/>, so the client itself has to be replaced):
/// </para>
/// <list type="bullet">
/// <item><b>Before every call</b> the current token is resolved again. Inside its pre-expiry window the
/// OAuth service refreshes it; when the resolved token differs from the one the client was built with, the
/// client is renewed first. This is the path that handles an ordinary expiry, and it also covers a 401 whose
/// body is empty, which the body-based predicate cannot recognise.</item>
/// <item><b>After a call</b> whose response reads as an expired session (the same predicate and replay rules
/// as <see cref="ReauthExecutor"/>), the rejected token is refreshed even though its expiry has not been
/// reached, the client is renewed, and the call is retried once when <c>replayAllowed</c> is
/// <see langword="true"/>.</item>
/// </list>
/// <para>
/// There is never a login/password fallback: the only recovery is the refresh token.
/// </para>
/// </remarks>
internal sealed class OAuthAuthorizationCodeReauthExecutor : IReauthExecutor {
	#region Fields: Private

	private readonly Func<string> _resolveAccessToken;
	private readonly Func<string, string> _refreshAccessToken;
	private readonly Action _renewClient;
	private readonly ReauthExecutor _recovery;
	private readonly object _tokenSync = new();
	private string _clientToken;

	#endregion

	#region Constructors: Public

	/// <summary>Creates a new <see cref="OAuthAuthorizationCodeReauthExecutor"/>.</summary>
	/// <param name="resolveAccessToken">Returns the current access token, refreshing it inside its expiry window.</param>
	/// <param name="refreshAccessToken">Refreshes the access token the server rejected (the argument) and returns the new one.</param>
	/// <param name="renewClient">Makes the next call build a new client, which reads its token from <see cref="AcquireClientToken"/>.</param>
	public OAuthAuthorizationCodeReauthExecutor(Func<string> resolveAccessToken,
		Func<string, string> refreshAccessToken, Action renewClient) {
		_resolveAccessToken = resolveAccessToken ?? throw new ArgumentNullException(nameof(resolveAccessToken));
		_refreshAccessToken = refreshAccessToken ?? throw new ArgumentNullException(nameof(refreshAccessToken));
		_renewClient = renewClient ?? throw new ArgumentNullException(nameof(renewClient));
		// Per-executor state over this executor's own renewal callback, exactly as CreatioClientAdapter builds
		// its default ReauthExecutor; the DI container cannot supply that callback.
#pragma warning disable CLIO001
		_recovery = new ReauthExecutor(RenewWithRefreshedToken);
#pragma warning restore CLIO001
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Returns the access token a new client is built with and records it as the token the client carries.
	/// Called by the client factory, never by request code.
	/// </summary>
	public string AcquireClientToken() {
		lock (_tokenSync) {
			_clientToken = _resolveAccessToken();
			return _clientToken;
		}
	}

	/// <inheritdoc />
	public T Execute<T>(Func<T> call, Func<T, bool> isUnauthorized, bool replayAllowed) {
		RenewIfTokenReplaced();
		return _recovery.Execute(call, isUnauthorized, replayAllowed);
	}

	#endregion

	#region Methods: Private

	private void RenewIfTokenReplaced() {
		lock (_tokenSync) {
			// No client has been built yet (or a renewal is pending): the factory resolves a current token
			// when it builds one, so there is nothing to compare against.
			if (_clientToken is null) {
				return;
			}
			string current = _resolveAccessToken();
			if (string.Equals(current, _clientToken, StringComparison.Ordinal)) {
				return;
			}
			RenewClient();
		}
	}

	private void RenewWithRefreshedToken() {
		lock (_tokenSync) {
			_refreshAccessToken(_clientToken);
			RenewClient();
		}
	}

	private void RenewClient() {
		_clientToken = null;
		_renewClient();
	}

	#endregion
}
