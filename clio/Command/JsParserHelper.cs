namespace Clio.Command;

/// <summary>
/// Shared JavaScript lexer primitives reused by schema validation services.
/// </summary>
internal static class JsParserHelper
{

	/// <summary>Returns <see langword="true"/> when <paramref name="c"/> can start a JS identifier.</summary>
	internal static bool IsIdentifierStart(char c) =>
		char.IsLetter(c) || c is '_' or '$';

	/// <summary>Returns <see langword="true"/> when <paramref name="c"/> can continue a JS identifier.</summary>
	internal static bool IsIdentifierPart(char c) =>
		char.IsLetterOrDigit(c) || c is '_' or '$';

	/// <summary>Advances <paramref name="startIndex"/> past any leading whitespace characters.</summary>
	internal static int SkipWhitespace(string content, int startIndex) {
		int index = startIndex;
		while (index < content.Length && char.IsWhiteSpace(content[index])) {
			index++;
		}
		return index;
	}

	/// <summary>
	/// Advances one character within a string literal, handling backslash escapes and the closing quote.
	/// </summary>
	internal static int ConsumeStringLiteralCharacter(
		string content,
		int index,
		ref bool inString,
		char stringChar) {
		if (content[index] == '\\') {
			return index + 1 < content.Length ? index + 2 : index + 1;
		}

		if (content[index] == stringChar) {
			inString = false;
		}

		return index + 1;
	}

	/// <summary>
	/// When currently inside a string literal, advances <paramref name="index"/> by one character position
	/// and returns <see langword="true"/>; otherwise returns <see langword="false"/>.
	/// </summary>
	internal static bool TryConsumeStringLiteralCharacter(
		string content,
		ref int index,
		ref bool inString,
		char stringChar) {
		if (!inString) {
			return false;
		}

		index = ConsumeStringLiteralCharacter(content, index, ref inString, stringChar);
		return true;
	}

	/// <summary>Skips a <c>//</c> line comment and returns the index of the first character after it.</summary>
	internal static int SkipLineComment(string content, int index, int length) {
		index += 2;
		while (index < length && content[index] != '\n' && content[index] != '\r') {
			index++;
		}
		return index;
	}

	/// <summary>
	/// Skips a <c>/* … */</c> block comment and returns the index of the first character after the
	/// closing <c>*/</c>. Returns <paramref name="length"/> when the comment is unterminated.
	/// </summary>
	internal static int SkipBlockComment(string content, int index, int length) {
		index += 2;
		while (index + 1 < length) {
			if (content[index] == '*' && content[index + 1] == '/') {
				return index + 2;
			}
			index++;
		}
		return length;
	}

	/// <summary>
	/// Handles the structural characters <c>"</c>, <c>'</c>, <c>`</c>, <c>{</c>, and <c>}</c>
	/// that affect brace depth or string mode; advances <paramref name="index"/> and returns
	/// <see langword="true"/> when a character was consumed.
	/// </summary>
	internal static bool TryHandleStructuralCharacter(
		char current,
		ref int index,
		ref int depth,
		ref bool inString,
		ref char stringChar) {
		if (current is '"' or '\'' or '`') {
			inString = true;
			stringChar = current;
			index++;
			return true;
		}

		if (current == '{') {
			depth++;
			index++;
			return true;
		}

		if (current == '}') {
			depth--;
			index++;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Attempts to read a quoted (string-literal) property name followed by a colon.
	/// </summary>
	/// <param name="content">Source text.</param>
	/// <param name="startIndex">Index of the opening quote character.</param>
	/// <param name="quote">The quote character (<c>"</c>, <c>'</c>, or <c>`</c>).</param>
	/// <param name="propertyName">Receives the unquoted property name on success.</param>
	/// <param name="nextIndex">Receives the index immediately after the colon on success.</param>
	internal static bool TryReadQuotedPropertyName(
		string content,
		int startIndex,
		char quote,
		out string propertyName,
		out int nextIndex) {
		propertyName = string.Empty;
		nextIndex = startIndex;
		int endIndex = startIndex + 1;
		while (endIndex < content.Length) {
			if (content[endIndex] == '\\') {
				endIndex += endIndex + 1 < content.Length ? 2 : 1;
				continue;
			}

			if (content[endIndex] == quote) {
				break;
			}

			endIndex++;
		}

		if (endIndex >= content.Length || content[endIndex] != quote) {
			return false;
		}

		int colonIndex = SkipWhitespace(content, endIndex + 1);
		if (colonIndex >= content.Length || content[colonIndex] != ':') {
			return false;
		}

		propertyName = content.Substring(startIndex + 1, endIndex - startIndex - 1);
		nextIndex = colonIndex + 1;
		return !string.IsNullOrWhiteSpace(propertyName);
	}

	/// <summary>
	/// Attempts to extract the JavaScript object literal that starts at <paramref name="braceStart"/>,
	/// correctly handling nested braces, string literals, and <c>//</c> and <c>/* */</c> comments.
	/// </summary>
	/// <param name="content">Source text.</param>
	/// <param name="braceStart">Index of the opening <c>{</c> character.</param>
	/// <param name="objectContent">Receives the full object text (from <c>{</c> to <c>}</c> inclusive) on success.</param>
	/// <param name="nextIndex">Receives the index immediately after the closing <c>}</c> on success.</param>
	internal static bool TryExtractBalancedJavaScriptObject(
		string content,
		int braceStart,
		out string objectContent,
		out int nextIndex) {
		int depth = 0;
		bool inString = false;
		char stringChar = '"';
		int index = braceStart;
		while (index < content.Length) {
			if (inString) {
				index = ConsumeStringLiteralCharacter(content, index, ref inString, stringChar);
				continue;
			}

			if (content[index] == '/' && index + 1 < content.Length) {
				if (content[index + 1] == '/') {
					index = SkipLineComment(content, index, content.Length);
					continue;
				}
				if (content[index + 1] == '*') {
					index = SkipBlockComment(content, index, content.Length);
					continue;
				}
			}

			char current = content[index];
			if (current is '"' or '\'' or '`') {
				inString = true;
				stringChar = current;
				index++;
				continue;
			}

			if (current == '{') {
				depth++;
			} else if (current == '}') {
				depth--;
				if (depth == 0) {
					objectContent = content.Substring(braceStart, index - braceStart + 1);
					nextIndex = index + 1;
					return true;
				}
			}

			index++;
		}

		objectContent = string.Empty;
		nextIndex = braceStart;
		return false;
	}

}
