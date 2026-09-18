using System;

namespace Clio.Common;

/// <summary>
/// Wraps an HTTP-style call and re-authenticates the underlying Creatio client
/// when the response indicates an expired session, before retrying the call once.
/// </summary>
/// <remarks>
/// Introduced so <see cref="CreatioClientAdapter"/> can be unit-tested in isolation
/// from the <see cref="Creatio.Client.CreatioClient"/> NuGet dependency.
/// </remarks>
internal interface IReauthExecutor {

	/// <summary>
	/// Executes <paramref name="call"/>. If <paramref name="isUnauthorized"/> returns
	/// <see langword="true"/> for the first result, re-authenticates and — only when
	/// <paramref name="replayAllowed"/> is <see langword="true"/> — retries the call once.
	/// </summary>
	/// <remarks>
	/// Detection is purely body-based: the predicate inspects the returned value. Transport
	/// exceptions thrown by the underlying call (for example a future 401 surfaced as an
	/// <see cref="System.Net.Http.HttpRequestException"/>) are propagated to the caller
	/// without re-auth. This is intentional for the on-prem .NET Framework Creatio flow that
	/// ENG-90393 targets, where the server replies with an HTML login page (HTTP 200) rather
	/// than failing the request.
	/// </remarks>
	/// <typeparam name="T">Result type returned by the underlying call.</typeparam>
	/// <param name="call">The call to execute, typically a wrapper over an HTTP request.</param>
	/// <param name="isUnauthorized">Predicate that classifies a result as a session-expired response.</param>
	/// <param name="replayAllowed">
	/// Whether the call may be issued a second time after a successful re-login. A call the caller
	/// knows to be a record write - PUT, PATCH, DELETE, and a POST declared as a write - passes
	/// <see langword="false"/>, so a false-positive classification of an already-committed response
	/// cannot commit it twice. Everything the caller cannot classify passes <see langword="true"/>
	/// and keeps the session recovery. There is deliberately no default: every call site has to state
	/// which one it is.
	/// </param>
	/// <returns>
	/// The original result when it is not unauthorized. When it is unauthorized, the result of the
	/// single retry if <paramref name="replayAllowed"/> is <see langword="true"/>, otherwise the
	/// original (unauthorized) result, with the re-login still performed so the next call succeeds.
	/// </returns>
	T Execute<T>(Func<T> call, Func<T, bool> isUnauthorized, bool replayAllowed);
}
