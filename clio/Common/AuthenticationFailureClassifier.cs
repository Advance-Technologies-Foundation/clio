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
/// prose-only <c>Contains("401")</c> one in <see cref="SysSettingsManager"/> - and the manager's ran
/// FIRST, on the real preflight path, so the corrected command-layer logic never saw the original
/// exception. Everything the manager wrapped as an <see cref="AuthenticationException"/> reached the
/// command already misclassified: <c>Connection refused at http://localhost:40124</c> and
/// <c>Correlation id x401y</c> both told the operator to repair valid credentials. One classifier,
/// used by both layers, is what keeps the two answers from diverging again.
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
