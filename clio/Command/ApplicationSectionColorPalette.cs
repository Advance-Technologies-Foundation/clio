using System;
using System.Text.RegularExpressions;

namespace Clio.Command;

internal static class ApplicationSectionColorPalette {
	internal static readonly string[] Colors = [
		"#A6DE00",
		"#20A959",
		"#22AC14",
		"#FFAC07",
		"#FF8800",
		"#F9307F",
		"#FF602E",
		"#FF4013",
		"#B87CCF",
		"#7848EE",
		"#247EE5",
		"#0058EF",
		"#009DE3",
		"#4F43C2",
		"#08857E",
		"#00BFA5",
		"#BE1B5A",
		"#E00022",
		"#0B6A32",
		"#1566B9",
		"#9641A9",
		"#F86700",
		"#0D2E4E"
	];

	internal static readonly string JoinedColors = string.Join(", ", Colors);

	private static readonly Regex HexColorRegex = new(
		"^#[0-9A-Fa-f]{6}$",
		RegexOptions.None,
		TimeSpan.FromSeconds(5));

	internal static string PickRandom() => Colors[Random.Shared.Next(Colors.Length)];

	internal static bool IsValidFormat(string value) =>
		!string.IsNullOrWhiteSpace(value) && HexColorRegex.IsMatch(value.Trim());

	internal static bool IsInPalette(string value) {
		if (string.IsNullOrWhiteSpace(value)) return false;
		string normalized = value.Trim().ToUpperInvariant();
		foreach (string color in Colors) {
			if (string.Equals(color, normalized, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	internal static void ValidateOrThrow(string value) {
		if (string.IsNullOrWhiteSpace(value)) {
			throw new ArgumentException("icon-background cannot be empty.");
		}

		string trimmed = value.Trim();
		if (!HexColorRegex.IsMatch(trimmed)) {
			throw new ArgumentException(
				$"icon-background '{value}' must use #RRGGBB format (six hex digits).");
		}

		if (!IsInPalette(trimmed)) {
			throw new ArgumentException(
				$"icon-background '{value}' is not a Freedom UI palette color. " +
				$"Use one of: {JoinedColors}.");
		}
	}
}
