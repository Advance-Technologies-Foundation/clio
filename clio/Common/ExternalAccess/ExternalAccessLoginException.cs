using System;

namespace Clio.Common.ExternalAccess;

/// <summary>
/// Raised when an external-access token cannot be exchanged for a Creatio session, or when a session
/// obtained that way has ended and cannot be renewed.
/// </summary>
/// <remarks>
/// The message never echoes the token: it is a credential for the customer's site.
/// Marked <see cref="IAuthoritativeErrorMessage" />: the message already names the grant condition
/// that was refused and what to do about it, and an inner transport or parser message would replace
/// that with something the caller cannot act on.
/// </remarks>
public sealed class ExternalAccessLoginException : Exception, Clio.Common.IAuthoritativeErrorMessage {
	/// <summary>Initializes the exception with a caller-actionable message.</summary>
	/// <param name="message">What went wrong and what the caller has to do about it.</param>
	public ExternalAccessLoginException(string message)
		: base(message) { }

	/// <summary>Initializes the exception with a message and the transport failure behind it.</summary>
	/// <param name="message">What went wrong and what the caller has to do about it.</param>
	/// <param name="innerException">The underlying failure.</param>
	public ExternalAccessLoginException(string message, Exception innerException)
		: base(message, innerException) { }

	/// <summary>The site refused the token.</summary>
	/// <param name="uri">The target environment url.</param>
	/// <param name="reason">The reason reported by the site, already safe to print.</param>
	/// <returns>A configured exception.</returns>
	public static ExternalAccessLoginException Refused(string uri, string reason) =>
		// The site's own message often ends with a period; trimming it keeps the sentence from reading
		// "... (JWE).. The grant may have expired".
		new($"External access to '{uri}' was refused: {reason?.TrimEnd('.', ' ')}. "
			+ "The grant may have expired, been deactivated, or the token may have been minted for "
			+ "another site; request a fresh token from the access list.");

	/// <summary>The exchange succeeded but no authentication cookie was issued.</summary>
	/// <param name="uri">The target environment url.</param>
	/// <returns>A configured exception.</returns>
	public static ExternalAccessLoginException NoSessionCookie(string uri) =>
		new($"External access to '{uri}' returned success but issued no authentication cookie, "
			+ "so there is no session to work with.");

	/// <summary>The environment could not be reached at all.</summary>
	/// <param name="uri">The target environment url.</param>
	/// <param name="innerException">The transport failure.</param>
	/// <returns>A configured exception.</returns>
	public static ExternalAccessLoginException Connectivity(string uri, Exception innerException) =>
		new($"Could not reach '{uri}' to exchange the external-access token.", innerException);
}
