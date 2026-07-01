using System;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Theming;

/// <summary>
/// Calibration anchors for <see cref="ColorNormalizer.Normalize"/>: the full shorthand / no-hash /
/// lowercase / rgb() / hsl() / named matrix and the alpha-rejection contract.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Theming")]
public sealed class ColorNormalizerTests {

	[Test]
	[Description("Normalize accepts #RGB / RRGGBB / rgb() / hsl() / named colours and lowercases.")]
	public void Normalize_ShouldAcceptEveryContractForm_AndLowercase() {
		// Act / Assert
		ColorNormalizer.Normalize("#FFF").Should().Be("#ffffff", because: "#RGB expands and lowercases");
		ColorNormalizer.Normalize("004FD6").Should().Be("#004fd6", because: "a no-# 6-hex value is prefixed and lowercased");
		ColorNormalizer.Normalize("rgb(0, 79, 214)").Should().Be("#004fd6", because: "rgb() channels convert to hex");
		ColorNormalizer.Normalize("hsl(217, 100%, 42%)").Should().Be("#0052d6", because: "hsl() converts via the HSL→RGB path");
		ColorNormalizer.Normalize("CornflowerBlue").Should().Be("#6495ed", because: "a CSS named colour resolves via lowercase-then-ordinal-exact lookup");
	}

	[Test]
	[Description("Normalize rejects every alpha form with ALPHA_NOT_SUPPORTED.")]
	public void Normalize_ShouldRejectAlphaForms_WhenInputHasAlpha() {
		// Act / Assert
		Invoking("rgba(0, 0, 0, 0.5)").Should().Throw<ArgumentException>().WithMessage("ALPHA_NOT_SUPPORTED*",
			because: "rgba() carries alpha, which the theme contract does not support");
		Invoking("hsla(217, 100%, 42%, 0.5)").Should().Throw<ArgumentException>().WithMessage("ALPHA_NOT_SUPPORTED*",
			because: "hsla() carries alpha");
		Invoking("#004fd680").Should().Throw<ArgumentException>().WithMessage("ALPHA_NOT_SUPPORTED*",
			because: "an 8-digit hex is #RRGGBBAA — alpha");
	}

	[Test]
	[Description("Normalize rejects unrecognized and out-of-range input with INVALID_COLOR.")]
	public void Normalize_ShouldRejectUnrecognized_WithInvalidColor() {
		// Act / Assert
		Invoking("not-a-color").Should().Throw<ArgumentException>().WithMessage("INVALID_COLOR*",
			because: "an unrecognized token is not a colour");
		Invoking("rgb(300, 0, 0)").Should().Throw<ArgumentException>().WithMessage("INVALID_COLOR*",
			because: "an rgb() channel above 255 is out of range");
		Invoking("hsl(0, 200%, 50%)").Should().Throw<ArgumentException>().WithMessage("INVALID_COLOR*",
			because: "an hsl() saturation above 100% is out of range — rejected like an rgb() channel above 255, not silently clamped");
		Invoking("hsl(0, 50%, 150%)").Should().Throw<ArgumentException>().WithMessage("INVALID_COLOR*",
			because: "an hsl() lightness above 100% is out of range — rejected like an rgb() channel above 255, not silently clamped");
		Invoking(null).Should().Throw<ArgumentException>().WithMessage("INVALID_COLOR*",
			because: "a null input is not a valid colour");
	}

	[Test]
	[SetCulture("de-DE")]
	[Description("Normalize's rgb()/hsl() numeric parsing is culture-invariant under de-DE.")]
	public void Normalize_ShouldParseRgbAndHslInvariantly_WhenAmbientCultureIsGerman() {
		// Act / Assert
		ColorNormalizer.Normalize("rgb(0, 79, 214)").Should().Be("#004fd6",
			because: "rgb() channel parsing must not depend on the ambient decimal separator");
		ColorNormalizer.Normalize("hsl(217, 100%, 42%)").Should().Be("#0052d6",
			because: "hsl() parsing must be culture-invariant");
	}

	private static Action Invoking(string input) {
		return () => ColorNormalizer.Normalize(input);
	}
}
