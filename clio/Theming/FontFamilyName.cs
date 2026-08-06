using System;
using System.Text.RegularExpressions;

namespace Clio.Theming;

/// <summary>
/// The font family-name contract, owned in one place because three unrelated collaborators depend on it:
/// the availability probe (as its cache key and outbound path segment), the CSS <c>@import</c> URL, and the
/// build's own input validation. Keeping it here means none of them has to depend on another.
/// </summary>
internal static class FontFamilyName {

	internal const int MaxLength = 100;

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
	private static readonly Regex FamilyPattern = new(@"^[A-Za-z0-9][A-Za-z0-9 -]*\z", RegexOptions.Compiled, RegexTimeout);
	private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, RegexTimeout);

	/// <summary>
	/// Canonicalizes <paramref name="family"/> — trimmed, with internal whitespace runs collapsed to single
	/// spaces — so the probe, the css2 URL, the CSS <c>font-family</c> token and any cache key all see one
	/// spelling. Returns <paramref name="family"/> unchanged when it is null or blank.
	/// </summary>
	internal static string Normalize(string family) {
		return string.IsNullOrWhiteSpace(family) ? family : CollapseWhitespace(family.Trim());
	}

	/// <summary>Collapses internal whitespace runs to single spaces without trimming.</summary>
	internal static string CollapseWhitespace(string family) {
		return WhitespaceRegex.Replace(family, " ");
	}

	/// <summary>
	/// Whether <paramref name="family"/> is well formed: untrimmed padding rejected, at most
	/// <see cref="MaxLength"/> characters, and matching the family grammar.
	/// </summary>
	internal static bool IsValid(string family) {
		if (string.IsNullOrEmpty(family)) {
			return false;
		}
		return family == family.Trim()
			&& family.Length <= MaxLength
			&& FamilyPattern.IsMatch(family);
	}

	/// <summary>Throws <see cref="ArgumentException"/> unless <paramref name="family"/> is well formed.</summary>
	internal static void Validate(string family) {
		if (!IsValid(family)) {
			throw new ArgumentException($"INVALID_FONT_FAMILY: \"{family}\"", nameof(family));
		}
	}
}
