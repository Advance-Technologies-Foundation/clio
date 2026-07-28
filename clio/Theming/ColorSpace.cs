using System;
using System.Globalization;

namespace Clio.Theming;

/// <summary>Classifies how a colour's shade ramp is generated, based on its lightness, chroma, and hue.</summary>
internal enum PaletteMode {
	/// <summary>Default ramp behaviour.</summary>
	Standard,

	/// <summary>Light base colour (OKLCH lightness &gt; 0.78).</summary>
	Light,

	/// <summary>Dark base colour (OKLCH lightness &lt; 0.38).</summary>
	Dark,

	/// <summary>Yellow-ish hue (80°–105° with chroma &gt; 0.06) — gets bespoke ramp handling.</summary>
	Yellow
}

/// <summary>
/// Deterministic colour-space conversions over the OKLCH model: a colour travels
/// <c>HEX → sRGB → linear RGB → OKLab → OKLCH</c> through fixed matrices, and back. Also finds the
/// largest in-gamut chroma for a lightness/hue and renders OKLCH coordinates as a hex colour.
/// </summary>
internal static class ColorSpace {

	/// <summary>Splits a 6-digit <c>#rrggbb</c> colour into its red, green, and blue channels in the range [0,1].</summary>
	/// <param name="hex">A 6-digit hex colour, with or without a leading <c>#</c>.</param>
	/// <exception cref="ArgumentException">The value is not a 6-digit hex colour.</exception>
	internal static (double R, double G, double B) HexToRgb(string hex) {
		string normalized = hex.Replace("#", string.Empty);
		if (normalized.Length != 6) {
			throw new ArgumentException(
				$"Expected a normalized 6-digit hex colour ('#rrggbb'); got '{hex}'. Inputs must pass through ColorNormalizer.Normalize first.",
				nameof(hex));
		}
		return (
			ParseChannel(normalized, 0) / 255.0,
			ParseChannel(normalized, 2) / 255.0,
			ParseChannel(normalized, 4) / 255.0);
	}

	/// <summary>sRGB → linear-RGB gamma expansion for a single channel.</summary>
	internal static double LinearizeChannel(double value) {
		return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
	}

	/// <summary>Converts a hex colour to its OKLab coordinates (lightness, a, b).</summary>
	internal static (double L, double A, double B) HexToOklab(string hex) {
		(double r, double g, double b) = HexToRgb(hex);
		double rl = LinearizeChannel(r);
		double gl = LinearizeChannel(g);
		double bl = LinearizeChannel(b);
		double ox = 0.4122214708 * rl + 0.5363325363 * gl + 0.0514459929 * bl;
		double oy = 0.2119034982 * rl + 0.6806995451 * gl + 0.1073969566 * bl;
		double oz = 0.0883024619 * rl + 0.2817188376 * gl + 0.6299787005 * bl;
		double lv = Math.Cbrt(ox);
		double mv = Math.Cbrt(oy);
		double sv = Math.Cbrt(oz);
		double l = 0.2104542553 * lv + 0.793617785 * mv - 0.0040720468 * sv;
		double a = 1.9779984951 * lv - 2.428592205 * mv + 0.4505937099 * sv;
		double bb = 0.0259040371 * lv + 0.7827717662 * mv - 0.808675766 * sv;
		return (l, a, bb);
	}

	/// <summary>Converts a hex colour to its OKLCH coordinates (lightness, chroma, and hue in degrees).</summary>
	internal static (double L, double C, double H) HexToOklch(string hex) {
		(double l, double a, double b) = HexToOklab(hex);
		double c = Math.Sqrt(a * a + b * b);
		double h = (Math.Atan2(b, a) * 180.0 / Math.PI + 360.0) % 360.0;
		return (l, c, h);
	}

	/// <summary>Converts OKLCH back to (delinearized) sRGB channels — values may fall outside [0,1].</summary>
	internal static (double R, double G, double B) OklchToRgb(double l, double c, double h) {
		double hr = h * Math.PI / 180.0;
		double a = c * Math.Cos(hr);
		double b = c * Math.Sin(hr);
		double lv = l + 0.3963377774 * a + 0.2158037573 * b;
		double mv = l - 0.1055613458 * a - 0.0638541728 * b;
		double sv = l - 0.0894841775 * a - 1.291485548 * b;
		double lc = Math.Pow(lv, 3);
		double mc = Math.Pow(mv, 3);
		double sc = Math.Pow(sv, 3);
		return (
			DelinearizeChannel(4.0767416621 * lc - 3.3077115913 * mc + 0.2309699292 * sc),
			DelinearizeChannel(-1.2684380046 * lc + 2.6097574011 * mc - 0.3413193965 * sc),
			DelinearizeChannel(-0.0041960863 * lc - 0.7034186147 * mc + 1.707614701 * sc));
	}

	/// <summary>Encodes sRGB channels as a <c>#rrggbb</c> colour, rounding each channel to the nearest byte and clamping to [0,255].</summary>
	internal static string RgbToHex((double R, double G, double B) rgb) {
		return "#" + ToHexChannel(rgb.R) + ToHexChannel(rgb.G) + ToHexChannel(rgb.B);
	}

	/// <summary>Rounds to the nearest integer, with exact halves rounded toward positive infinity.</summary>
	internal static double RoundHalfUp(double x) {
		double floor = Math.Floor(x);
		double frac = x - floor;
		if (frac < 0.5) {
			return floor;
		}
		return floor + 1.0;
	}

	/// <summary>Finds the largest chroma that stays within the sRGB gamut for the given lightness and hue.</summary>
	internal static double MaxChromaInGamut(double l, double h) {
		double lo = 0.0;
		double hi = 0.4;
		for (int i = 0; i < 24; i++) {
			double mid = (lo + hi) / 2.0;
			(double r, double g, double b) = OklchToRgb(l, mid, h);
			if (InGamut(r) && InGamut(g) && InGamut(b)) {
				lo = mid;
			} else {
				hi = mid;
			}
		}
		return lo;
	}

	/// <summary>Renders OKLCH as hex, normalizing the hue and clamping chroma into gamut first.</summary>
	internal static string OklchToHex(double l, double c, double h) {
		double safeHue = ((h % 360.0) + 360.0) % 360.0;
		double safeChroma = Math.Min(c, MaxChromaInGamut(l, safeHue));
		return RgbToHex(OklchToRgb(l, safeChroma, safeHue));
	}

	/// <summary>Classifies a colour's ramp mode from its OKLCH coordinates.</summary>
	internal static PaletteMode DetectMode(double l, double c, double h) {
		if (h >= 80 && h <= 105 && c > 0.06) {
			return PaletteMode.Yellow;
		}
		if (l < 0.38) {
			return PaletteMode.Dark;
		}
		if (l > 0.78) {
			return PaletteMode.Light;
		}
		return PaletteMode.Standard;
	}

	private static int ParseChannel(string sixHex, int start) {
		return byte.Parse(sixHex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
	}

	private static double DelinearizeChannel(double value) {
		return value <= 0.0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
	}

	private static string ToHexChannel(double channel) {
		int rounded = (int)Math.Max(0.0, Math.Min(255.0, RoundHalfUp(channel * 255.0)));
		return rounded.ToString("x2", CultureInfo.InvariantCulture);
	}

	private static bool InGamut(double channel) {
		return channel >= -0.001 && channel <= 1.001;
	}
}
