using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Clio.Theming;

/// <summary>
/// Normalizes a colour to lowercase <c>#rrggbb</c>. Accepts <c>#RGB</c> / <c>RRGGBB</c> / <c>rgb()</c> /
/// <c>hsl()</c> and CSS named colours, and rejects any form that carries an alpha channel.
/// </summary>
internal static class ColorNormalizer {

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
	private static readonly Regex Hex8Regex = new(@"^#?[0-9a-f]{8}\z", RegexOptions.Compiled, RegexTimeout);
	private static readonly Regex Hex3Regex = new(@"^#?([0-9a-f])([0-9a-f])([0-9a-f])\z", RegexOptions.Compiled, RegexTimeout);
	private static readonly Regex Hex6Regex = new(@"^#?([0-9a-f]{6})\z", RegexOptions.Compiled, RegexTimeout);
	private static readonly Regex RgbRegex = new(@"^rgb\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)\z", RegexOptions.Compiled, RegexTimeout);
	private static readonly Regex HslRegex = new(@"^hsl\(\s*(\d{1,3})\s*,\s*(\d{1,3})%\s*,\s*(\d{1,3})%\s*\)\z", RegexOptions.Compiled, RegexTimeout);

	/// <summary>Normalizes a colour to lowercase <c>#rrggbb</c>.</summary>
	/// <exception cref="ArgumentException">The input is an alpha form (<c>ALPHA_NOT_SUPPORTED</c>) or an
	/// unrecognized/out-of-range value (<c>INVALID_COLOR</c>).</exception>
	internal static string Normalize(string input) {
		if (input == null) {
			throw InvalidColor(input);
		}
		string value = input.Trim().ToLowerInvariant();
		GuardAgainstAlpha(value, input);
		if (CssNamedColors.Map.TryGetValue(value, out string named)) {
			return named;
		}
		if (TryShorthandHex(value, out string fromShorthand)) {
			return fromShorthand;
		}
		if (TryFullHex(value, out string fromHex)) {
			return fromHex;
		}
		if (TryRgb(value, out string fromRgb)) {
			return fromRgb;
		}
		if (TryHsl(value, out string fromHsl)) {
			return fromHsl;
		}
		throw InvalidColor(input);
	}

	private static void GuardAgainstAlpha(string value, string input) {
		if (Hex8Regex.IsMatch(value)
			|| value.StartsWith("rgba", StringComparison.Ordinal)
			|| value.StartsWith("hsla", StringComparison.Ordinal)) {
			throw new ArgumentException($"ALPHA_NOT_SUPPORTED: \"{input}\"", nameof(input));
		}
	}

	private static bool TryShorthandHex(string value, out string hex) {
		Match match = Hex3Regex.Match(value);
		if (!match.Success) {
			hex = null;
			return false;
		}
		string r = match.Groups[1].Value;
		string g = match.Groups[2].Value;
		string b = match.Groups[3].Value;
		hex = $"#{r}{r}{g}{g}{b}{b}";
		return true;
	}

	private static bool TryFullHex(string value, out string hex) {
		Match match = Hex6Regex.Match(value);
		if (!match.Success) {
			hex = null;
			return false;
		}
		hex = "#" + match.Groups[1].Value;
		return true;
	}

	private static bool TryRgb(string value, out string hex) {
		hex = null;
		Match match = RgbRegex.Match(value);
		if (!match.Success) {
			return false;
		}
		int r = ParseInt(match.Groups[1].Value);
		int g = ParseInt(match.Groups[2].Value);
		int b = ParseInt(match.Groups[3].Value);
		if (r > 255 || g > 255 || b > 255) {
			return false;
		}
		hex = ColorSpace.RgbToHex((r / 255.0, g / 255.0, b / 255.0));
		return true;
	}

	private static bool TryHsl(string value, out string hex) {
		hex = null;
		Match match = HslRegex.Match(value);
		if (!match.Success) {
			return false;
		}
		int saturation = ParseInt(match.Groups[2].Value);
		int lightness = ParseInt(match.Groups[3].Value);
		if (saturation > 100 || lightness > 100) {
			return false;
		}
		double h = ParseInt(match.Groups[1].Value) % 360;
		double s = saturation / 100.0;
		double l = lightness / 100.0;
		hex = HslToHex(h, s, l);
		return true;
	}

	private static int ParseInt(string value) {
		return int.Parse(value, CultureInfo.InvariantCulture);
	}

	private static string HslToHex(double h, double s, double l) {
		double c = (1 - Math.Abs(2 * l - 1)) * s;
		double x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
		double m = l - c / 2;
		double r;
		double g;
		double b;
		if (h < 60) {
			(r, g, b) = (c, x, 0);
		} else if (h < 120) {
			(r, g, b) = (x, c, 0);
		} else if (h < 180) {
			(r, g, b) = (0, c, x);
		} else if (h < 240) {
			(r, g, b) = (0, x, c);
		} else if (h < 300) {
			(r, g, b) = (x, 0, c);
		} else {
			(r, g, b) = (c, 0, x);
		}
		return ColorSpace.RgbToHex((r + m, g + m, b + m));
	}

	private static ArgumentException InvalidColor(string input) {
		return new ArgumentException($"INVALID_COLOR: \"{input}\"", nameof(input));
	}
}
