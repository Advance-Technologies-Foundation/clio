using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Theming;

/// <summary>
/// Calibration anchors for <see cref="PaletteGenerator"/>: the primary ramp, the derived secondary, the
/// three accent candidates, and the <c>FindCuspLightness</c> search bound. (Broad input coverage lives in
/// <see cref="ColorMathParityTests"/>; these are the hand-picked anchors.)
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Theming")]
public sealed class PaletteGeneratorTests {

	[Test]
	[Description("generateScale('#004fd6') reproduces the calibrated 12-shade primary ramp.")]
	public void GenerateScale_ShouldReproduceCalibratedPrimaryRamp_ForCalibrationAnchor() {
		// Act
		System.Collections.Generic.IReadOnlyDictionary<int, string> scale = PaletteGenerator.GenerateScale("#004fd6");

		// Assert
		scale[10].Should().Be("#f7f8fb", because: "shade 10 is the calibrated lightest stop");
		scale[25].Should().Be("#eff4fb", because: "calibrated stop 25");
		scale[50].Should().Be("#e3ebfa", because: "calibrated stop 50");
		scale[100].Should().Be("#cbdbf8", because: "calibrated stop 100");
		scale[200].Should().Be("#9bbaf2", because: "calibrated stop 200");
		scale[300].Should().Be("#6c99ea", because: "calibrated stop 300");
		scale[400].Should().Be("#3d76e1", because: "calibrated stop 400");
		scale[500].Should().Be("#004fd6", because: "the -500 anchor is echoed verbatim");
		scale[600].Should().Be("#0041b5", because: "calibrated stop 600");
		scale[700].Should().Be("#003495", because: "calibrated stop 700");
		scale[800].Should().Be("#002877", because: "calibrated stop 800");
		scale[900].Should().Be("#001c5a", because: "shade 900 is the calibrated darkest stop");
	}

	[Test]
	[Description("generateScale('#ffd700') reproduces the calibrated 12-shade Yellow-mode ramp (a yellow-branch regression fails the aggregate parity test but this pins the exact stop).")]
	public void GenerateScale_ShouldReproduceCalibratedYellowRamp_ForYellowModeAnchor() {
		// Act
		System.Collections.Generic.IReadOnlyDictionary<int, string> scale = PaletteGenerator.GenerateScale("#ffd700");

		// Assert
		scale[10].Should().Be("#fcfbf5", because: "shade 10 is the calibrated lightest Yellow-mode stop");
		scale[25].Should().Be("#fdfbee", because: "calibrated Yellow-mode stop 25");
		scale[50].Should().Be("#fdf9e3", because: "calibrated Yellow-mode stop 50");
		scale[100].Should().Be("#fdf6d0", because: "calibrated Yellow-mode stop 100");
		scale[200].Should().Be("#feefab", because: "calibrated Yellow-mode stop 200");
		scale[300].Should().Be("#fee886", because: "calibrated Yellow-mode stop 300");
		scale[400].Should().Be("#fee05b", because: "calibrated Yellow-mode stop 400");
		scale[500].Should().Be("#ffd700", because: "the -500 anchor is echoed verbatim");
		scale[600].Should().Be("#daa400", because: "calibrated Yellow-mode stop 600");
		scale[700].Should().Be("#ab7a00", because: "calibrated Yellow-mode stop 700");
		scale[800].Should().Be("#7b5300", because: "calibrated Yellow-mode stop 800");
		scale[900].Should().Be("#4d3100", because: "shade 900 is the calibrated darkest Yellow-mode stop");
	}

	[Test]
	[Description("generateScale('#000000') keeps every darker stop at pure black — the ramp never inverts toward lighter shades when the base is already at the lightness floor.")]
	public void GenerateScale_ShouldKeepDarkerStopsFlatBlack_WhenBaseIsPureBlack() {
		// Act
		System.Collections.Generic.IReadOnlyDictionary<int, string> scale = PaletteGenerator.GenerateScale("#000000");

		// Assert
		scale[500].Should().Be("#000000", because: "the -500 anchor is echoed verbatim");
		scale[600].Should().Be("#000000", because: "a darker stop of pure black cannot get lighter than the base");
		scale[700].Should().Be("#000000", because: "a darker stop of pure black cannot get lighter than the base");
		scale[800].Should().Be("#000000", because: "a darker stop of pure black cannot get lighter than the base");
		scale[900].Should().Be("#000000", because: "the darkest stop must stay the deepest shade of the ramp");
	}

	[Test]
	[Description("deriveSecondary('#004fd6') maps the primary to the calibrated secondary.")]
	public void DeriveSecondary_ShouldMapToCalibratedSecondary_ForCalibrationAnchor() {
		// Act / Assert
		PaletteGenerator.DeriveSecondary("#004fd6").Should().Be("#0d2e4e",
			because: "the calibrated secondary derivation is the calibrated anchor");
	}

	[Test]
	[Description("GenerateAccentCandidates('#004fd6') returns all three candidates at +135/+180/+225.")]
	public void GenerateAccentCandidates_ShouldReturnAllThree_ForCalibrationAnchor() {
		// Act
		System.Collections.Generic.IReadOnlyList<AccentCandidate> candidates = PaletteGenerator.GenerateAccentCandidates("#004fd6");

		// Assert
		candidates.Should().HaveCount(3, because: "the hue is offset by 135°, 180° and 225°");
		candidates[0].Should().Be(new AccentCandidate("#f94e11", 135), because: "the +135° candidate is the calibrated anchor");
		candidates[1].Should().Be(new AccentCandidate("#d29a16", 180), because: "the +180° candidate is the calibrated anchor");
		candidates[2].Should().Be(new AccentCandidate("#87b716", 225), because: "the +225° candidate is the calibrated anchor");
	}

	[Test]
	[Description("FindCuspLightness returns a lightness within its [0.35, 0.85] search bounds, pinning the exact double += loop.")]
	public void FindCuspLightness_ShouldStayWithinSearchBounds_ForAnyHue() {
		// Act
		double lightness = PaletteGenerator.FindCuspLightness(250);

		// Assert
		lightness.Should().BeGreaterThanOrEqualTo(0.35, because: "the cusp scan starts at L=0.35");
		lightness.Should().BeLessThanOrEqualTo(0.85, because: "the cusp scan ends at L=0.85");
	}

	[Test]
	[SetCulture("de-DE")]
	[Description("Under a comma-decimal culture (de-DE), generateScale must still produce the invariant calibrated ramp — parse/format use InvariantCulture.")]
	public void GenerateScale_ShouldProduceCalibratedRamp_WhenAmbientCultureIsGerman() {
		// Act
		System.Collections.Generic.IReadOnlyDictionary<int, string> scale = PaletteGenerator.GenerateScale("#004fd6");

		// Assert
		scale[10].Should().Be("#f7f8fb",
			because: "hex parse/format must be culture-independent — the lightest shade matches the calibrated golden under de-DE");
		scale[500].Should().Be("#004fd6",
			because: "the -500 anchor is echoed verbatim");
		scale[900].Should().Be("#001c5a",
			because: "the darkest shade matches the calibrated golden regardless of ambient culture");
	}

	[Test]
	[SetCulture("de-DE")]
	[Description("deriveSecondary is also culture-invariant under de-DE.")]
	public void DeriveSecondary_ShouldMatchCalibratedSecondary_WhenAmbientCultureIsGerman() {
		// Act
		string secondary = PaletteGenerator.DeriveSecondary("#004fd6");

		// Assert
		secondary.Should().Be("#0d2e4e",
			because: "the derived secondary must be culture-independent");
	}
}
