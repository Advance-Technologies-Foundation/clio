using System;
using System.Collections.Generic;
using System.Linq;
using PaletteSet = System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<int, string>>;

namespace Clio.Theming;

/// <summary>The palette + step a text token resolves to.</summary>
internal sealed record TextTokenResolution(string PaletteName, int Step);

/// <summary>Which kind of colour a <c>text-on-*</c> token resolves to.</summary>
internal enum TextOnColorKind {
	/// <summary>The base light colour.</summary>
	BaseLight,

	/// <summary>A specific palette stop.</summary>
	Palette
}

/// <summary>How a <c>text-on-*</c> token is satisfied: either the base light colour, or a specific palette stop.</summary>
internal sealed record TextOnColorResolution {
	/// <summary>Whether the token resolves to the base light colour or a palette stop.</summary>
	internal TextOnColorKind Kind { get; private init; }

	/// <summary>The palette name when <see cref="Kind"/> is <see cref="TextOnColorKind.Palette"/>; otherwise <c>null</c>.</summary>
	internal string PaletteName { get; private init; }

	/// <summary>The palette step when <see cref="Kind"/> is <see cref="TextOnColorKind.Palette"/>; otherwise <c>0</c>.</summary>
	internal int Step { get; private init; }

	/// <summary>The light-base resolution.</summary>
	internal static TextOnColorResolution BaseLight() {
		return new() { Kind = TextOnColorKind.BaseLight };
	}

	/// <summary>A palette-stop resolution.</summary>
	internal static TextOnColorResolution Palette(string paletteName, int step) {
		return new() { Kind = TextOnColorKind.Palette, PaletteName = paletteName, Step = step };
	}
}

/// <summary>
/// Resolves text colour tokens to accessible palette stops: walks a palette from a start step toward 900
/// for the first stop that meets the text contrast minimum on white, derives the link-hover stop one
/// step darker, and chooses base-light versus the darkest palette stop for a <c>text-on-*</c> background.
/// </summary>
internal static class TextTokenResolver {

	/// <summary>Minimum WCAG AA contrast for text tokens (on white / on the resolved background).</summary>
	private const double TextContrastMin = 4.5;

	/// <summary>Text-token → palette mapping, in resolution order.</summary>
	internal static readonly (string Token, string Palette)[] TextTokenPaletteOrdered = {
		("text-heading", PaletteNames.Secondary),
		("text-action", PaletteNames.Secondary),
		("text-action-hover", PaletteNames.Primary),
		("text-link", PaletteNames.Primary),
		("text-primary", PaletteNames.Primary),
		("text-secondary", PaletteNames.Secondary),
		("text-accent", PaletteNames.Accent),
		("text-error", PaletteNames.Error),
		("text-success", PaletteNames.Success),
	};

	/// <summary>Text-on-colour token → palette mapping, in resolution order.</summary>
	internal static readonly (string Token, string Palette)[] TextOnColorPaletteOrdered = {
		("text-on-primary", PaletteNames.Primary),
		("text-on-primary-subtle", PaletteNames.Primary),
		("text-on-primary-soft", PaletteNames.Primary),
		("text-on-secondary", PaletteNames.Secondary),
		("text-on-secondary-subtle", PaletteNames.Secondary),
		("text-on-secondary-soft", PaletteNames.Secondary),
		("text-on-accent", PaletteNames.Accent),
		("text-on-accent-subtle", PaletteNames.Accent),
		("text-on-accent-soft", PaletteNames.Accent),
		("text-on-error", PaletteNames.Error),
		("text-on-error-subtle", PaletteNames.Error),
		("text-on-error-soft", PaletteNames.Error),
		("text-on-success", PaletteNames.Success),
		("text-on-success-subtle", PaletteNames.Success),
		("text-on-success-soft", PaletteNames.Success),
	};

	private static readonly int[] AscendingSteps = { 500, 600, 700, 800, 900 };

	private static readonly IReadOnlyDictionary<string, string> TextTokenPalette =
		TextTokenPaletteOrdered.ToDictionary(entry => entry.Token, entry => entry.Palette, StringComparer.Ordinal);

	private static readonly IReadOnlyDictionary<string, string> TextOnColorPalette =
		TextOnColorPaletteOrdered.ToDictionary(entry => entry.Token, entry => entry.Palette, StringComparer.Ordinal);

	/// <summary>Resolves a text token to the first palette stop (from <paramref name="templateStartStep"/>) that is AA on white.</summary>
	internal static TextTokenResolution ResolveTextToken(string role, PaletteSet palettes, int templateStartStep = 500) {
		string paletteName = TextTokenPalette[role];
		IReadOnlyDictionary<int, string> palette = palettes[paletteName];
		int startIdx = Array.IndexOf(AscendingSteps, templateStartStep);
		IEnumerable<int> steps = startIdx >= 0 ? AscendingSteps.Skip(startIdx) : AscendingSteps;
		int resolvedStep = steps.FirstOrDefault(
			step => ColorMetrics.ContrastRatio(palette[step], ColorMetrics.White) >= TextContrastMin, 900);
		return new TextTokenResolution(paletteName, resolvedStep);
	}

	/// <summary>Returns the link-hover token one stop darker than the resolved link step (capped at 900).</summary>
	internal static TextTokenResolution ResolveLinkHover(int resolvedLinkStep) {
		int idx = Array.IndexOf(AscendingSteps, resolvedLinkStep);
		int next = idx >= 0 && idx < AscendingSteps.Length - 1
			? AscendingSteps[idx + 1]
			: 900;
		return new TextTokenResolution(PaletteNames.Primary, next);
	}

	/// <summary>Resolves a <c>text-on-*</c> token to the base light colour or the role palette's 900 stop.</summary>
	internal static TextOnColorResolution ResolveTextOnColorToken(string token, string bgHex, PaletteSet palettes, string baseLightHex = null) {
		baseLightHex ??= ColorMetrics.White;
		string paletteName = TextOnColorPalette[token];
		if (ColorMetrics.ContrastRatio(baseLightHex, bgHex) >= TextContrastMin) {
			return TextOnColorResolution.BaseLight();
		}
		string stop900 = palettes[paletteName][900];
		if (ColorMetrics.ContrastRatio(stop900, bgHex) >= TextContrastMin) {
			return TextOnColorResolution.Palette(paletteName, 900);
		}
		double cWhite = ColorMetrics.ContrastRatio(baseLightHex, bgHex);
		double c900 = ColorMetrics.ContrastRatio(stop900, bgHex);
		return cWhite >= c900 ? TextOnColorResolution.BaseLight() : TextOnColorResolution.Palette(paletteName, 900);
	}
}
