using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Common;

/// <summary>
/// Detects Creatio authentication failures in raw service responses.
/// When a Forms-auth session expires (or the auth cookie is missing/invalid), Creatio rejects
/// a protected service call in one of two observed shapes, both surfaced to the caller as a plain
/// response body — the HTTP status and final URL are hidden by the client:
/// <list type="bullet">
/// <item><description>
/// A JSON 401 fault envelope <c>{"Message":"Authentication failed.","StackTrace":null,"ExceptionType":"..."}</c>
/// returned by <c>*.svc</c> ServiceModel endpoints — verified against .NET Framework targets.
/// </description></item>
/// <item><description>
/// A <c>302</c> redirect to <c>/Login/NuiLogin.aspx</c> that the HTTP stack auto-follows,
/// yielding the login-page HTML (XHTML beginning with <c>&lt;!DOCTYPE</c> and containing
/// <c>NuiLogin</c>) as a 200 body.
/// </description></item>
/// </list>
/// Both shapes are produced <i>before</i> the request reaches the service handler, so a caller that
/// re-authenticates and retries on this signal cannot duplicate a side effect. The NetCore login
/// route may differ; the heuristic targets Forms-auth (.NET Framework) — see the scope note on
/// <c>CreatioClientAdapter.ExecuteWithReauthRetry</c>.
/// </summary>
internal static class CreatioAuthResponseGuard {

	// Cheap pre-filter: present in the 401 envelope and in any other fault that mentions auth.
	private const string AuthFailedSubstring = "Authentication failed";

	// Exact value of the top-level "Message" field in the auth-failure 401 envelope. Matched by
	// equality (NOT substring): the same envelope shape carries every server exception, so a
	// mid-call 500 with a different Message — or a valid payload that merely mentions the phrase —
	// must NOT be treated as an expired session, otherwise re-auth would re-run an executed write.
	private const string AuthFailedMessage = "Authentication failed.";

	// Login-page (HTML) markers, unique to the Creatio Forms-auth login redirect.
	private const string LoginPageMarker = "NuiLogin";
	private const string LoginPathMarker = "/Login/";

	/// <summary>
	/// Returns <c>true</c> when <paramref name="response"/> indicates an expired or missing Creatio
	/// session (the JSON 401 auth-failure envelope or a login-page redirect) rather than a real
	/// service response.
	/// </summary>
	/// <remarks>
	/// A JSON body (first non-whitespace char '{' or '[') is an auth failure only when it is the 401
	/// envelope, identified structurally: the top-level <c>Message</c> must <i>equal</i>
	/// <see cref="AuthFailedMessage"/>. Equality, not substring, is essential on write paths — a
	/// substring match would fire on a valid, already-executed response that merely contains the
	/// phrase and would re-run the write. A cheap substring pre-filter skips JSON parsing for the
	/// overwhelming majority of payloads that never mention auth. A non-JSON body is an auth failure
	/// only when it carries a login marker, so a generic IIS / 5xx HTML error page is left alone
	/// (re-login could not fix it, and the original error should surface unchanged).
	/// </remarks>
	public static bool IsLikelyAuthRedirect(string response) {
		if (string.IsNullOrWhiteSpace(response)) {
			return false;
		}
		ReadOnlySpan<char> trimmed = response.AsSpan().TrimStart();
		if (trimmed.Length == 0) {
			return false;
		}
		char first = trimmed[0];
		if (first == '[') {
			return false;
		}
		if (first == '{') {
			return response.IndexOf(AuthFailedSubstring, StringComparison.OrdinalIgnoreCase) >= 0
				&& IsAuthFailureFaultEnvelope(response);
		}
		return response.IndexOf(LoginPageMarker, StringComparison.OrdinalIgnoreCase) >= 0
			|| response.IndexOf(LoginPathMarker, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsAuthFailureFaultEnvelope(string response) {
		try {
			JObject envelope = JObject.Parse(response);
			return string.Equals((string)envelope["Message"], AuthFailedMessage, StringComparison.OrdinalIgnoreCase);
		} catch (JsonException) {
			return false;
		}
	}
}
