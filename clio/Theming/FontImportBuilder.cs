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
		if (!FontFamilyPattern.IsMatch(trimmed)) {
			throw new ArgumentException($"INVALID_FONT_FAMILY: \"{font.Family}\"", nameof(font));
		}
		string name = WhitespaceRegex.Replace(trimmed, "+");
		List<int> list = weights.Distinct().OrderBy(weight => weight).ToList();
		return list.Count > 0
			? $"{name}:wght@{string.Join(";", list.Select(weight => weight.ToString(CultureInfo.InvariantCulture)))}"
			: name;
	}
}
