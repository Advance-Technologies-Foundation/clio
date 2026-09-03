using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.RegularExpressions;

namespace Clio.Common;

/// <summary>
/// The single answer to "was this failure a rejected credential?".
/// </summary>
/// <remarks>
/// There used to be two classifiers - a typed-status-first one in <c>SysSettingsCommand</c> and a
/// prose-only <c>Contains("401")</c> one in <c>SysSettingsManager</c>'s authentication preflight - and the
/// preflight's ran FIRST, so the corrected command-layer logic never saw the original exception.
/// Everything the preflight wrapped reached the command already misclassified:
/// <c>Connection refused at http://localhost:40124</c> and <c>Correlation id x401y</c> both told the
/// operator to repair valid credentials. The preflight is gone (issue #1371), and its three successors all
/// come back here:
/// <list type="bullet">
/// <item><see cref="Clio.Common.ClassifyingDataProvider"/> - every ATF <c>IDataProvider</c> response and
/// every exception those calls throw.</item>
/// <item><c>SysSettingsManager</c>'s write-path check, which has the RAW response body and therefore uses
/// <see cref="IsAuthenticationFailureResponse"/> rather than the message overload.</item>
/// <item><c>SysSettingsCommand.CategorizeFailure</c> - the command/MCP envelope.</item>
/// </list>
/// One classifier, used by all of them, is what keeps the answers from diverging again.
/// </remarks>
public static class AuthenticationFailureClassifier {

	private const int MaxExceptionUnwrapDepth = 16;

	/// <summary>
	/// Matches 401 only as a standalone token. A substring match turned the port in
	/// <c>http://localhost:40124</c> and the digits inside a correlation id into credential failures, and
	/// alphanumeric neighbours are excluded for the same reason digits are: <c>x401y</c> is an identifier,
	/// not a status. A timeout is supplied because the input is server-controlled prose.
	/// </summary>
	private static readonly Regex UnauthorizedStatusToken =
		new(@"(?<![0-9A-Za-z])401(?![0-9A-Za-z])", RegexOptions.Compiled | RegexOptions.CultureInvariant,
			TimeSpan.FromSeconds(1));

	/// <summary>
	/// Names a transport-security failure rather than a rejected credential.
	/// </summary>
	/// <remarks>
	/// <see cref="AuthenticationException"/> is the framework exception for a TLS handshake as well as
	/// for a credential rejection: a bad server certificate arrives as
	/// <c>HttpRequestException("The SSL connection could not be established")</c> wrapping
	/// <c>AuthenticationException("The remote certificate is invalid")</c>, with no HTTP status at all.
	/// Classifying that as rejected credentials replaced the actionable certificate diagnosis with
	/// "verify the environment credentials", which sends the operator to repair a working login while
	/// the untrusted certificate stays untouched. A timeout is supplied because the input is
	/// server-controlled prose.
	/// </remarks>
	private static readonly Regex TransportSecurityFailure =
		new(@"certificate|\bSSL\b|\bTLS\b|secure channel|handshake|trust relationship",
			RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
			TimeSpan.FromSeconds(1));

	/// <summary>
	/// Newtonsoft's prose for "the body is HTML, not JSON": <c>Unexpected character encountered while
	/// parsing value: &lt;</c>. Anchored on that whole phrase rather than on the presence of a
	/// <c>&lt;</c>, because a legitimate platform error message can contain an angle bracket
	/// (<c>"Column &lt;Name&gt; is required"</c>) and must stay a generic failure.
	/// </summary>
	private static readonly Regex HtmlWhereJsonExpected =
		new(@"Unexpected character encountered while parsing value:\s*<",
			RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
			TimeSpan.FromSeconds(1));

	/// <summary>
	/// Creatio's DataService <c>ErrorCode 5</c>, which is the platform's authentication-rejection code.
	/// It reaches clio in two renderings: ATF's <c>ConvertBatchResponse</c> composes
	/// <c>errorCode + ": " + message</c> so it arrives as a leading <c>5:</c>, and a raw fault envelope
	/// carries it as a JSON property. Both are matched; a bare digit 5 is not.
	/// </summary>
	private static readonly Regex DataServiceAuthenticationErrorCode =
		new(@"(?:^|[^0-9A-Za-z])5:\s|""[Ee]rror[Cc]ode""\s*:\s*""?5""?",
			RegexOptions.Compiled | RegexOptions.CultureInvariant,
			TimeSpan.FromSeconds(1));

	/// <summary>
	/// Returns <see langword="true"/> when the exception represents rejected credentials rather than a
	/// routing, network or server failure.
	/// </summary>
	/// <remarks>
	/// Only 401 is a credential signal. A typed status is authoritative in BOTH directions - a typed 404 or
	/// 500 stops there rather than falling through to the prose match, so a routing failure whose text
	/// happens to mention a standalone 401 is not reported as rejected credentials. Prose is the last
	/// resort, for transports that report the failure no other way.
	/// </remarks>
	/// <param name="exception">The failure to classify.</param>
	public static bool IsAuthenticationFailure(Exception exception) =>
		IsAuthenticationFailure(exception, depth: 0);

	/// <summary>
	/// What a provider-reported error <b>message</b> says about the cause of a failure.
	/// </summary>
	public enum ProviderFailureVerdict {

		/// <summary>Nothing in the message names a credential; report the failure as itself.</summary>
		NotAuthentication,

		/// <summary>The message names a rejected credential outright.</summary>
		Authentication,

		/// <summary>
		/// The message says the body was a non-JSON page where a DataService response was required, and
		/// says nothing more. That is a rejected session OR a proxy/gateway/404 page - the two are
		/// indistinguishable from the message alone, so neither may be claimed.
		/// </summary>
		NonJsonPage
	}

	/// <summary>
	/// Classifies a provider-reported error <b>message</b>. Used where no exception object survives:
	/// ATF.Repository's provider catches everything and hands back only <c>ErrorMessage</c>.
	/// </summary>
	/// <remarks>
	/// The <see cref="ProviderFailureVerdict.NonJsonPage"/> verdict exists because the HTML-where-JSON
	/// signal is NOT sufficient evidence of an authentication failure. Creatio serves its login page with
	/// HTTP 200 when it rejects a credential, so the provider's Newtonsoft deserialization fails and the
	/// only trace reaching clio is the parser's own prose - but an IIS/nginx 404 page, a WAF block and a
	/// gateway error page all produce the byte-identical message. <c>ReauthExecutor.IsSessionExpiredResponse</c>
	/// cannot break the tie either: it inspects the response BODY, and the body is never part of the
	/// message. Only a corroborating marker (a status, or prose naming the credential outcome) earns
	/// <see cref="ProviderFailureVerdict.Authentication"/>; without one the caller must name both causes.
	/// A caller that HAS the raw body should use <see cref="IsAuthenticationFailureResponse"/> instead.
	/// </remarks>
	/// <param name="message">The provider-reported error text. Cap it before calling: it is
	/// server-controlled, and every rule below is a scan over it.</param>
	public static ProviderFailureVerdict ClassifyProviderErrorMessage(string message) {
		if (string.IsNullOrWhiteSpace(message)) {
			return ProviderFailureVerdict.NotAuthentication;
		}
		if (TransportSecurityFailure.IsMatch(message)) {
			return ProviderFailureVerdict.NotAuthentication;
		}
		bool namesACredential = message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
			|| UnauthorizedStatusToken.IsMatch(message)
			|| message.Contains("password has expired", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("authentication failed", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("authentication error", StringComparison.OrdinalIgnoreCase)
			|| DataServiceAuthenticationErrorCode.IsMatch(message);
		if (namesACredential) {
			return ProviderFailureVerdict.Authentication;
		}
		return HtmlWhereJsonExpected.IsMatch(message)
			? ProviderFailureVerdict.NonJsonPage
			: ProviderFailureVerdict.NotAuthentication;
	}

	/// <summary>
	/// The fixed local diagnostics a recognized authentication rejection maps to. Server prose never
	/// reaches a caller-visible field, so these are the only sentences an operator or an agent reads.
	/// </summary>
	/// <remarks>
	/// Issue #1333. A DataService <c>ErrorCode:5</c> envelope, a login page and a proxy page are all
	/// server-authored text: they can carry a bearer token, a user's e-mail, bidi controls that reorder the
	/// line, or a sentence shaped like an instruction to an agent. Stripping control characters does not
	/// change any of that - the text still lands in the CLI output, in the log and in an MCP envelope that
	/// an AI agent reads as part of its own context. So the recognized causes are mapped to fixed
	/// sentences here, and the raw excerpt survives only on a debug-verbosity log line, found through the
	/// correlation ID.
	/// </remarks>
	public static class FixedAuthenticationDiagnostics {

		/// <summary>The platform said the registered user's password is expired.</summary>
		public const string PasswordExpired = "The password for the registered user has expired.";

		/// <summary>The environment served its login page where a DataService response was expected.</summary>
		public const string LoginRedirect = "The environment redirected to its login page.";

		/// <summary>A DataService fault envelope with the authentication rejection code.</summary>
		public const string CredentialsRejected = "Creatio rejected the credentials.";

		/// <summary>The rejection is proven but its specific cause is not recognized.</summary>
		public const string UnknownAuthenticationCause =
			"Creatio rejected the credentials and did not name a recognized cause.";
	}

	/// <summary>
	/// Password-expired prose, in the renderings Creatio uses for it.
	/// </summary>
	private static readonly Regex PasswordExpiredCause =
		new(@"password\s+has\s+expired|password\s+is\s+expired|expired\s+password|PasswordExpired",
			RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
			TimeSpan.FromSeconds(1));

	/// <summary>
	/// Login-page markers: the auth-routing paths Creatio redirects to, and the parser's own prose for
	/// "the body was HTML, not JSON".
	/// </summary>
	private static readonly Regex LoginRedirectMarker =
		new(@"/Login/|NuiLogin|SimpleLogin|ClientUnauthorizedRequest|<\s*html",
			RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
			TimeSpan.FromSeconds(1));

	/// <summary>
	/// Maps recognized server text to ONE of the fixed local diagnostics in
	/// <see cref="FixedAuthenticationDiagnostics"/>. The argument is used only to CHOOSE a sentence -
	/// nothing from it is ever copied into the returned text (issue #1333).
	/// </summary>
	/// <param name="serverText">The server-authored message or response body.</param>
	/// <returns>A fixed local diagnostic naming the cause.</returns>
	public static string DescribeAuthenticationCause(string serverText) {
		if (string.IsNullOrWhiteSpace(serverText)) {
			return FixedAuthenticationDiagnostics.UnknownAuthenticationCause;
		}
		if (PasswordExpiredCause.IsMatch(serverText)) {
			return FixedAuthenticationDiagnostics.PasswordExpired;
		}
		if (LoginRedirectMarker.IsMatch(serverText)) {
			return FixedAuthenticationDiagnostics.LoginRedirect;
		}
		if (DataServiceAuthenticationErrorCode.IsMatch(serverText)
			|| serverText.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
			|| UnauthorizedStatusToken.IsMatch(serverText)
			|| serverText.Contains("authentication failed", StringComparison.OrdinalIgnoreCase)
			|| serverText.Contains("authentication error", StringComparison.OrdinalIgnoreCase)) {
			return FixedAuthenticationDiagnostics.CredentialsRejected;
		}
		return FixedAuthenticationDiagnostics.UnknownAuthenticationCause;
	}

	/// <summary>
	/// <see langword="true"/> when a RAW Creatio response body proves the session was rejected.
	/// </summary>
	/// <remarks>
	/// This is the strong form of the question, and the only one that can distinguish a login page from a
	/// gateway error page - it reads the body, which
	/// <see cref="ClassifyProviderErrorMessage"/> never sees. Use it at every site that still holds the
	/// response text: the sys-settings write path posts through <c>IApplicationClient</c> and gets the body
	/// back, so an expired password there is a definite answer rather than an ambiguous one.
	/// Matching is delegated to <see cref="ReauthExecutor.IsSessionExpiredResponse"/> for the login-page and
	/// JSON-401-envelope shapes, plus the DataService fault envelope's <c>ErrorCode 5</c> /
	/// password-expired prose, which the reauth predicate deliberately does not treat as retryable.
	/// </remarks>
	/// <param name="responseBody">The raw response body as the platform returned it.</param>
	public static bool IsAuthenticationFailureResponse(string responseBody) {
		if (string.IsNullOrWhiteSpace(responseBody)) {
			return false;
		}
		if (ReauthExecutor.IsSessionExpiredResponse(responseBody)) {
			return true;
		}
		return ClassifyProviderErrorMessage(responseBody) == ProviderFailureVerdict.Authentication;
	}

	/// <summary>
	/// <see langword="true"/> when the failure carries an authoritative HTTP status. A typed status is
	/// authoritative in BOTH directions, so a caller must not fall back to prose matching when one is
	/// present: a typed 404 whose body happens to mention a standalone 401 is a routing failure, not a
	/// credential one.
	/// </summary>
	/// <param name="exception">The failure to inspect.</param>
	public static bool HasTypedStatus(Exception exception) =>
		exception is HttpRequestException { StatusCode: not null }
			or WebException { Response: HttpWebResponse };

	private static bool IsAuthenticationFailure(Exception exception, int depth) {
		if (exception is null || depth >= MaxExceptionUnwrapDepth) {
			return false;
		}
		// An aggregate is a container, not a fault: its own message is a generic "One or more errors
		// occurred", so matching prose on it proves nothing. Every fault it holds is examined instead - a
		// single credential rejection among several failures is still one.
		if (exception is AggregateException aggregate) {
			return aggregate.InnerExceptions.Any(inner => IsAuthenticationFailure(inner, depth + 1));
		}
		// A typed status is authoritative before anything else is examined, so a 401 keeps its diagnosis
		// even when the prose around it happens to mention a certificate.
		if (exception is HttpRequestException { StatusCode: { } httpStatus }) {
			return httpStatus == HttpStatusCode.Unauthorized;
		}
		if (exception is WebException { Response: HttpWebResponse response }) {
			return response.StatusCode == HttpStatusCode.Unauthorized;
		}
		// TLS is a transport failure, not a credential one. It arrives with no HTTP status to read: as a
		// status-less HttpRequestException wrapping an AuthenticationException, or as a WebException whose
		// TrustFailure/SecureChannelFailure status sits on the exception rather than on a response. Both
		// would otherwise reach the credential arm below.
		if (NamesTransportSecurityFailure(exception, depth)) {
			return false;
		}
		// These types mean the same thing inside a wrapper as at the top level. Without this an aggregate
		// carrying an AuthenticationException was judged by that exception's PROSE - and "credentials were
		// rejected" carries neither the word unauthorized nor a 401 token, so the diagnosis was lost
		// exactly where it had been wrapped.
		if (exception is AuthenticationException or UnauthorizedAccessException) {
			return true;
		}
		string message = exception.Message ?? string.Empty;
		return message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
			|| UnauthorizedStatusToken.IsMatch(message)
			// Platform prose that names the credential outcome without a status code. These carry no
			// false-positive risk of the kind the bare 401 token has, so they stay text matches.
			|| message.Contains("password has expired", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("authentication failed", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("authentication error", StringComparison.OrdinalIgnoreCase)
			|| IsAuthenticationFailure(exception.InnerException, depth + 1);
	}

	/// <summary>
	/// True when this failure - or anything it wraps within the depth bound - names transport security
	/// rather than a credential. The chain is walked because the certificate prose sits on an inner
	/// exception while the outer one only says the connection could not be established.
	/// </summary>
	private static bool NamesTransportSecurityFailure(Exception exception, int depth) {
		for (Exception current = exception;
				current is not null && depth < MaxExceptionUnwrapDepth;
				current = current.InnerException, depth++) {
			if (current is WebException {
					Status: WebExceptionStatus.TrustFailure or WebExceptionStatus.SecureChannelFailure }) {
				return true;
			}
			if (TransportSecurityFailure.IsMatch(current.Message ?? string.Empty)) {
				return true;
			}
		}
		return false;
	}
}
