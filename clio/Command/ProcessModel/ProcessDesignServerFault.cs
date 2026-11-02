using System;
using System.Text.Json;

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
	/// The text of a response body that is a bare JSON string, trimmed and capped at
	/// <see cref="MaxServerTextLength"/> characters; <c>null</c> when the body is not a JSON string or is blank.
	/// </summary>
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
			text = text.Trim();
			return text.Length <= MaxServerTextLength ? text : text[..MaxServerTextLength] + "...";
		} catch (JsonException) {
			return null;
		}
	}

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

}
