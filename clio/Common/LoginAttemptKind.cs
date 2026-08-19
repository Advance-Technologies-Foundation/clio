namespace Clio.Common;

/// <summary>
/// Classifies why a Creatio login was attempted. Recorded by <see cref="ILoginDiagnostics"/> so a
/// failure surfaced in CI shows which of the three login paths was rejected.
/// </summary>
internal enum LoginAttemptKind {
	/// <summary>
	/// An explicit, caller-initiated login through <see cref="IApplicationClient.Login"/>.
	/// </summary>
	Initial,

	/// <summary>
	/// An automatic re-login driven by <see cref="IReauthExecutor"/> after a request observed a
	/// session-expired response.
	/// </summary>
	/// <remarks>
	/// The recorded in-flight request count excludes the request that triggered this re-login:
	/// <c>ReauthExecutor.Execute</c> runs the request to completion (or failure) before re-authenticating,
	/// so that request has already left the gauge. Read <c>in-flight-requests</c> on a reauth record as
	/// "besides the triggering request" — <c>0/0</c> means no <em>other</em> traffic, not no traffic.
	/// </remarks>
	Reauthentication,

	/// <summary>
	/// A login performed implicitly by the NuGet client from inside a request: its
	/// <c>InitAuthCookie</c> authenticates on the first request of a client that has no auth cookie
	/// yet. This is the dominant path in clio's MCP surface, where every tool call builds its own
	/// client and therefore logs in on its first request without anyone calling <c>Login</c>.
	/// </summary>
	Implicit
}
