namespace Clio.Common;

/// <summary>
/// Bounds the site text a write tool hands to <see cref="SensitiveErrorTextRedactor.RedactSiteText"/> before it
/// previews or quotes it.
/// </summary>
/// <remarks>
/// A preview or quote is redacted BEFORE it is cut to its own cap, so a token that straddles the cap is still
/// recognized whole; the input is clamped first to a multiple of that cap, so a page-sized body cannot time the rule
/// chain out (a timeout fails the whole text closed and loses the reason). The clamp itself could cut a token in two,
/// and the redactor does not recognize a token's head alone (a JSON Web Token needs all three segments) - so the run
/// of token characters the clamp cut into is dropped with it, back to the last whitespace or delimiter.
/// <para>Known edge: text with no whitespace or delimiter before the clamp (one run longer than the clamp) has no
/// safe place to cut and is kept whole; the preview's own cap then shows only its head, as it would show any long
/// run.</para>
/// </remarks>
internal static class SiteTextInput {

	/// <summary>How much more of a site's text than the cap of its preview the redaction reads.</summary>
	internal const int RedactionInputFactor = 8;

	/// <summary>
	/// <paramref name="text"/> cut to at most <paramref name="maxLength"/> characters, never inside a run of token
	/// characters it would cut in two.
	/// </summary>
	/// <param name="text">The site's raw text; <see langword="null"/> is returned as it is.</param>
	/// <param name="maxLength">The most characters to keep.</param>
	/// <returns>The text when it fits; otherwise its head up to the last boundary before the cut.</returns>
	internal static string ClampForRedaction(string text, int maxLength) {
		if (text is null || text.Length <= maxLength) {
			return text;
		}
		string head = TextUtilities.TruncateWithoutSplittingSurrogatePair(text, maxLength);
		if (!IsTokenCharacter(text[head.Length])) {
			return head;
		}
		for (int index = head.Length - 1; index >= 0; index--) {
			if (!IsTokenCharacter(head[index])) {
				return head[..(index + 1)];
			}
		}
		return head;
	}

	// What a key, a token or a base64 run is made of; anything else - whitespace, a quote, a bracket, a separator - is
	// a boundary no token crosses.
	private static bool IsTokenCharacter(char character) =>
		char.IsLetterOrDigit(character) || character is '+' or '/' or '=' or '_' or '-' or '.' or '~' or '%';
}
