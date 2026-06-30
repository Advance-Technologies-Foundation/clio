using System;

namespace Clio.Tests.Extensions;

/// <summary>
/// String helpers for tests that compare serialized text (for example YAML manifests)
/// against committed fixtures.
/// </summary>
public static class StringTestExtensions
{
	#region Methods: Public

	/// <summary>
	/// Normalizes all line endings to <c>\n</c> so that string equality assertions are
	/// independent of the operating system and of the working-tree line-ending state
	/// produced by git (<c>core.autocrlf</c>). The clio YAML serializer emits
	/// <see cref="Environment.NewLine"/> (CRLF on Windows) while committed fixtures are
	/// stored as LF, so comparing raw text is brittle across machines and CI runners.
	/// </summary>
	/// <param name="value">The text to normalize. May be <see langword="null"/>.</param>
	/// <returns>The text with every <c>\r\n</c> and lone <c>\r</c> replaced by <c>\n</c>, or <see langword="null"/> when <paramref name="value"/> is <see langword="null"/>.</returns>
	public static string NormalizeLineEndings(this string value) =>
		value?.Replace("\r\n", "\n").Replace("\r", "\n");

	#endregion
}
