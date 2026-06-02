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

	// Looking past the head of the body is unnecessary for login-page detection and
	// keeps allocations bounded if a caller happens to receive a large legitimate payload.
	private const int LoginPageScanWindow = 4096;

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
		// Capture the login version BEFORE the first call. If another concurrent caller
		// performs Login while we are mid-flight, we observe the version change and skip
		// our own Login (avoiding a redundant authentication round on a parallel burst).
		int versionAtStart = Volatile.Read(ref _loginVersion);
		T result = call();
		if (!isUnauthorized(result)) {
			return result;
		}
		TryReauthenticate(versionAtStart);
		// At most one retry, regardless of the retry's outcome. The caller observes the
		// second response as-is; if it is still the login page (Login failed, or the
		// session was invalidated again between Login and retry) the caller decides.
		return call();
	}

	/// <summary>
	/// Strict, allocation-light check that the body is a Creatio login HTML page rather than
	/// JSON. Intentionally uses only cheap string operations (no regex, no HTML parser) to
	/// avoid ReDoS, XXE and large-input pitfalls. Returns false for empty, null, or anything
	/// that does not start with an HTML tag.
	/// </summary>
	public static bool IsHtmlLoginPage(string body) {
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
		char first = body[start];
		// Early-exit on anything that does not look like an HTML/XML payload.
		// JSON (`{`, `[`, `"`) and plain text never qualify as a login page.
		if (first != '<') {
			return false;
		}
		int scanLength = Math.Min(body.Length - start, LoginPageScanWindow);
		ReadOnlySpan<char> head = body.AsSpan(start, scanLength);
		return ContainsOrdinalIgnoreCase(head, "id=\"LoginEdit\"")
			|| ContainsOrdinalIgnoreCase(head, "name=\"UserName\"")
			|| ContainsOrdinalIgnoreCase(head, "/Login/NuiLogin.aspx")
			|| ContainsOrdinalIgnoreCase(head, "/Login/Login.aspx")
			|| ContainsOrdinalIgnoreCase(head, "<title>Login");
	}

	#endregion

	#region Methods: Private

	private static bool ContainsOrdinalIgnoreCase(ReadOnlySpan<char> haystack, string needle) {
		return haystack.IndexOf(needle.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private void TryReauthenticate(int versionAtStart) {
		bool reauthPerformed = false;
		lock (_reauthLock) {
			// If the version has advanced while we waited on the lock, another caller has
			// already re-authenticated for us; skip our own Login and proceed to retry.
			if (_loginVersion == versionAtStart) {
				_login();
				_loginVersion = unchecked(_loginVersion + 1);
				reauthPerformed = true;
			}
		}
		if (reauthPerformed) {
			_logger?.WriteWarning("Detected expired Creatio session; re-authenticated and retrying the request.");
		}
	}

	#endregion
}
