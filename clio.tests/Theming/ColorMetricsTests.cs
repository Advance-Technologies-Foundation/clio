using System.Collections.Generic;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Theming;

/// <summary>
/// Calibration anchors for <see cref="ColorMetrics"/>: WCAG luminance/contrast bounds, OKLab distance, the
/// accent choice, the stable tie-break on equal scores, and the darker-primary suggestion.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Theming")]
public sealed class ColorMetricsTests {

	[Test]
	[Description("relativeLuminance pins black to 0 and white to 1.")]
	public void RelativeLuminance_ShouldPinBlackAndWhite() {
		// Act / Assert
		ColorMetrics.RelativeLuminance("#000000").Should().Be(0.0, because: "black has zero luminance");
		ColorMetrics.RelativeLuminance("#ffffff").Should().Be(1.0, because: "white has full luminance");
	}

	[Test]
	[Description("contrastRatio spans 1..21 at the extremes.")]
	public void ContrastRatio_ShouldSpanOneToTwentyOne() {
		// Act / Assert
		ColorMetrics.ContrastRatio("#000000", "#ffffff").Should().Be(21.0, because: "black-on-white is the maximum WCAG contrast");
		ColorMetrics.ContrastRatio("#004fd6", "#004fd6").Should().Be(1.0, because: "a colour against itself has no contrast");
	}

	[Test]
	[Description("distanceOklab is zero for identical colours.")]
	public void DistanceOklab_ShouldBeZero_ForIdenticalColours() {
		// Act / Assert
		ColorMetrics.DistanceOklab("#004fd6", "#004fd6").Should().Be(0.0, because: "a colour has zero OKLab distance from itself");
	}

	[Test]
	[Description("chooseBestAccent prefers the most distinct 3:1-on-white candidate.")]
	public void ChooseBestAccent_ShouldPreferMostDistinctAaCandidate_ForCalibrationAnchor() {
		// Act
		ScoredAccentCandidate best = ColorMetrics.ChooseBestAccent("#004fd6", PaletteGenerator.GenerateAccentCandidates("#004fd6"));

		// Assert
		best.Hex.Should().Be("#f94e11", because: "the +135° candidate is the most distinct 3:1-on-white accent");
		best.Offset.Should().Be(135, because: "the chosen candidate carries its originating hue offset");
	}

	[Test]
	[Description("chooseBestAccent preserves original candidate order on a score tie (a stable sort), so the first candidate wins.")]
	public void ChooseBestAccent_ShouldPreserveOriginalOrder_WhenScoresTie() {
		// Arrange — two candidates with identical hex => identical contrast + distance => a tie.
		List<AccentCandidate> tied = new() {
			new AccentCandidate("#f94e11", 135),
			new AccentCandidate("#f94e11", 999),
		};

		// Act
		ScoredAccentCandidate best = ColorMetrics.ChooseBestAccent("#004fd6", tied);

		// Assert
		best.Offset.Should().Be(135,
			because: "on a tie the first candidate must win (stable sort) — a non-stable sort could return offset 999");
	}

	[Test]
	[Description("suggestAdaptedPrimary500 returns null when already compliant and the calibrated darkened value otherwise.")]
	public void SuggestAdaptedPrimary500_ShouldReturnNullOrCalibratedDarkening() {
		// Act / Assert
		ColorMetrics.SuggestAdaptedPrimary500("#000000").Should().BeNull(
			because: "black already exceeds 3:1 on white, so no adaptation is suggested");
		ColorMetrics.SuggestAdaptedPrimary500("#cccccc")!.Adapted500.Should().Be("#949494",
			because: "a low-contrast grey is darkened to the calibrated AA-passing value");
	}

	[Test]
	[Description("MeetsMinContrastOnWhite gates on the 3:1 usability threshold so the caller never sees the number.")]
	public void MeetsMinContrastOnWhite_ShouldGateOnThreeToOne() {
		// Act / Assert
		ColorMetrics.MeetsMinContrastOnWhite("#000000").Should().BeTrue(because: "black far exceeds 3:1 on white");
		ColorMetrics.MeetsMinContrastOnWhite("#004fd6").Should().BeTrue(because: "the calibration primary passes 3:1 on white");
		ColorMetrics.MeetsMinContrastOnWhite("#cccccc").Should().BeFalse(because: "a light grey is below 3:1 on white");
	}

	[Test]
	[Description("ClassifySimilarityBand maps OKLab distance to clean/warn/strong at the 0.10 and 0.07 boundaries (inclusive-lower).")]
	public void ClassifySimilarityBand_ShouldMapDistanceToBand() {
		// Act / Assert
		ColorMetrics.ClassifySimilarityBand(0.20).Should().Be(AccentSimilarityBand.Clean, because: "distance >= 0.10 is distinct enough to offer plainly");
		ColorMetrics.ClassifySimilarityBand(0.10).Should().Be(AccentSimilarityBand.Clean, because: "exactly 0.10 is clean (inclusive lower bound)");
		ColorMetrics.ClassifySimilarityBand(0.08).Should().Be(AccentSimilarityBand.Warn, because: "0.07..0.10 is offerable with a caveat");
		ColorMetrics.ClassifySimilarityBand(0.07).Should().Be(AccentSimilarityBand.Warn, because: "exactly 0.07 is warn (inclusive lower bound)");
		ColorMetrics.ClassifySimilarityBand(0.05).Should().Be(AccentSimilarityBand.Strong, because: "below 0.07 is too similar to offer");
	}

	[Test]
	[Description("IsValidAccent requires BOTH >=3:1 contrast AND >=0.07 OKLab distance.")]
	public void IsValidAccent_ShouldRequireBothGates() {
		// Act / Assert
		ColorMetrics.IsValidAccent(4.0, 0.20).Should().BeTrue(because: "both gates pass");
		ColorMetrics.IsValidAccent(2.9, 0.20).Should().BeFalse(because: "contrast below 3:1 fails the usability gate");
		ColorMetrics.IsValidAccent(4.0, 0.06).Should().BeFalse(because: "distance below 0.07 fails the distinctness gate");
	}

	[Test]
	[Description("SelectBestValidAccent scores all candidates, counts the valid ones, and returns the most distinct valid candidate (not the ChooseBestAccent degenerate fallback).")]
	public void SelectBestValidAccent_ShouldReturnMostDistinctValidCandidate() {
		// Act
		ScoredAccentCandidate best = ColorMetrics.SelectBestValidAccent(
			"#004fd6",
			PaletteGenerator.GenerateAccentCandidates("#004fd6"),
			out int validCount,
			out System.Collections.Generic.IReadOnlyList<ScoredAccentCandidate> scored);

		// Assert
		scored.Count.Should().Be(3, because: "every generated candidate is scored and returned for display");
		validCount.Should().BeGreaterThan(0, because: "the calibration primary yields at least one valid accent");
		best.Should().NotBeNull(because: "a valid accent exists");
		best!.Hex.Should().Be("#f94e11", because: "the +135° candidate is the most distinct valid accent");
	}

	[Test]
	[Description("AdaptPrimary500 reports Compliant (with original contrast) when the primary already passes 3:1 on white.")]
	public void AdaptPrimary500_ShouldReportCompliant_WhenAlreadyReadable() {
		// Act
		AdaptedPrimaryResult result = ColorMetrics.AdaptPrimary500("#000000");

		// Assert
		result.Outcome.Should().Be(AdaptedPrimaryOutcome.Compliant, because: "black already exceeds 3:1 on white");
		result.Adapted.Should().BeNull(because: "no darker variant is produced when the primary is already compliant");
		result.OriginalContrastOnWhite.Should().BeGreaterThanOrEqualTo(3.0, because: "the original contrast is carried");
	}

	[Test]
	[Description("AdaptPrimary500 reports Adapted with the calibrated darker variant and the original contrast when the primary is below 3:1.")]
	public void AdaptPrimary500_ShouldReportAdapted_WhenPrimaryTooLight() {
		// Act
		AdaptedPrimaryResult result = ColorMetrics.AdaptPrimary500("#cccccc");

		// Assert
		result.Outcome.Should().Be(AdaptedPrimaryOutcome.Adapted, because: "a low-contrast grey is below 3:1 but a darker compliant variant exists");
		result.Adapted.Should().NotBeNull(because: "the adapted variant is produced");
		result.Adapted!.Adapted500.Should().Be("#949494", because: "the calibrated AA-passing darker value");
		result.OriginalContrastOnWhite.Should().BeLessThan(3.0, because: "the below-threshold original contrast is carried for the non-compliant state");
	}
}
