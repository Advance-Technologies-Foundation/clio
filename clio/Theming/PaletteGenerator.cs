using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Theming;

/// <summary>An accent colour together with the hue offset (in degrees) it was generated at.</summary>
internal sealed record AccentCandidate(string Hex, int Offset);

/// <summary>
/// Deterministic palette generation: the twelve-shade ramp around a base colour, the secondary colour
/// derived from a primary, and the accent candidates offset around the primary hue.
/// </summary>
internal static class PaletteGenerator {

	private const int AnchorStep = 500;

	/// <summary>Lightness the lighter shades converge toward (OKLCH L).</summary>
	private const double LightnessTop = 0.99;

	/// <summary>Default lightness floor the darker shades converge toward (OKLCH L).</summary>
	private const double LightnessBottom = 0.2;

	/// <summary>Fraction of the primary lightness the secondary starts from before the cap is applied.</summary>
	private const double SecondaryLightnessScale = 0.61;

	/// <summary>Upper bound on the secondary lightness (OKLCH L), keeping it reliably darker than the primary.</summary>
	private const double SecondaryLightnessCap = 0.3;

	/// <summary>Hue nudge (degrees) applied to the secondary relative to the primary.</summary>
	private const double SecondaryHueShiftDeg = 11;

	/// <summary>Centre hue (degrees) of the chroma-damping dip.</summary>
	private const double SecondaryDipCenterHueDeg = 20;

	/// <summary>Spread (degrees) of the Gaussian chroma-damping dip.</summary>
	private const double SecondaryDipSigmaDeg = 40;

	/// <summary>Baseline fraction of the primary chroma retained by the secondary.</summary>
	private const double SecondaryChromaBase = 0.319;

	/// <summary>Extra chroma removed at the centre of the dip.</summary>
	private const double SecondaryChromaDipDepth = 0.113;

	/// <summary>Chroma floor so the secondary never becomes fully neutral.</summary>
	private const double SecondaryMinChroma = 0.015;

	/// <summary>The twelve palette shade stops, in ascending order; 500 is each hue's base shade.</summary>
	internal static readonly int[] Steps = { 10, 25, 50, 100, 200, 300, 400, 500, 600, 700, 800, 900 };

	/// <summary>Builds the full 12-shade palette from a base (-500) hex colour.</summary>
	internal static IReadOnlyDictionary<int, string> GenerateScale(string hex500) {
		(double l, double c, double h) = ColorSpace.HexToOklch(hex500);
		PaletteMode mode = ColorSpace.DetectMode(l, c, h);
		Dictionary<int, string> palette = new() {
			[AnchorStep] = hex500.ToLowerInvariant()
		};
		int[] lighter = Steps.Where(step => step < AnchorStep).ToArray();
		int[] darker = Steps.Where(step => step > AnchorStep).ToArray();
		foreach (int step in lighter) {
			double t = (double)(AnchorStep - step) / AnchorStep;
			double ls = l + (LightnessTop - l) * t;
			double cs;
			double hs;
			if (mode == PaletteMode.Yellow) {
				cs = c * Math.Pow(1 - t, 0.8);
				hs = h + 4 * t;
			} else {
				cs = c * (1 - t);
				hs = h;
			}
			palette[step] = ColorSpace.OklchToHex(ls, cs, hs);
		}
		double lbot = LightnessBottom;
		if (mode == PaletteMode.Dark) {
			lbot = Math.Max(0.06, Math.Min(0.2, l - 0.035 * (darker.Length + 1)));
		}
		if (mode == PaletteMode.Light) {
			lbot = 0.24;
		}
		lbot = Math.Min(lbot, l);
		for (int i = 0; i < darker.Length; i++) {
			int step = darker[i];
			double f = (double)(i + 1) / (darker.Length + 1);
			double ls = l - (l - lbot) * f;
			double cs;
			double hs;
			if (mode == PaletteMode.Yellow) {
				hs = h - 24 * Math.Sqrt(f);
				double rel = Math.Min(1, c / Math.Max(ColorSpace.MaxChromaInGamut(l, h), 1e-6));
				cs = ColorSpace.MaxChromaInGamut(ls, hs) * Math.Max(rel, 0.85);
			} else {
				hs = h;
				cs = c;
			}
			palette[step] = ColorSpace.OklchToHex(ls, cs, hs);
		}
		return palette;
	}

	/// <summary>Derives the secondary base colour from the primary (darker, hue-shifted, chroma-damped).</summary>
	internal static string DeriveSecondary(string primaryHex) {
		(double l, double c, double h) = ColorSpace.HexToOklch(primaryHex);
		double ls = Math.Min(l * SecondaryLightnessScale, SecondaryLightnessCap);
		double hs = (h - SecondaryHueShiftDeg + 360) % 360;
		double d = Math.Min(Math.Abs(hs - SecondaryDipCenterHueDeg), Math.Abs(hs - (SecondaryDipCenterHueDeg + 360)));
		double dip = Math.Exp(-(d * d) / (2 * SecondaryDipSigmaDeg * SecondaryDipSigmaDeg));
		double mult = SecondaryChromaBase - SecondaryChromaDipDepth * dip;
		double cs = Math.Min(Math.Max(c * mult, SecondaryMinChroma), ColorSpace.MaxChromaInGamut(ls, hs));
		return ColorSpace.OklchToHex(ls, cs, hs);
	}

	/// <summary>Finds the lightness of maximum chroma for a hue, scanning [0.35, 0.85] in 0.01 steps.</summary>
	internal static double FindCuspLightness(double h) {
		double bestL = 0.6;
		double bestC = 0;
		for (double l = 0.35; l <= 0.85; l += 0.01) {
			double c = ColorSpace.MaxChromaInGamut(l, h);
			if (c > bestC) {
				bestC = c;
				bestL = l;
			}
		}
		return bestL;
	}

	/// <summary>Generates the three accent candidates at +135°, +180°, and +225° from the primary hue.</summary>
	internal static IReadOnlyList<AccentCandidate> GenerateAccentCandidates(string primaryHex) {
		(_, _, double h) = ColorSpace.HexToOklch(primaryHex);
		int[] offsets = { 135, 180, 225 };
		List<AccentCandidate> candidates = new(offsets.Length);
		foreach (int offset in offsets) {
			double ha = (h + offset) % 360;
			double la = Math.Min(Math.Max(FindCuspLightness(ha), 0.55), 0.72);
			double ca = ColorSpace.MaxChromaInGamut(la, ha) * 0.97;
			candidates.Add(new AccentCandidate(ColorSpace.OklchToHex(la, ca, ha), offset));
		}
		return candidates;
	}
}
