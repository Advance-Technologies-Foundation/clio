using System;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Theming;

/// <summary>
/// Focused unit anchors for <see cref="ColorSpace"/>: the rounding, gamut, and mode-detection behaviour
/// that the broad fixture exercises in aggregate but that deserve explicit, named pins.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Theming")]
public sealed class ColorSpaceTests {

	[Test]
	[Description("RoundHalfUp rounds the double just below 0.5 DOWN to 0, where the naive Math.Floor(x+0.5) wrongly yields 1.")]
	public void RoundHalfUp_ShouldRoundDownToZero_WhenInputIsTheDoubleJustBelowHalf() {
		// Arrange
		const double justBelowHalf = 0.49999999999999994;

		// Act
		double rounded = ColorSpace.RoundHalfUp(justBelowHalf);
		double naiveFloorPlusHalf = Math.Floor(justBelowHalf + 0.5);

		// Assert
		rounded.Should().Be(0.0,
			because: "0.49999999999999994 is below 0.5, so it rounds to 0 — comparing the exact fractional part avoids the FP-addition error");
		naiveFloorPlusHalf.Should().Be(1.0,
			because: "Math.Floor(x+0.5) misrounds this input to 1, which is why RoundHalfUp is used instead");
	}

	[Test]
	[Description("RoundHalfUp rounds halves toward +∞ for representative midpoints.")]
	public void RoundHalfUp_ShouldRoundHalvesTowardPositiveInfinity_ForMidpoints() {
		// Act / Assert
		ColorSpace.RoundHalfUp(0.5).Should().Be(1.0, because: "0.5 rounds to 1");
		ColorSpace.RoundHalfUp(2.5).Should().Be(3.0, because: "2.5 rounds to 3");
		ColorSpace.RoundHalfUp(-0.5).Should().Be(0.0, because: "-0.5 ties toward +∞, i.e. 0");
		ColorSpace.RoundHalfUp(127.5).Should().Be(128.0, because: "a mid-channel .5 must round up before clamping");
	}

	[Test]
	[Description("RgbToHex rounds THEN clamps to [0,255]: [2,-1,0.5] -> #ff0080, plus black/white extremes.")]
	public void RgbToHex_ShouldRoundThenClamp_ForSpecAnchors() {
		// Act / Assert
		ColorSpace.RgbToHex((0.0, 0.0, 0.0)).Should().Be("#000000", because: "black maps to all-zero channels");
		ColorSpace.RgbToHex((1.0, 1.0, 1.0)).Should().Be("#ffffff", because: "white maps to all-255 channels");
		ColorSpace.RgbToHex((2.0, -1.0, 0.5)).Should().Be("#ff0080",
			because: "2 clamps to ff, -1 clamps to 00, 0.5*255=127.5 rounds to 0x80 — round then clamp");
	}

	[Test]
	[Description("HexToOklch maps pure black to the OKLCH origin.")]
	public void HexToOklch_ShouldReturnOrigin_ForBlack() {
		// Act
		(double l, double c, double h) = ColorSpace.HexToOklch("#000000");

		// Assert
		l.Should().Be(0.0, because: "black has zero lightness in OKLab");
		c.Should().Be(0.0, because: "black is achromatic");
		h.Should().Be(0.0, because: "atan2(0,0) is 0, normalized to 0°");
	}

	[Test]
	[Description("DetectMode classifies the four ramp modes at their representative colours.")]
	public void DetectMode_ShouldClassifyRampMode_ForRepresentativeColours() {
		// Act / Assert
		Mode("#004fd6").Should().Be(PaletteMode.Standard, because: "the calibration primary is a standard mid colour");
		Mode("#0d2e4e").Should().Be(PaletteMode.Dark, because: "the derived secondary is dark (L < 0.38)");
		Mode("#ffffff").Should().Be(PaletteMode.Light, because: "white is light (L > 0.78)");
		Mode("#ffd700").Should().Be(PaletteMode.Yellow, because: "gold sits in the 80°–105° / chroma>0.06 yellow band");

		static PaletteMode Mode(string hex) {
			(double l, double c, double h) = ColorSpace.HexToOklch(hex);
			return ColorSpace.DetectMode(l, c, h);
		}
	}

	[Test]
	[Description("HexToRgb fails fast with a user-friendly ArgumentException on a non-6-hex input.")]
	public void HexToRgb_ShouldThrowArgumentException_WhenInputIsNotSixHexDigits() {
		// Act
		Action act = () => ColorSpace.HexToRgb("#12");

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*normalized 6-digit hex*",
				because: "malformed input is surfaced clearly rather than as a raw FormatException");
	}
}
