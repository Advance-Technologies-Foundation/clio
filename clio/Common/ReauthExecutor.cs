using System;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

	#region Properties: Internal

	/// <summary>
	/// Current login-generation counter. Test seam used to pin the gate invariant that a
	/// failed <c>_login()</c> must NOT advance the version — otherwise a follow-up caller
	/// would observe a bumped version and skip its own Login even though the session was
	/// never actually refreshed. Read with <see cref="Volatile.Read{T}(ref T)"/> so a
	/// reader on a weak memory model (ARM/AArch64) sees the latest publish from
	/// <see cref="TryReauthenticate"/>.
	/// </summary>
	internal int LoginVersion => Volatile.Read(ref _loginVersion);

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
	/// Strict, allocation-light check that the body indicates a Creatio session-expired
	/// response. Detection runs through two arms keyed off the response body's leading
	/// character, both verified empirically on the on-prem .NET Framework surface:
	/// <list type="bullet">
	/// <item><b>HTML arm</b> — body starts with <c>&lt;</c> (rendered login page or 302
	/// redirect body). Classified as expired session when the HTML body references
	/// Creatio's auth-routing namespace: the literal <c>/Login/</c> (in the .NET FW
	/// rendered login form action AND in every "Object moved" redirect target, FW
	/// <c>…/Login/NuiLogin.aspx</c> + Core <c>…/Login/Login.html</c>), OR the literal
	/// <c>"bootstrap.login"</c> (the auto-followed .NET Core login shell, which has no
	/// <c>/Login/</c> literal in its body because the form is rendered client-side).</item>
	/// <item><b>JSON arm</b> — body starts with <c>{</c>. ServiceModel <c>.svc</c> endpoints
	/// (e.g. <c>WorkspaceExplorerService.svc/Build</c>, <c>AppInstallerService.svc/ClearRedisDb</c>,
	/// <c>PackageInstallerService.svc/GetZipPackages</c>) return a JSON 401 fault envelope
	/// <c>{"Message":"Authentication failed.","StackTrace":null,"ExceptionType":"..."}</c>
	/// on an expired Forms-auth cookie. A cheap substring pre-filter skips JSON parsing
	/// for the overwhelming majority of payloads that do not mention auth; matched ones
	/// are parsed once and the top-level <c>Message</c> field is compared by <i>equality</i>
	/// to <c>"Authentication failed."</c> — substring matching here would re-run a
	/// non-idempotent write whose response merely contains the phrase.</item>
	/// </list>
	/// Keying off the platform's auth-routing tokens (rather than login-page DOM such as
	/// form input IDs, page titles, "Object moved" alone) keeps the HTML arm stable across
	/// login-page redesigns and ignores generic 5xx / IIS / WAF error HTML (those surface
	/// to the caller unchanged — re-authentication could not fix them). JSON arrays
	/// (<c>[</c>) and plain-text bodies always return false.
	/// </summary>
	/// <remarks>
	/// Detection is body-based. The NuGet creatio.client auto-follows 302/307 redirects on
	/// .NET Core, so the HTML body it returns is the rendered <c>Login.html</c> shell
	/// rather than an empty redirect envelope. Truly empty redirect bodies (a hypothetical
	/// client configured with <c>AllowAutoRedirect = false</c>) are out of scope and would
	/// surface as-is, mirroring the original (pre-fix) behavior. Full markup-independence
	/// would require keying off the HTTP status / final <c>ResponseUri</c>, which the
	/// NuGet client does not expose — worth a follow-up if that surface is ever added.
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
		char first = body[start];
		if (first == '<') {
			return IsHtmlAuthRedirect(body, start);
		}
		if (first == '{') {
			return IsJsonAuthFailureEnvelope(body);
		}
		// JSON arrays, quoted strings, and plain-text bodies cannot be a session-expired
		// response — they are never produced by Creatio's auth-rejection path.
		return false;
	}

	#endregion

	#region Methods: Private

	private static bool ContainsOrdinalIgnoreCase(ReadOnlySpan<char> haystack, string needle) {
		return haystack.IndexOf(needle.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsHtmlAuthRedirect(string body, int start) {
		int scanLength = Math.Min(body.Length - start, MaxBodyScanCharacters);
		ReadOnlySpan<char> head = body.AsSpan(start, scanLength);
		// Both tokens survive a login-page DOM redesign; neither appears in a 500
		// stack-trace page, a 502 proxy page, or a WAF block, so genuine server
		// errors are correctly NOT flagged as an expired session.
		return ContainsOrdinalIgnoreCase(head, "/Login/")
			|| ContainsOrdinalIgnoreCase(head, "\"bootstrap.login\"");
	}

	// Cheap pre-filter substring used to skip JSON parsing on the overwhelming majority of
	// JSON bodies that do not mention auth. Present in the 401 envelope and in any other
	// fault that happens to mention authentication.
	private const string AuthFailedSubstring = "Authentication failed";

	// Exact value of the top-level "Message" field in the JSON 401 fault envelope. Matched
	// by structural equality — substring matching would fire on a valid, already-executed
	// write whose response happens to embed the phrase, and re-auth would re-run the write.
	private const string AuthFailedMessage = "Authentication failed.";

	private static bool IsJsonAuthFailureEnvelope(string body) {
		// Two-stage filter: cheap substring first, then a single JObject parse only when
		// the phrase is present. The substring index hit caps at <body.Length so it stays
		// O(N), and the parse path is reserved for the rare body that actually mentions
		// auth — keeping the happy-path cost essentially zero for JSON service responses.
		if (body.IndexOf(AuthFailedSubstring, StringComparison.OrdinalIgnoreCase) < 0) {
			return false;
		}
		try {
			JObject envelope = JObject.Parse(body);
			return string.Equals(
				(string)envelope["Message"],
				AuthFailedMessage,
				StringComparison.OrdinalIgnoreCase);
		} catch (JsonException) {
			return false;
		}
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
