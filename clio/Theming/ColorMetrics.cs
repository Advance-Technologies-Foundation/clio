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

/// <summary>The three possible outcomes of evaluating a primary colour for readability on white.</summary>
internal enum AdaptedPrimaryOutcome {
	/// <summary>The primary already meets the minimum contrast on white; no adaptation needed.</summary>
	Compliant,

	/// <summary>The primary was below the minimum but a darker compliant variant was found.</summary>
	Adapted,

	/// <summary>The primary was below the minimum and no darker variant reached the minimum.</summary>
	CouldNotAdapt
}

/// <summary>
/// The full outcome of adapting a primary: which of the three states applies, the original contrast (for the
/// non-compliant states), and the darker variant when one was found. Distinguishes "already fine" from
/// "could not fix" — both of which a bare <c>null</c> would otherwise conflate.
/// </summary>
internal sealed record AdaptedPrimaryResult(
	AdaptedPrimaryOutcome Outcome,
	string Original500,
	double OriginalContrastOnWhite,
	AdaptedPrimary Adapted);

/// <summary>The classification of an accent colour's OKLab distance from the primary.</summary>
internal enum AccentSimilarityBand {
	/// <summary>Distinct enough to offer plainly (distance ≥ 0.10).</summary>
	Clean,

	/// <summary>Close but offerable with a caveat (0.07 ≤ distance &lt; 0.10).</summary>
	Warn,

	/// <summary>Too similar to offer (distance &lt; 0.07).</summary>
	Strong
}

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

	/// <summary>OKLab distance below which an accent is flagged as too similar to the primary (a caveat).</summary>
	internal const double AccentSimilarityWarning = 0.10;

	/// <summary>OKLab distance below which an accent is too similar to the primary to offer at all.</summary>
	internal const double AccentSimilarityStrong = 0.07;

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

	/// <summary>Picks the most distinct 3:1-on-white accent; falls back to the highest-contrast candidate.</summary>
	internal static ScoredAccentCandidate ChooseBestAccent(string primaryHex, IReadOnlyList<AccentCandidate> candidates) {
		if (candidates is null || candidates.Count == 0) {
			throw new ArgumentException("At least one accent candidate is required.", nameof(candidates));
		}
		List<ScoredAccentCandidate> enriched = ScoreCandidates(primaryHex, candidates);
		List<ScoredAccentCandidate> valid = enriched
			.Where(candidate => candidate.ContrastOnWhite >= MinContrastOnWhite)
			.ToList();
		return valid.Count > 0
			? valid.OrderByDescending(candidate => candidate.DistanceFromPrimary).First()
			: enriched.OrderByDescending(candidate => candidate.ContrastOnWhite).First();
	}

	/// <summary>Whether a colour meets the minimum WCAG contrast (3:1) on white — the usability gate.</summary>
	internal static bool MeetsMinContrastOnWhite(string hex) {
		return ContrastRatio(hex, White) >= MinContrastOnWhite;
	}

	/// <summary>Classifies an OKLab distance from the primary into the accent-similarity band.</summary>
	internal static AccentSimilarityBand ClassifySimilarityBand(double distanceFromPrimary) {
		if (distanceFromPrimary >= AccentSimilarityWarning) {
			return AccentSimilarityBand.Clean;
		}
		return distanceFromPrimary >= AccentSimilarityStrong
			? AccentSimilarityBand.Warn
			: AccentSimilarityBand.Strong;
	}

	/// <summary>Whether an accent is valid: usable on white (≥3:1) AND distinct enough from the primary (≥0.07).</summary>
	internal static bool IsValidAccent(double contrastOnWhite, double distanceFromPrimary) {
		return contrastOnWhite >= MinContrastOnWhite && distanceFromPrimary >= AccentSimilarityStrong;
	}

	/// <summary>
	/// Scores every accent candidate against the primary and picks the most distinct VALID one. Unlike
	/// <see cref="ChooseBestAccent"/>, the valid set requires BOTH ≥3:1 contrast AND ≥0.07 OKLab distance, and
	/// there is no degenerate max-contrast fallback: when nothing is valid the best is <c>null</c>.
	/// </summary>
	/// <param name="primaryHex">The primary the candidates are compared against.</param>
	/// <param name="candidates">The raw accent candidates to score.</param>
	/// <param name="validCount">The number of candidates that passed both gates.</param>
	/// <param name="scored">Every candidate, enriched with its contrast and distance (for display).</param>
	/// <returns>The max-distance valid candidate, or <c>null</c> when none is valid.</returns>
	internal static ScoredAccentCandidate SelectBestValidAccent(
		string primaryHex,
		IReadOnlyList<AccentCandidate> candidates,
		out int validCount,
		out IReadOnlyList<ScoredAccentCandidate> scored) {
		List<ScoredAccentCandidate> enriched = ScoreCandidates(primaryHex, candidates);
		scored = enriched;
		List<ScoredAccentCandidate> valid = enriched
			.Where(candidate => IsValidAccent(candidate.ContrastOnWhite, candidate.DistanceFromPrimary))
			.ToList();
		validCount = valid.Count;
		return valid
			.OrderByDescending(candidate => candidate.DistanceFromPrimary)
			.FirstOrDefault();
	}

	private static List<ScoredAccentCandidate> ScoreCandidates(string primaryHex, IReadOnlyList<AccentCandidate> candidates) {
		return candidates
			.Select(candidate => new ScoredAccentCandidate(
				candidate.Hex,
				candidate.Offset,
				ContrastRatio(candidate.Hex, White),
				DistanceOklab(primaryHex, candidate.Hex)))
			.ToList();
	}

	/// <summary>
	/// Evaluates a primary for readability on white and returns the three-state outcome: already compliant,
	/// adapted to a darker compliant variant, or could-not-adapt (below the minimum with no compliant darker
	/// variant). The original contrast is carried for both non-compliant states.
	/// </summary>
	internal static AdaptedPrimaryResult AdaptPrimary500(string primaryHex) {
		double originalContrast = ContrastRatio(primaryHex, White);
		if (originalContrast >= MinContrastOnWhite) {
			return new AdaptedPrimaryResult(AdaptedPrimaryOutcome.Compliant, null, originalContrast, null);
		}
		(double l, double c, double h) = ColorSpace.HexToOklch(primaryHex);
		for (double ls = l; ls >= 0.05; ls -= 0.005) {
			double safeChroma = Math.Min(c, ColorSpace.MaxChromaInGamut(ls, h));
			string adaptedHex = ColorSpace.OklchToHex(ls, safeChroma, h);
			double adaptedContrast = ContrastRatio(adaptedHex, White);
			if (adaptedContrast >= MinContrastOnWhite) {
				AdaptedPrimary adapted = new(
					primaryHex,
					adaptedHex,
					originalContrast,
					adaptedContrast,
					DistanceOklab(primaryHex, adaptedHex));
				return new AdaptedPrimaryResult(AdaptedPrimaryOutcome.Adapted, primaryHex, originalContrast, adapted);
			}
		}
		return new AdaptedPrimaryResult(AdaptedPrimaryOutcome.CouldNotAdapt, primaryHex, originalContrast, null);
	}

	/// <summary>Suggests a darker primary that reaches 3:1 on white, or <c>null</c> when already compliant or unfixable.</summary>
	internal static AdaptedPrimary SuggestAdaptedPrimary500(string primaryHex) {
		AdaptedPrimaryResult result = AdaptPrimary500(primaryHex);
		return result.Outcome == AdaptedPrimaryOutcome.Adapted ? result.Adapted : null;
	}
}
