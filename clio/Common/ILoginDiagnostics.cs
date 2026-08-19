using System;

namespace Clio.Common;

/// <summary>
/// Records the timing and concurrency context of Creatio login attempts, so a rejected login is
/// diagnosable from CI output alone.
/// </summary>
/// <remarks>
/// <para>
/// Motivated by GitHub issue #1106 (clio MCP e2e flakiness): concurrent <c>create-app-section</c>
/// calls intermittently fail with <c>Unauthorized &lt;user&gt; for &lt;url&gt;</c>, which
/// <c>Creatio.Client.CreatioClient.Login()</c> throws when the login response body contains
/// <c>"Code":1</c>. The message alone cannot tell whether several logins for the same credentials
/// overlapped (every tool call builds its own <see cref="IApplicationClient"/>, so logins are not
/// de-duplicated across calls) or whether a single isolated login was rejected. The recorded
/// in-flight figures answer exactly that question on the next reproduction.
/// </para>
/// <para>
/// Both entry points must be wired, because a login can start in two very different places:
/// <see cref="Track"/> covers the logins clio drives itself (an explicit
/// <see cref="IApplicationClient.Login"/> and the <see cref="IReauthExecutor"/> re-login), while
/// <see cref="TrackRequest{T}"/> covers the login the NuGet client performs implicitly inside the
/// first request of a client that has no auth cookie yet — the path clio never sees as a call.
/// </para>
/// </remarks>
internal interface ILoginDiagnostics {
	/// <summary>
	/// Invokes <paramref name="login"/>. On success the call is transparent. On failure the original
	/// message is re-thrown as <see cref="CreatioLoginFailedException"/> with the diagnostic context
	/// appended.
	/// </summary>
	/// <param name="login">The login callback. Required.</param>
	/// <param name="kind">Why this login is being attempted.</param>
	/// <exception cref="ArgumentNullException"><paramref name="login"/> is <c>null</c>.</exception>
	/// <exception cref="CreatioLoginFailedException"><paramref name="login"/> threw.</exception>
	void Track(Action login, LoginAttemptKind kind);

	/// <summary>
	/// Invokes <paramref name="request"/> and returns its result unchanged. Only a failure that carries
	/// the NuGet client's login-rejection signature is decorated (as
	/// <see cref="LoginAttemptKind.Implicit"/>); every other exception propagates untouched, so this
	/// wrapper never changes how ordinary request failures surface.
	/// </summary>
	/// <typeparam name="T">The request result type.</typeparam>
	/// <param name="request">The request callback. Required.</param>
	/// <returns>Whatever <paramref name="request"/> returned.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
	/// <exception cref="CreatioLoginFailedException">
	/// <paramref name="request"/> failed because its implicit login was rejected.
	/// </exception>
	T TrackRequest<T>(Func<T> request);
}
