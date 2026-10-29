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

	// The start of the error string WCF returns when the request body does not fit the service contract.
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
	/// is known: nothing was applied.
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
