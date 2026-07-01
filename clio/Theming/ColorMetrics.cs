using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Theming;

/// <summary>An accent candidate scored against white-contrast and OKLab distance from the primary.</summary>
internal sealed record ScoredAccentCandidate(string Hex, int Offset, double ContrastOnWhite, double DistanceFromPrimary);

/// <summary>A suggested darker primary that reaches WCAG contrast on white, with the supporting metrics.</summary>
internal sealed record AdaptedPrimary(
	string Original500,
	string Adapted500,
	double OriginalContrastOnWhite,
	double AdaptedContrastOnWhite,
	double DistanceFromOriginal);

/// <summary>
/// Colour comparison and contrast-driven decisions: WCAG relative luminance and contrast ratio, OKLab
/// perceptual distance, the best-accent selection, and a darker-primary suggestion when a colour falls
/// below the minimum contrast on white.
/// </summary>
internal static class ColorMetrics {

	/// <summary>The base light colour (white) used as the contrast reference.</summary>
	internal const string White = "#ffffff";

	/// <summary>Minimum WCAG contrast against white for a colour to be considered usable.</summary>
	private const double MinContrastOnWhite = 3.0;

	/// <summary>WCAG relative luminance of a colour.</summary>
	internal static double RelativeLuminance(string hex) {
		(double r, double g, double b) = ColorSpace.HexToRgb(hex);
		return 0.2126 * ColorSpace.LinearizeChannel(r)
			+ 0.7152 * ColorSpace.LinearizeChannel(g)
			+ 0.0722 * ColorSpace.LinearizeChannel(b);
	}

	/// <summary>WCAG contrast ratio between two colours (1..21).</summary>
	internal static double ContrastRatio(string hexA, string hexB) {
		double l1 = RelativeLuminance(hexA);
		double l2 = RelativeLuminance(hexB);
		return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
	}

	/// <summary>Euclidean distance between two colours in OKLab space.</summary>
	internal static double DistanceOklab(string hexA, string hexB) {
		(double l1, double a1, double b1) = ColorSpace.HexToOklab(hexA);
		(double l2, double a2, double b2) = ColorSpace.HexToOklab(hexB);
		return Math.Sqrt(Math.Pow(l1 - l2, 2) + Math.Pow(a1 - a2, 2) + Math.Pow(b1 - b2, 2));
	}

	/// <summary>Picks the most distinct AA-on-white accent; falls back to the highest-contrast candidate.</summary>
	internal static ScoredAccentCandidate ChooseBestAccent(string primaryHex, IReadOnlyList<AccentCandidate> candidates) {
		List<ScoredAccentCandidate> enriched = candidates
			.Select(candidate => new ScoredAccentCandidate(
				candidate.Hex,
				candidate.Offset,
				ContrastRatio(candidate.Hex, White),
				DistanceOklab(primaryHex, candidate.Hex)))
			.ToList();
		List<ScoredAccentCandidate> valid = enriched
			.Where(candidate => candidate.ContrastOnWhite >= MinContrastOnWhite)
			.ToList();
		return valid.Count > 0
			? valid.OrderByDescending(candidate => candidate.DistanceFromPrimary).First()
			: enriched.OrderByDescending(candidate => candidate.ContrastOnWhite).First();
	}

	/// <summary>Suggests a darker primary that reaches AA on white, or <c>null</c> when already compliant.</summary>
	internal static AdaptedPrimary SuggestAdaptedPrimary500(string primaryHex) {
		double originalContrast = ContrastRatio(primaryHex, White);
		if (originalContrast >= MinContrastOnWhite) {
			return null;
		}
		(double l, double c, double h) = ColorSpace.HexToOklch(primaryHex);
		for (double ls = l; ls >= 0.05; ls -= 0.005) {
			double safeChroma = Math.Min(c, ColorSpace.MaxChromaInGamut(ls, h));
			string adaptedHex = ColorSpace.OklchToHex(ls, safeChroma, h);
			double adaptedContrast = ContrastRatio(adaptedHex, White);
			if (adaptedContrast >= MinContrastOnWhite) {
				return new AdaptedPrimary(
					primaryHex,
					adaptedHex,
					originalContrast,
					adaptedContrast,
					DistanceOklab(primaryHex, adaptedHex));
			}
		}
		return null;
	}
}
