using System;
using System.Threading;

namespace Clio.Common;

/// <summary>
/// Detects a stale Creatio session (server returned the HTML login page instead of JSON)
/// and performs a single Login + retry per call. Login invocations are serialized across
/// concurrent callers using a monotonically increasing version token, so a parallel burst
/// of failing requests triggers exactly one Login while serial bursts that each observe a
/// fresh expired-session response each get their own Login.
/// </summary>
internal sealed class ReauthExecutor : IReauthExecutor {
	#region Constants: Private

	/// <summary>
	/// Maximum number of characters from the start of the response body that
	/// <see cref="IsSessionExpiredResponse"/> inspects when looking for session-expired
	/// markers. Creatio's login-page markers always live in the HTML head
	/// (<c>&lt;title&gt;</c>, login form inputs, bootstrap loader attribute), so 4096 is
	/// more than enough to catch every variant. Capping the scan also bounds the per-call
	/// CPU and allocations if a caller ever receives a large legitimate HTML payload —
	/// the predicate fails fast instead of walking megabytes of body.
	/// </summary>
	private const int MaxBodyScanCharacters = 4096;

	#endregion

	#region Fields: Private

	private readonly Action _login;
	private readonly ILogger _logger;
	private readonly object _reauthLock = new();
	private int _loginVersion;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Creates a new <see cref="ReauthExecutor"/>.
	/// </summary>
	/// <param name="login">Callback that re-authenticates the underlying client. Required.</param>
	/// <param name="logger">Optional logger; a single warning is written each time a re-auth is performed.</param>
	public ReauthExecutor(Action login, ILogger logger = null) {
		_login = login ?? throw new ArgumentNullException(nameof(login));
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public T Execute<T>(Func<T> call, Func<T, bool> isUnauthorized) {
		if (call is null) {
			throw new ArgumentNullException(nameof(call));
		}
		if (isUnauthorized is null) {
			throw new ArgumentNullException(nameof(isUnauthorized));
		}
		T result = call();
		if (!isUnauthorized(result)) {
			return result;
		}
		// Capture the login version AT THE TIME we observed the failure, not at call-start.
		// For the long-running operations this fix targets, the request itself can span
		// minutes — capturing before the call would make us skip our own Login() on every
		// parallel reauth that completed during that window, even when our own request is
		// the one whose response now needs a fresh session. Reading after the failure
		// narrows the "someone else logged in for me" window to the gap before we acquire
		// the reauth lock — exactly the parallel-burst case the dedupe is designed for.
		int observedVersion = Volatile.Read(ref _loginVersion);
		TryReauthenticate(observedVersion);
		// At most one retry, regardless of the retry's outcome. The caller observes the
		// second response as-is; if it is still the login page (Login failed, or the
		// session was invalidated again between Login and retry) the caller decides.
		return call();
	}

	/// <summary>
	/// Strict, allocation-light check that the body looks like a Creatio session-expired
	/// response rather than JSON. Detection runs through two gates:
	/// <list type="bullet">
	/// <item><b>Gate 1 (shape)</b> — the body is HTML rather than JSON. All wrapped endpoints
	/// return JSON, so an HTML response from one of them already means "something redirected
	/// us"; JSON payloads (first non-whitespace char <c>{</c>, <c>[</c>, <c>"</c>) exit
	/// immediately and cannot be misclassified.</item>
	/// <item><b>Gate 2 (auth route)</b> — the HTML body references Creatio's auth-routing
	/// namespace. Either the literal <c>/Login/</c> appears (in the rendered .NET Framework
	/// login page's form action AND in every "Object moved" 302 redirect target, FW
	/// <c>…/Login/NuiLogin.aspx</c> + Core <c>…/Login/Login.html</c>), or the literal
	/// <c>"bootstrap.login"</c> appears (the auto-followed .NET Core login shell, which has
	/// no <c>/Login/</c> in its body because the form is rendered client-side).</item>
	/// </list>
	/// Keying off the platform's auth-routing tokens rather than login-page DOM (form input
	/// IDs, page titles, "Object moved" alone) keeps detection stable across login-page
	/// redesigns and correctly ignores generic 5xx / IIS / WAF error HTML — those surface
	/// to the caller unchanged because re-authentication could not fix them. Uses only
	/// cheap span-based <see cref="ReadOnlySpan{T}.IndexOf{T}(ReadOnlySpan{T}, System.StringComparison)"/>
	/// calls — no regex, no HTML parser — to avoid ReDoS, XXE and large-input pitfalls.
	/// </summary>
	/// <remarks>
	/// Detection is body-based. The NuGet creatio.client auto-follows 302/307 redirects on
	/// .NET Core, so the body it returns to the caller is the rendered <c>Login.html</c>
	/// shell rather than an empty 302/307 envelope. Truly empty redirect bodies (a
	/// hypothetical client configured with <c>AllowAutoRedirect = false</c>) are out of
	/// scope and would surface to the caller as-is, mirroring the original (pre-fix)
	/// behavior. Full markup-independence would require keying off the HTTP status / final
	/// <c>ResponseUri</c>, which the NuGet client does not expose — worth a follow-up if
	/// that surface is ever added.
	/// </remarks>
	public static bool IsSessionExpiredResponse(string body) {
		if (string.IsNullOrEmpty(body)) {
			return false;
		}
		int start = 0;
		while (start < body.Length && char.IsWhiteSpace(body[start])) {
			start++;
		}
		if (start >= body.Length) {
			return false;
		}
		// Gate 1: anything that does not start with an HTML tag is not a kick-out.
		// JSON (`{`, `[`, `"`) and plain text exit here so the predicate cannot
		// misclassify legitimate service responses regardless of their content.
		if (body[start] != '<') {
			return false;
		}
		int scanLength = Math.Min(body.Length - start, MaxBodyScanCharacters);
		ReadOnlySpan<char> head = body.AsSpan(start, scanLength);
		// Gate 2: the HTML body must reference Creatio's auth-routing namespace.
		// Both tokens survive a login-page DOM redesign; neither appears in a 500
		// stack-trace page, a 502 proxy page, or a WAF block, so genuine server
		// errors are correctly NOT flagged as an expired session.
		return ContainsOrdinalIgnoreCase(head, "/Login/")
			|| ContainsOrdinalIgnoreCase(head, "\"bootstrap.login\"");
	}

	#endregion

	#region Methods: Private

	private static bool ContainsOrdinalIgnoreCase(ReadOnlySpan<char> haystack, string needle) {
		return haystack.IndexOf(needle.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private void TryReauthenticate(int observedVersion) {
		bool reauthPerformed = false;
		lock (_reauthLock) {
			// If the version has advanced while we waited on the lock, another caller has
			// already re-authenticated for us; skip our own Login and proceed to retry.
			if (_loginVersion == observedVersion) {
				_login();
				// Volatile.Write pairs with the Volatile.Read in Execute so the bump is
				// observable on weak memory models (ARM, AArch64) without depending on
				// the lock's release fence to publish it to lock-free readers.
				Volatile.Write(ref _loginVersion, unchecked(_loginVersion + 1));
				reauthPerformed = true;
			}
		}
		if (reauthPerformed) {
			_logger?.WriteWarning("Detected expired Creatio session; re-authenticated and retrying the request.");
		}
	}

	#endregion
}
