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
	[Description("chooseBestAccent prefers the most distinct AA-on-white candidate.")]
	public void ChooseBestAccent_ShouldPreferMostDistinctAaCandidate_ForCalibrationAnchor() {
		// Act
		ScoredAccentCandidate best = ColorMetrics.ChooseBestAccent("#004fd6", PaletteGenerator.GenerateAccentCandidates("#004fd6"));

		// Assert
		best.Hex.Should().Be("#f94e11", because: "the +135° candidate is the most distinct AA-on-white accent");
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
}
