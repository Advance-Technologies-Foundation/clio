namespace Clio.Common;

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
	/// The same cause rendered for a terminal: scrubbed, flattened and capped, with no agent fence.
	/// </summary>
	/// <remarks>
	/// PR #1374 review. <see cref="Cause"/> is the agent rendering and carries the
	/// <c>[untrusted-source-text begin] … [untrusted-source-text end]</c> markers, because an MCP
	/// envelope field is read by a model. A console line has no such reader, and printing the fence there
	/// reads as clio malfunctioning. Every sink therefore picks its own rendering off the same
	/// composition rather than each fencing (or not fencing) for itself.
	/// <para>Never <see langword="null"/>: it falls back to <see cref="NoServerTextCause"/> for the same
	/// reason <see cref="Cause"/> does.</para>
	/// </remarks>
	public string ConsoleCause { get; init; } = NoServerTextCause;

	/// <summary>
	/// Fences and scrubs <paramref name="serverMessage"/> for use as a cause, or reports its absence.
	/// </summary>
	/// <param name="serverMessage">The text the platform reported, as it arrived.</param>
	public static ServerReportedFailureText Describe(string serverMessage) {
		string fenced = UntrustedText.Fenced(serverMessage);
		if (fenced is null) {
			return new ServerReportedFailureText(NoServerTextCause, HasServerText: false) {
				ConsoleCause = NoServerTextCause
			};
		}
		//The console rendering is derived from the SAME raw text, not by unwrapping the fenced one: the
		//fence markers are stripped by a string operation nowhere, so there is no second place where a
		//forged marker could be mistaken for clio's own framing.
		string console = UntrustedText.ForConsole(serverMessage);
		return new ServerReportedFailureText(fenced, HasServerText: true) {
			ConsoleCause = console ?? NoServerTextCause
		};
	}

	/// <summary>Composes the single-line diagnostic for <paramref name="operationLabel"/>.</summary>
	/// <param name="operationLabel">The operation, as it reads inside the message.</param>
	public string ComposeMessage(string operationLabel) => $"Failed {operationLabel}: {Cause}";

	/// <summary>The same single-line diagnostic, rendered for a terminal (no agent fence).</summary>
	/// <param name="operationLabel">The operation, as it reads inside the message.</param>
	public string ComposeConsoleMessage(string operationLabel) => $"Failed {operationLabel}: {ConsoleCause}";
}
