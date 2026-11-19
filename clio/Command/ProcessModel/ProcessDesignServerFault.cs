using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Reads the bare error string ProcessDesignService answers instead of its result envelope, shared by the three
/// write paths (create, modify in place, save as a new version) so each classifies the same fault the same way.
/// A WCF fault comes back as a JSON string, not as the envelope; its text says what failed, and one kind of it -
/// the request could not be read - tells the caller that NOTHING ran.
/// </summary>
internal static class ProcessDesignServerFault {

	/// <summary>The longest server text quoted to the caller; a longer one is cut and ends with "...".</summary>
	internal const int MaxServerTextLength = 400;

	// The start of the error string WCF returns when the request body does not fit the service contract. Only the
	// .NET Framework host answers this text: on .NET Core the request is bound by the ASP.NET Core input formatter,
	// which fails with a different shape that no sample of has been captured yet. There the caller still gets the
	// generic "could not read the response ... UNKNOWN" message - less specific, but never a false "NOT applied".
	private const string RequestDeserializationFailure =
		"The formatter threw an exception while trying to deserialize the message";

	/// <summary>
	/// The text of a response body that is a bare JSON string, trimmed, redacted as the site's text and capped at
	/// <see cref="MaxServerTextLength"/> characters; <c>null</c> when the body is not a JSON string or is blank.
	/// </summary>
	/// <remarks>
	/// The fault is the site's text, so it takes the site-text redaction here, where it is read: every caller quotes
	/// it into an exception message that becomes the tool's error and its log line, and a stdio log is not redacted
	/// again. The redaction runs BEFORE the cap, on an input clamped to a multiple of it, so a token straddling the cap
	/// is still recognized whole and a page-sized fault cannot time the chain out. The WCF request-deserialization
	/// prefix <see cref="IsUnreadableRequest"/> looks for is plain prose that no rule takes.
	/// </remarks>
	/// <param name="responseBody">The body the server answered.</param>
	internal static string? ReadServerText(string? responseBody) {
		if (string.IsNullOrWhiteSpace(responseBody) || !responseBody.TrimStart().StartsWith('"')) {
			return null;
		}
		try {
			string? text = JsonSerializer.Deserialize<string>(responseBody);
			if (string.IsNullOrWhiteSpace(text)) {
				return null;
			}
			text = SensitiveErrorTextRedactor.RedactSiteText(SensitiveErrorTextRedactor.ClampAtBoundary(
				text.Trim(), MaxServerTextLength * RedactionInputFactor)).Trim();
			if (text.Length == 0) {
				return null;
			}
			return text.Length <= MaxServerTextLength
				? text
				: TextUtilities.TruncateWithoutSplittingSurrogatePair(text, MaxServerTextLength) + "...";
		} catch (JsonException) {
			return null;
		}
	}

	// How much more of the fault than MaxServerTextLength the redaction reads.
	private const int RedactionInputFactor = 8;

	/// <summary>
	/// True when <paramref name="serverText"/> is WCF's request-deserialization failure. WCF answers it when it
	/// cannot read the REQUEST, before any operation runs, so - unlike any other unreadable answer - the outcome
	/// is known: nothing was applied. Recognized on the .NET Framework host only; a .NET Core host's binding
	/// failure is not matched and falls back to the "outcome unknown" message.
	/// </summary>
	/// <param name="serverText">The text returned by <see cref="ReadServerText"/>.</param>
	internal static bool IsUnreadableRequest(string? serverText) =>
		serverText?.StartsWith(RequestDeserializationFailure, StringComparison.Ordinal) == true;

	/// <summary>
	/// The sentence that quotes the server, with a leading space so it appends to a message; empty when there is
	/// no server text.
	/// </summary>
	/// <param name="serverText">The text returned by <see cref="ReadServerText"/>.</param>
	internal static string ServerSaid(string? serverText) =>
		serverText is null ? string.Empty : $" The server said: {serverText}";

	/// <summary>
	/// The server's own sentence on a refused ProcessDesignService write (its <c>errorMessage</c>), redacted as the
	/// site's text; <paramref name="fallback"/> - clio's own sentence, not redacted - when it sent none.
	/// </summary>
	/// <remarks>
	/// Every write path (create, modify in place, save as a new version, activate a version) throws this sentence,
	/// so it becomes the tool's error and the command's log line, and a stdio log is not redacted again.
	/// </remarks>
	/// <param name="errorMessage">The answer's <c>errorMessage</c>.</param>
	/// <param name="fallback">clio's sentence for a refusal without one.</param>
	internal static string SiteMessageOr(string? errorMessage, string fallback) {
		string redacted = SiteText(errorMessage);
		return redacted.Length == 0 ? fallback : redacted;
	}

	/// <summary>
	/// The server's warnings on a refused write, each redacted as the site's text ON ITS OWN, joined after a leading
	/// space; empty when there are none.
	/// </summary>
	/// <remarks>
	/// One by one, not joined first: the site-text cut drops everything after a stack frame, so a frame in the first
	/// warning would otherwise cut every later one - the owed-compile warning among them.
	/// </remarks>
	/// <param name="warnings">The answer's <c>warnings</c>.</param>
	internal static string SiteWarnings(IEnumerable<string>? warnings) {
		string joined = string.Join(" ", (warnings ?? []).Select(SiteText).Where(warning => warning.Length > 0));
		return joined.Length == 0 ? string.Empty : " " + joined;
	}

	/// <summary>
	/// The server's warnings on a SUCCESSFUL write, each redacted as the site's text on its own; <see langword="null"/>
	/// for none, as the answer had it.
	/// </summary>
	/// <remarks>
	/// The write's result carries them to the tool, and every write command logs each one as a warning line, which a
	/// stdio log does not redact again. A warning that redacts to nothing is dropped. A server notice keeps its own
	/// opening words, which is what a caller matching a notice by its start reads.
	/// </remarks>
	/// <param name="warnings">The answer's <c>warnings</c>.</param>
	internal static IReadOnlyList<string>? SiteWarningList(IReadOnlyList<string>? warnings) =>
		warnings?.Select(SiteText).Where(warning => warning.Length > 0).ToList();

	/// <summary>
	/// A schema or version name the server returned on a successful write, redacted as the site's text;
	/// <see langword="null"/> for none, as the answer had it. A code-shaped name (<c>UsrOrderProcess</c>) is left as it
	/// is by the redaction, so the read-back that follows still finds it.
	/// </summary>
	/// <param name="name">The answer's name.</param>
	internal static string? SiteNameOrNull(string? name) => name is null ? null : SiteText(name);

	/// <summary>
	/// A name or a sentence the server wrote into its answer (a version name, an element name), redacted as the
	/// site's text and trimmed; empty for none.
	/// </summary>
	/// <param name="text">The server's text.</param>
	internal static string SiteText(string? text) =>
		string.IsNullOrWhiteSpace(text) ? string.Empty : SensitiveErrorTextRedactor.RedactSiteText(text).Trim();

}
