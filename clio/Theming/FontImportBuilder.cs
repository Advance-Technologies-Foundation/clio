using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Clio.Theming;

/// <summary>A web-font request: a family name and the weights to load (defaulted when null).</summary>
internal sealed record FontFamilyEntry(string Family, IReadOnlyList<int> Weights = null);

/// <summary>
/// Builds the CSS that loads a theme's web fonts — the Google Fonts CSS2 URL and the <c>@import</c> rule
/// that wraps it. Family names are validated and their spaces joined with <c>+</c>; weights are
/// de-duplicated and sorted ascending.
/// </summary>
internal static class FontImportBuilder {

	internal const int MaxFamilyLength = 100;

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
	private static readonly Regex FontFamilyPattern = new(@"^[A-Za-z0-9][A-Za-z0-9 -]*\z", RegexOptions.Compiled, RegexTimeout);
	private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, RegexTimeout);
	private static readonly int[] DefaultFontWeights = { 400, 500, 600 };

	/// <summary>Builds the Google Fonts CSS2 URL that loads the requested families.</summary>
	internal static string BuildUrl(IReadOnlyList<FontFamilyEntry> fonts) {
		string familyParams = string.Join("&", fonts.Select(font => $"family={BuildFamilyParam(font)}"));
		return $"https://fonts.googleapis.com/css2?{familyParams}&display=swap";
	}

	/// <summary>Builds the CSS <c>@import url('…');</c> rule that loads the requested families.</summary>
	internal static string BuildRule(IReadOnlyList<FontFamilyEntry> fonts) {
		return $"@import url('{BuildUrl(fonts)}');";
	}

	private static string BuildFamilyParam(FontFamilyEntry font) {
		IReadOnlyList<int> weights = font.Weights ?? DefaultFontWeights;
		string trimmed = font.Family.Trim();
		ValidateFamily(trimmed);
		string name = WhitespaceRegex.Replace(trimmed, "+");
		List<int> list = weights.Distinct().OrderBy(weight => weight).ToList();
		return list.Count > 0
			? $"{name}:wght@{string.Join(";", list.Select(weight => weight.ToString(CultureInfo.InvariantCulture)))}"
			: name;
	}

	/// <summary>Throws <see cref="ArgumentException"/> unless <paramref name="family"/> is a valid font family name (grammar + max length 100).</summary>
	internal static void ValidateFamily(string family) {
		if (!IsValidFamily(family)) {
			throw new ArgumentException($"INVALID_FONT_FAMILY: \"{family}\"", nameof(family));
		}
	}

	/// <summary>
	/// Whether <paramref name="family"/> is a well-formed font family name: untrimmed padding rejected,
	/// at most <see cref="MaxFamilyLength"/> characters, and matching the family grammar.
	/// </summary>
	internal static bool IsValidFamily(string family) {
		if (string.IsNullOrEmpty(family)) {
			return false;
		}
		return family == family.Trim()
			&& family.Length <= MaxFamilyLength
			&& FontFamilyPattern.IsMatch(family);
	}

	/// <summary>
	/// Collapses internal whitespace runs to single spaces, so the availability probe, the css2 URL, the
	/// CSS <c>font-family</c> token and any cache key all see one canonical spelling of the family.
	/// </summary>
	internal static string CollapseWhitespace(string family) {
		return WhitespaceRegex.Replace(family, " ");
	}
}
