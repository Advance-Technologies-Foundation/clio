using System;

namespace Clio.Common;

/// <summary>
/// Detects Creatio authentication failures in raw service responses.
/// When a Forms-auth session expires (or the auth cookie is missing/invalid), Creatio rejects
/// a protected service call in one of two observed shapes, both of which the underlying HTTP
/// stack surfaces to the caller as a plain response body — the HTTP status and final URL are
/// hidden by the client, so the body is the only signal available:
/// <list type="bullet">
/// <item><description>
/// A JSON 401 payload <c>{"Message":"Authentication failed.",...}</c> returned by
/// <c>*.svc</c> ServiceModel endpoints — verified against .NET Framework targets with both a
/// missing and an invalid auth cookie.
/// </description></item>
/// <item><description>
/// A <c>302</c> redirect to <c>/Login/NuiLogin.aspx</c> that the HTTP stack auto-follows,
/// yielding the login-page HTML (XHTML beginning with <c>&lt;!DOCTYPE</c> and containing
/// <c>NuiLogin</c>) as a 200 body.
/// </description></item>
/// </list>
/// This guard reports either shape so the caller can re-authenticate and retry. The NetCore
/// login route may differ; the heuristic targets Forms-auth (.NET Framework) — see the scope
/// note on <c>CreatioClientAdapter.ExecuteWithReauthRetry</c>.
/// </summary>
internal static class CreatioAuthResponseGuard {

	// JSON 401 body marker. Canonical Creatio ServiceModel auth-failure message.
	private const string AuthFailedMarker = "Authentication failed";

	// Login-page (HTML) markers, unique to the Creatio Forms-auth login redirect.
	private const string LoginPageMarker = "NuiLogin";
	private const string LoginPathMarker = "/Login/";

	/// <summary>
	/// Returns <c>true</c> when <paramref name="response"/> indicates an expired or missing
	/// Creatio session (JSON 401 auth-failure body or a login-page redirect) rather than a real
	/// service response.
	/// </summary>
	/// <remarks>
	/// The JSON 401 auth-failure body is valid JSON, so it is matched by content first. Otherwise,
	/// every Creatio configuration-service and OData response is JSON: a body whose first
	/// non-whitespace character is '{' or '[' is a real response (zero false positives on the hot
	/// path), while a non-JSON body is treated as an auth redirect only when it carries a login
	/// marker — so a generic IIS / 5xx HTML error page is NOT mistaken for an expired session
	/// (re-login would not fix it, and the original error should surface unchanged).
	/// Known bounded false positive: a payload that legitimately contains the text
	/// "Authentication failed" (e.g. a get-page body carrying such a caption) triggers one extra
	/// re-login; the retried call returns the correct body and cannot loop, so the cost is a single
	/// wasted re-authentication. This is accepted deliberately — a false negative would make the
	/// fix inert, which is worse than one redundant login.
	/// </remarks>
	public static bool IsLikelyAuthRedirect(string response) {
		if (string.IsNullOrWhiteSpace(response)) {
			return false;
		}
		if (response.IndexOf(AuthFailedMarker, StringComparison.OrdinalIgnoreCase) >= 0) {
			return true;
		}
		ReadOnlySpan<char> trimmed = response.AsSpan().TrimStart();
		if (trimmed.Length == 0) {
			return false;
		}
		char first = trimmed[0];
		if (first == '{' || first == '[') {
			return false;
		}
		return response.IndexOf(LoginPageMarker, StringComparison.OrdinalIgnoreCase) >= 0
			|| response.IndexOf(LoginPathMarker, StringComparison.OrdinalIgnoreCase) >= 0;
	}
}
