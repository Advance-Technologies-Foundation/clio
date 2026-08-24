using System.Collections.Generic;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Theming;

/// <summary>
/// Calibration anchors for <see cref="TextTokenResolver"/>: the AA-passing stop walk, the link-hover
/// increment, and the <c>text-on-*</c> light / palette-900 / dark resolution.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Theming")]
public sealed class TextTokenResolverTests {

	private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> Palettes =
		new Dictionary<string, IReadOnlyDictionary<int, string>>(System.StringComparer.Ordinal) {
			["primary"] = PaletteGenerator.GenerateScale("#004fd6"),
			["secondary"] = PaletteGenerator.GenerateScale("#0d2e4e"),
			["accent"] = PaletteGenerator.GenerateScale("#ff4013"),
			["error"] = PaletteGenerator.GenerateScale("#d2310d"),
			["success"] = PaletteGenerator.GenerateScale("#0b8500"),
		};

	[Test]
	[Description("resolveTextToken keeps text-primary at primary-500 (already AA on white).")]
	public void ResolveTextToken_ShouldKeepTextPrimaryAt500_WhenAlreadyAaOnWhite() {
		// Act / Assert
		TextTokenResolver.ResolveTextToken("text-primary", Palettes).Should().Be(new TextTokenResolution("primary", 500),
			because: "primary-500 already passes AA on white");
	}

	[Test]
	[Description("resolveTextToken increments text-accent to the first AA-passing stop (600).")]
	public void ResolveTextToken_ShouldIncrementTextAccentTo600_WhenLighterStopsFailAa() {
		// Act / Assert
		TextTokenResolver.ResolveTextToken("text-accent", Palettes).Should().Be(new TextTokenResolution("accent", 600),
			because: "accent-500 fails AA on white, so the first passing stop is 600");
	}

	[Test]
	[Description("resolveLinkHover derives one stop darker than text-link.")]
	public void ResolveLinkHover_ShouldBeOneStopDarkerThanLink() {
		// Arrange
		TextTokenResolution link = TextTokenResolver.ResolveTextToken("text-link", Palettes);

		// Act / Assert
		TextTokenResolver.ResolveLinkHover(link.Step).Should().Be(new TextTokenResolution("primary", 600),
			because: "link resolves to primary-500, so hover is one stop darker at 600");
	}

	[Test]
	[Description("resolveTextOnColorToken returns the light reference over a dark background.")]
	public void ResolveTextOnColorToken_ShouldReturnBaseLight_OverDarkBackground() {
		// Act
		TextOnColorResolution resolved = TextTokenResolver.ResolveTextOnColorToken("text-on-primary", Palettes["primary"][500], Palettes);

		// Assert
		resolved.Kind.Should().Be(TextOnColorKind.BaseLight, because: "white passes AA over the dark primary-500 background");
	}

	[Test]
	[Description("resolveTextOnColorToken falls back to stop 900 over a light background.")]
	public void ResolveTextOnColorToken_ShouldResolveToStop900_OverLightBackground() {
		// Act
		TextOnColorResolution resolved = TextTokenResolver.ResolveTextOnColorToken("text-on-primary-subtle", Palettes["primary"][10], Palettes);

		// Assert
		resolved.Kind.Should().Be(TextOnColorKind.Palette, because: "white fails AA over the light primary-10 background");
		resolved.PaletteName.Should().Be("primary", because: "the token maps to the primary palette");
		resolved.Step.Should().Be(900, because: "the darkest stop is used as the dark-text option");
	}

	[Test]
	[Description("resolveTextOnColorToken falls through to the dark reference when neither white nor stop 900 reaches AA.")]
	public void ResolveTextOnColorToken_ShouldResolveToBaseDark_WhenWhiteAndStop900BothFailAa() {
		// Act
		TextOnColorResolution resolved = TextTokenResolver.ResolveTextOnColorToken("text-on-accent", Palettes["accent"][500], Palettes);

		// Assert
		resolved.Kind.Should().Be(TextOnColorKind.BaseDark,
			because: "over the accent-500 background white reaches only 3.50 and accent-900 only 4.18, "
				+ "while the dark reference reaches 5.07 and is the first candidate that passes AA");
	}

	[Test]
	[Description("resolveTextOnColorToken picks the highest-contrast candidate when none of the three reaches AA.")]
	public void ResolveTextOnColorToken_ShouldPickHighestContrast_WhenNoCandidateReachesAa() {
		// Act
		TextOnColorResolution resolved = TextTokenResolver.ResolveTextOnColorToken("text-on-primary", Palettes["primary"][400], Palettes);

		// Assert
		resolved.Kind.Should().Be(TextOnColorKind.BaseLight,
			because: "over the mid-tone primary-400 background every candidate fails AA (white 4.31, "
				+ "primary-900 3.71, dark 4.12), so the highest-contrast one wins and no AA-passing "
				+ "text colour exists for that background");
	}
}
