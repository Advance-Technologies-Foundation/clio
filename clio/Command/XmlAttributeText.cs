namespace Clio.Command;

/// <summary>
/// The characters a Creatio schema resource value cannot hold. A localizable value is written into the schema
/// resource file as an XML attribute by <c>XmlWriter.WriteAttributeString</c> with the default
/// <c>CheckCharacters = true</c>, so an XML-invalid character is accepted by the save and throws later, on
/// somebody else's package export (docs/knowledge/platform/a-schema-resource-value-is-an-xml-attribute-with-checkcharacters-on.md).
/// </summary>
internal static class XmlAttributeText {

	/// <summary>
	/// Returns whether a single UTF-16 code unit is outside what an XML attribute can hold, ignoring surrogates
	/// (a surrogate is judged pairwise by <see cref="ContainsUnstorable"/>): the C0/C1 controls except tab, LF
	/// and CR, and the non-characters <c>U+FFFE</c> and <c>U+FFFF</c>, which are not control characters.
	/// </summary>
	/// <param name="character">The code unit to test.</param>
	/// <returns><see langword="true"/> when the character cannot be stored.</returns>
	internal static bool IsUnstorable(char character) =>
		(char.IsControl(character) && character != '\t' && character != '\n' && character != '\r')
			|| character == '\uFFFE' || character == '\uFFFF';

	/// <summary>
	/// Returns whether <paramref name="value"/> carries a character an XML attribute cannot hold: one that
	/// <see cref="IsUnstorable"/> rejects, or a lone surrogate half. A surrogate pair is one legal character.
	/// </summary>
	/// <param name="value">The text to test; <see langword="null"/> is storable.</param>
	/// <returns><see langword="true"/> when the value cannot be stored as it is.</returns>
	internal static bool ContainsUnstorable(string value) {
		if (string.IsNullOrEmpty(value)) {
			return false;
		}
		int index = 0;
		while (index < value.Length) {
			char character = value[index];
			if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1])) {
				index += 2;
				continue;
			}
			if (char.IsSurrogate(character) || IsUnstorable(character)) {
				return true;
			}
			index++;
		}
		return false;
	}
}
