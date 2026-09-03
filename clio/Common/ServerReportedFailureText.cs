namespace Clio.Common;

using Clio.Command.McpServer;

/// <summary>
/// The one composition of a diagnostic from text the SERVER reported, shared by every site that has to
/// surface such text because no fixed local sentence can replace it.
/// </summary>
/// <remarks>
/// Issue #1333 allows exactly one kind of server text through to a caller-visible field: the platform's
/// own validation prose on an unsuccessful response ("Column 'Name' is required"), because dropping it
/// destroys the diagnosis. Two places produce that diagnostic - <see cref="ClassifyingDataProvider"/> for
/// a <c>Success == false</c> ATF response, and <c>SysSettingsCommand</c> for a <c>success:false</c>
/// DataService response - and when each fenced and worded it for itself, the two drifted: different
/// fallback sentences, and only one of them naming the operation. This type is that single answer.
/// </remarks>
/// <param name="Cause">The fenced, scrubbed cause, or the local no-text sentence.</param>
/// <param name="HasServerText">
/// Whether the server actually supplied text. A FLAG, not a string comparison: comparing the composed
/// cause against the local sentence let a payload that reproduced that sentence pass itself off as
/// clio's own prose.
/// </param>
public sealed record ServerReportedFailureText(string Cause, bool HasServerText) {

	/// <summary>
	/// What is reported when the response carried no text at all. <c>ConvertBatchResponse</c> sets
	/// <c>ErrorMessage</c> to <see cref="string.Empty"/> when the batch carries no <c>ResponseStatus</c>,
	/// and <c>new ExecuteResponse()</c> leaves it <see langword="null"/>, so without this the message
	/// would end at a bare colon and name no cause.
	/// </summary>
	public const string NoServerTextCause =
		"the environment reported an unsuccessful response without an error message.";

	/// <summary>
	/// Fences and scrubs <paramref name="serverMessage"/> for use as a cause, or reports its absence.
	/// </summary>
	/// <param name="serverMessage">The text the platform reported, as it arrived.</param>
	public static ServerReportedFailureText Describe(string serverMessage) {
		string fenced = SensitiveErrorTextRedactor.RedactUntrustedOrNull(serverMessage);
		return fenced is null
			? new ServerReportedFailureText(NoServerTextCause, HasServerText: false)
			: new ServerReportedFailureText(fenced, HasServerText: true);
	}

	/// <summary>Composes the single-line diagnostic for <paramref name="operationLabel"/>.</summary>
	/// <param name="operationLabel">The operation, as it reads inside the message.</param>
	public string ComposeMessage(string operationLabel) => $"Failed {operationLabel}: {Cause}";
}
