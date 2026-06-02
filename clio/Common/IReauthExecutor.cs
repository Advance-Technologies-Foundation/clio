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
	/// <see langword="true"/> for the first result, performs a single login + retry.
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
	/// <returns>The original result if it is not unauthorized; otherwise the result of the retry.</returns>
	T Execute<T>(Func<T> call, Func<T, bool> isUnauthorized);
}
