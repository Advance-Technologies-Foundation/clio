using System;

namespace Clio.Common;

/// <summary>
/// Detects a stale Creatio session (server returned the HTML login page instead of JSON)
/// and performs a single Login + retry. Login() invocations are serialized and deduped
/// across concurrent callers within a short time window so a burst of failing requests
/// triggers at most one authentication round.
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
	private readonly Func<DateTime> _utcNow;
	private readonly TimeSpan _dedupeWindow;
	private readonly object _reauthLock = new();
	private DateTime _lastReauthAt = DateTime.MinValue;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Creates a new <see cref="ReauthExecutor"/>.
	/// </summary>
	/// <param name="login">Callback that re-authenticates the underlying client. Required.</param>
	/// <param name="logger">Optional logger; a single warning is written each time a re-auth is performed.</param>
	/// <param name="utcNow">Optional clock; defaults to <see cref="DateTime.UtcNow"/>. Intended for tests.</param>
	/// <param name="dedupeWindow">Optional dedupe window for concurrent re-auth attempts; defaults to 2 seconds.</param>
	public ReauthExecutor(Action login, ILogger logger = null, Func<DateTime> utcNow = null,
		TimeSpan? dedupeWindow = null) {
		_login = login ?? throw new ArgumentNullException(nameof(login));
		_logger = logger;
		_utcNow = utcNow ?? (() => DateTime.UtcNow);
		_dedupeWindow = dedupeWindow ?? TimeSpan.FromSeconds(2);
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
		TryReauthenticate();
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

	private void TryReauthenticate() {
		bool reauthPerformed = false;
		lock (_reauthLock) {
			DateTime now = _utcNow();
			if (now - _lastReauthAt > _dedupeWindow) {
				_login();
				_lastReauthAt = now;
				reauthPerformed = true;
			}
		}
		if (reauthPerformed) {
			_logger?.WriteWarning("Detected expired Creatio session; re-authenticated and retrying the request.");
		}
	}

	#endregion
}
