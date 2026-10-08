using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Encodings.Web;

namespace Clio.Command.McpServer;

/// <summary>
/// The encoder the text of an MCP tool result is written with (ENG-99970): <see
/// cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>, plus every BMP character of Unicode category
/// Format - bidi controls (U+202A-U+202E, U+2066-U+2069, U+200E/U+200F), zero-width characters
/// (U+200B-U+200D, U+2060-U+2064), the soft hyphen and the like - written as a <c>\uXXXX</c> escape.
/// </summary>
/// <remarks>
/// <para>
/// Relaxed, because the default encoder escapes for embedding in HTML: a quote inside a string became
/// <c>\u0022</c> and every apostrophe, backtick, dash and non-ASCII letter a six-character escape, which a
/// result an agent reads pays for and never needs. describe-business-process, whose graph travels as a
/// string inside the result, was 53-62 thousand characters and was spilled to a file on every call.
/// </para>
/// <para>
/// Format characters stay escaped because they are invisible and reorder or hide text: a caption carrying
/// U+202E shows the agent's transcript - or an approval prompt quoting that caption - something other than
/// what is stored. Creatio data is written by whoever has access to the environment, and the repository
/// already treats the category as hostile (<see cref="Clio.Common.TextUtilities.IsDisplayHostile"/>).
/// The relaxed encoder already escapes control characters, the line and paragraph separators and every
/// astral character (including the Tag block), so this adds only the BMP Format characters, which are
/// rare in real data and cost nothing where they are absent.
/// </para>
/// </remarks>
internal sealed class McpResultJsonEncoder : JavaScriptEncoder {

	/// <summary>The one instance; the encoder holds no state.</summary>
	internal static readonly McpResultJsonEncoder Instance = new();

	private static readonly JavaScriptEncoder Relaxed = UnsafeRelaxedJsonEscaping;

	private const int EscapeLength = 6;

	private const string UnsafeOverrideJustification = "JavaScriptEncoder declares these members abstract over "
		+ "char*, so an encoder cannot exist without them. Both read or write only within the length the "
		+ "framework passes alongside the pointer, and delegate to UnsafeRelaxedJsonEscaping for everything "
		+ "but the BMP Format characters.";

	private McpResultJsonEncoder() {
	}

	/// <inheritdoc />
	public override int MaxOutputCharactersPerInputCharacter =>
		Math.Max(Relaxed.MaxOutputCharactersPerInputCharacter, EscapeLength);

	/// <inheritdoc />
	public override bool WillEncode(int unicodeScalar) =>
		IsBmpFormat(unicodeScalar) || Relaxed.WillEncode(unicodeScalar);

	/// <inheritdoc />
	[SuppressMessage("Security", "S6640:Make sure that using \"unsafe\" is safe here",
		Justification = UnsafeOverrideJustification)]
	public override unsafe int FindFirstCharacterToEncode(char* text, int textLength) {
		ArgumentNullException.ThrowIfNull(text);
		int relaxedIndex = Relaxed.FindFirstCharacterToEncode(text, textLength);
		int limit = relaxedIndex < 0 ? textLength : relaxedIndex;
		for (int index = 0; index < limit; index++) {
			if (IsBmpFormat(text[index])) {
				return index;
			}
		}
		return relaxedIndex;
	}

	/// <inheritdoc />
	[SuppressMessage("Security", "S6640:Make sure that using \"unsafe\" is safe here",
		Justification = UnsafeOverrideJustification)]
	public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength,
		out int numberOfCharactersWritten) {
		ArgumentNullException.ThrowIfNull(buffer);
		if (!IsBmpFormat(unicodeScalar)) {
			return Relaxed.TryEncodeUnicodeScalar(unicodeScalar, buffer, bufferLength, out numberOfCharactersWritten);
		}
		if (bufferLength < EscapeLength) {
			numberOfCharactersWritten = 0;
			return false;
		}
		const string hex = "0123456789ABCDEF";
		buffer[0] = '\\';
		buffer[1] = 'u';
		buffer[2] = hex[(unicodeScalar >> 12) & 0xF];
		buffer[3] = hex[(unicodeScalar >> 8) & 0xF];
		buffer[4] = hex[(unicodeScalar >> 4) & 0xF];
		buffer[5] = hex[unicodeScalar & 0xF];
		numberOfCharactersWritten = EscapeLength;
		return true;
	}

	private static bool IsBmpFormat(int unicodeScalar) =>
		unicodeScalar is >= 0 and <= 0xFFFF
		&& !char.IsSurrogate((char)unicodeScalar)
		&& CharUnicodeInfo.GetUnicodeCategory((char)unicodeScalar) == UnicodeCategory.Format;
}
