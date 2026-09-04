using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.Json;
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
/// <item><c>SysSettingsCommand.CategorizeError</c> - the command/MCP envelope.</item>
/// </list>
/// One classifier, used by all of them, is what keeps the answers from diverging again.
/// </remarks>
public static class AuthenticationFailureClassifier {

	private const int MaxExceptionUnwrapDepth = 16;

	// Cap on a server-controlled body before it is scanned. Large enough that a login page's auth-routing
	// markers and a fault envelope's ErrorCode are both well inside it, small enough that no scan here can
	// hit the compiled regexes' 1 s MatchTimeout.
	private const int MaxClassifiedBodyLength = 4096;

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
	/// <remarks>
	/// The composed rendering is ANCHORED at the start of the text, because ATF's
	/// <c>ConvertBatchResponse</c> always places the code first. A floating <c>5:\s</c> preceded by any
	/// non-alphanumeric character matched ordinary provider prose - <c>Msg 1205, Level 13, State 5:
	/// Transaction ... was deadlocked</c>, <c>Unexpected token at line 5: '&lt;'</c> - and because this is
	/// the strongest signal in <see cref="ClassifyProviderErrorMessage"/>, a deadlock or a parser error was
	/// raised as an <see cref="System.Security.Authentication.AuthenticationException"/> telling the
	/// operator to repair working credentials. That is the misdiagnosis class the removed preflight probe
	/// was blamed for, and the decorator puts this predicate on every command rather than on one.
	/// </remarks>
	private static readonly Regex DataServiceAuthenticationErrorCode =
		new(@"^\s*5:\s|""[Ee]rror[Cc]ode""\s*:\s*""?5""?",
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
		// A SUCCESSFUL DataService answer is never a rejected session, and it must not be free-text scanned.
		// The write path reaches here holding whatever the platform returned, including at
		// GetEntityIdByDisplayValue a successful SelectQuery payload full of row data: a lookup display value
		// "Unauthorized users", or a sys-setting whose text mentions "Authentication failed", would otherwise
		// turn a write that LANDED into a hard AuthenticationException telling the operator to repair working
		// credentials - and on a write the caller cannot tell whether it landed.
		if (IsSuccessfulDataServiceResponse(responseBody)) {
			return false;
		}
		// CAPPED before every scan below, which is the precondition ClassifyProviderErrorMessage documents and
		// this method used to break. The body is server-controlled and uncapped: on a full HTML login page or a
		// large payload the compiled regexes' 1 s MatchTimeout turns into a RegexMatchTimeoutException on the
		// write path. The markers all sit in the first bytes of any of these shapes.
		string bounded = responseBody.Length <= MaxClassifiedBodyLength
			? responseBody
			: responseBody[..MaxClassifiedBodyLength];
		if (ReauthExecutor.IsSessionExpiredResponse(bounded)) {
			return true;
		}
		return ClassifyProviderErrorMessage(bounded) == ProviderFailureVerdict.Authentication;
	}

	/// <summary>
	/// <see langword="true"/> ONLY when the body is a JSON object carrying an explicit <c>success: true</c> -
	/// the platform's shape for "the operation went through". A flagless JSON object is not exempt: the
	/// DataService fault envelopes take that shape too.
	/// </summary>
	private static bool IsSuccessfulDataServiceResponse(string responseBody) {
		try {
			using JsonDocument document = JsonDocument.Parse(responseBody);
			if (document.RootElement.ValueKind != JsonValueKind.Object) {
				return false;
			}
			JsonProperty[] successFlag = document.RootElement.EnumerateObject()
				.Where(property => string.Equals(property.Name, "success", StringComparison.OrdinalIgnoreCase))
				.Take(1)
				.ToArray();
			if (successFlag.Length == 1) {
				return successFlag[0].Value.ValueKind == JsonValueKind.True;
			}
			// NO success flag: NOT treated as successful. The platform's own fault envelopes take this shape
			// ({"Message":"Authentication failed.","StackTrace":null}), so exempting a flagless object would
			// silence the very case this predicate exists for. Only an explicit success:true is an exemption.
			return false;
		} catch (JsonException) {
			return false;
		}
	}

	/// <summary>
	/// <see langword="true"/> when the failure carries an authoritative HTTP status. A typed status is
	/// authoritative in BOTH directions, so a caller must not fall back to prose matching when one is
	/// present: a typed 404 whose body happens to mention a standalone 401 is a routing failure, not a
	/// credential one.
	/// </summary>
	/// <remarks>
	/// UNWRAPS the same way <see cref="IsAuthenticationFailure(Exception)"/> does - through
	/// <see cref="AggregateException.InnerExceptions"/> and <see cref="Exception.InnerException"/> up to
	/// <c>MaxExceptionUnwrapDepth</c>. A shallow match made the two predicates disagree about the SAME
	/// object on exactly the wrapping this repository documents as the norm (the Creatio client reaches
	/// transport faults through <c>Task.Result</c>, which wraps them in an <see cref="AggregateException"/>):
	/// <c>IsAuthenticationFailure</c> saw through the wrapper and read the typed status, while this returned
	/// <see langword="false"/>, so the caller went on to prose-match a status it had already been told was
	/// authoritative - which is the fallback the summary above says must not happen.
	/// </remarks>
	/// <param name="exception">The failure to inspect.</param>
	public static bool HasTypedStatus(Exception exception) =>
		HasTypedStatus(exception, depth: 0);

	private static bool HasTypedStatus(Exception exception, int depth) {
		if (exception is null || depth >= MaxExceptionUnwrapDepth) {
			return false;
		}
		if (exception is AggregateException aggregate) {
			return aggregate.InnerExceptions.Any(inner => HasTypedStatus(inner, depth + 1));
		}
		if (exception is HttpRequestException { StatusCode: not null } or WebException { Response: HttpWebResponse }) {
			return true;
		}
		return HasTypedStatus(exception.InnerException, depth + 1);
	}

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
