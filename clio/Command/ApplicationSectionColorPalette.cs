using System;
using System.Linq;
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
		"#00BFA5"
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
		return Colors.Any(color => string.Equals(color, normalized, StringComparison.Ordinal));
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
