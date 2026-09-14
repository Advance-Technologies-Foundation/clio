using System;
using System.Security.Authentication;

namespace Clio.Common;

/// <summary>
/// A proven credential rejection whose message is a FIXED local diagnostic, with the server text it was
/// diagnosed from kept aside for debug verbosity only.
/// </summary>
/// <remarks>
/// Derives from <see cref="AuthenticationException"/> so every existing handler and test keeps working -
/// <c>SysSettingsCommand.CategorizeFailure</c>'s authentication arms, <c>SchemaNamePrefixTool</c>'s
/// authentication catch, and the classifier's own <c>AuthenticationException</c> shortcut all match a
/// subclass. The type exists because <see cref="AuthenticationException"/> has nowhere to put
/// <see cref="ServerDetail"/>, and issue #1333 requires that text to leave the message.
/// </remarks>
public sealed class SessionRejectedException : AuthenticationException, IServerDetailCarrier {

	/// <summary>Creates the failure.</summary>
	/// <param name="message">The fixed local diagnostic to surface.</param>
	/// <param name="serverDetail">The neutralized server excerpt, for debug verbosity only.</param>
	/// <param name="innerException">The underlying fault, when there was one.</param>
	public SessionRejectedException(string message, string serverDetail = null,
		Exception innerException = null)
		: base(message, innerException) => ServerDetail = serverDetail;

	/// <inheritdoc/>
	public string ServerDetail { get; }
}
