using System;

namespace Clio.Common;

/// <summary>
/// A failure that <see cref="ClassifyingDataProvider"/> raised because an ATF response was unsuccessful
/// and nothing in it named a rejected credential.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> so every existing handler keeps working unchanged
/// - in particular <c>SysSettingsCommand.CategorizeFailure</c>'s <c>InvalidOperationException invEx =&gt;
/// invEx.Message</c> arm, which is what surfaces the composed diagnostic.
/// <para>
/// The distinct type exists so a consumer can tell "the data provider failed, and its message is the only
/// diagnosis available" apart from an ordinary <see cref="InvalidOperationException"/> raised by clio's own
/// argument or state checks. <c>SchemaNamePrefixTool</c> needs exactly that distinction: it surfaces this
/// message verbatim, while keeping the deliberately generic "Failed to read SchemaNamePrefix." label for
/// everything else (an unregistered environment name, for instance, must not have its resolver text
/// promoted into the tool's error field).
/// </para>
/// </remarks>
public sealed class DataProviderFailureException : InvalidOperationException, IServerDetailCarrier,
		IConsoleRenderedFailure {

	/// <summary>Creates the failure with a composed diagnostic.</summary>
	/// <param name="message">The diagnostic to surface to the caller.</param>
	/// <param name="serverDetail">
	/// The neutralized excerpt of the server text the diagnostic was derived from. Kept OUT of
	/// <see cref="Exception.Message"/> by issue #1333, and surfaced only at debug verbosity.
	/// </param>
	public DataProviderFailureException(string message, string serverDetail = null) : base(message) =>
		ServerDetail = serverDetail;

	/// <summary>Creates the failure with a composed diagnostic and the underlying fault.</summary>
	/// <param name="message">The diagnostic to surface to the caller.</param>
	/// <param name="innerException">The failure the provider reported.</param>
	/// <param name="serverDetail">The neutralized server excerpt, for debug verbosity only.</param>
	public DataProviderFailureException(string message, Exception innerException,
		string serverDetail = null)
		: base(message, innerException) => ServerDetail = serverDetail;

	/// <inheritdoc/>
	public string ServerDetail { get; }

	/// <inheritdoc/>
	/// <remarks>
	/// PR #1374 review. Defaults to <see cref="Exception.Message"/>, so a failure whose diagnosis holds no
	/// server text at all (a fixed local sentence, a non-JSON-page message) has nothing to render twice.
	/// Only the arm composed from platform prose sets it, and it sets it from the SAME raw text the fenced
	/// form came from rather than by unwrapping the fence - a forged marker therefore has no second place
	/// where it could be mistaken for clio's own framing.
	/// </remarks>
	public string ConsoleMessage {
		get => _consoleMessage ?? Message;
		init => _consoleMessage = value;
	}

	private readonly string _consoleMessage;
}
