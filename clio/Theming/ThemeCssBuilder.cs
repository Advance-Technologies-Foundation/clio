using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PaletteSet = System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<int, string>>;

namespace Clio.Theming;

/// <summary>Custom heading/body font selection for a theme, with the weights to load.</summary>
public sealed record FontsInput(string Heading = null, string Body = null, IReadOnlyList<int> Weights = null);

/// <summary>Brand inputs for building a theme's CSS.</summary>
public sealed record BuildThemeInput {
	/// <summary>Required primary colour (any form accepted by <see cref="ColorNormalizer.Normalize"/>).</summary>
	public string Primary { get; init; }

	/// <summary>Optional secondary; derived from the primary when omitted.</summary>
	public string Secondary { get; init; }

	/// <summary>Optional accent; chosen from the primary when omitted.</summary>
	public string Accent { get; init; }

	/// <summary>Optional success colour; defaults to the platform success when omitted.</summary>
	public string Success { get; init; }

	/// <summary>Optional error colour; defaults to the platform error when omitted.</summary>
	public string Error { get; init; }

	/// <summary>Required CSS class applied when the theme is active (<c>^[A-Za-z][A-Za-z0-9_-]*$</c>, ≤100).</summary>
	public string ThemeCssClass { get; init; }

	/// <summary>Optional custom fonts.</summary>
	public FontsInput Fonts { get; init; }
}

/// <summary>Builds the <c>theme.css</c> string from brand inputs and a theme template.</summary>
public interface IThemeCssBuilder {

	/// <summary>
	/// Fills <paramref name="templateCss"/> with the palettes and colour tokens derived from
	/// <paramref name="options"/>, returning the completed <c>theme.css</c>.
	/// </summary>
	/// <param name="templateCss">The theme template to fill.</param>
	/// <param name="options">The brand inputs; <see cref="BuildThemeInput.Primary"/> and
	/// <see cref="BuildThemeInput.ThemeCssClass"/> are required.</param>
	/// <exception cref="ArgumentException">A required input is missing, or a colour, theme class, or font
	/// family is invalid.</exception>
	/// <exception cref="InvalidOperationException">The template does not match the expected contract — an
	/// unresolved <c>&lt;%…%&gt;</c> placeholder remained, or a palette stop was not substituted.</exception>
	string Build(string templateCss, BuildThemeInput options);
}

/// <summary>
/// Fills a theme template into a complete <c>theme.css</c>: validates the inputs, derives and normalizes
/// the five base colours, generates their palettes, strips the template's header comment, fills the theme
/// class, substitutes the palette stops, finalizes the text colour tokens, and applies the fonts. Holds no
/// instance state.
/// </summary>
internal sealed class ThemeCssBuilder : IThemeCssBuilder {

	private const string DefaultFontFamily = "Montserrat";

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	private static readonly string[] GeneratedPaletteNames = { PaletteNames.Primary, PaletteNames.Secondary, PaletteNames.Accent, PaletteNames.Success, PaletteNames.Error };

	private static readonly Regex CommentStripRegex = new(@"/\*(?:(?!\*/)[\s\S])*?Creatio custom theme template(?:(?!\*/)[\s\S])*?\*/\n?", RegexOptions.Compiled, RegexTimeout);
	private static readonly Regex PaletteRefRegex = new(@"var\(--crt-palette-([a-z]+)-(\d+)\)", RegexOptions.Compiled, RegexTimeout);
	private static readonly Regex ColorDeclarationRegex = new(@"(--crt-color-[a-z0-9-]+):\s*([^;]+);", RegexOptions.Compiled, RegexTimeout);

	private static readonly ConcurrentDictionary<string, Regex> DeclarationRegexCache = new(StringComparer.Ordinal);

	private static Regex GetDeclarationRegex(string pattern) {
		return DeclarationRegexCache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.None, RegexTimeout));
	}

	/// <inheritdoc />
	public string Build(string templateCss, BuildThemeInput options) {
		if (string.IsNullOrEmpty(options?.Primary)) {
			throw new ArgumentException("PRIMARY_REQUIRED: a primary color is required.", nameof(options));
		}
		if (string.IsNullOrEmpty(options.ThemeCssClass)) {
			throw new ArgumentException("THEME_CSS_CLASS_REQUIRED: a themeCssClass is required.", nameof(options));
		}
		if (!ThemeParameterValidator.IsValidCssClassName(options.ThemeCssClass)) {
			throw new ArgumentException($"INVALID_THEME_CSS_CLASS: \"{options.ThemeCssClass}\"", nameof(options));
		}
		if (templateCss == null) {
			throw new ArgumentNullException(nameof(templateCss), "A theme template is required.");
		}
		templateCss = templateCss.Replace("\r\n", "\n").Replace("\r", "\n");
		string primary = ColorNormalizer.Normalize(options.Primary);
		string secondary = ColorNormalizer.Normalize(options.Secondary ?? PaletteGenerator.DeriveSecondary(primary));
		string accent = ColorNormalizer.Normalize(options.Accent ?? ColorMetrics.ChooseBestAccent(primary, PaletteGenerator.GenerateAccentCandidates(primary)).Hex);
		string success = ColorNormalizer.Normalize(options.Success ?? ReadTemplateSystemDefault(templateCss, PaletteNames.Success));
		string error = ColorNormalizer.Normalize(options.Error ?? ReadTemplateSystemDefault(templateCss, PaletteNames.Error));
		PaletteSet palettes = new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.Ordinal) {
			[PaletteNames.Primary] = PaletteGenerator.GenerateScale(primary),
			[PaletteNames.Secondary] = PaletteGenerator.GenerateScale(secondary),
			[PaletteNames.Accent] = PaletteGenerator.GenerateScale(accent),
			[PaletteNames.Success] = PaletteGenerator.GenerateScale(success),
			[PaletteNames.Error] = PaletteGenerator.GenerateScale(error),
		};
		string css = CommentStripRegex.Replace(templateCss, string.Empty, 1);
		css = css.Replace("<%themeCssClass%>", options.ThemeCssClass);
		css = ApplyPalettes(css, palettes);
		css = FinalizeTextTokens(css, palettes);
		css = ApplyFonts(css, options.Fonts);
		GuardFilledTemplate(css, palettes);
		return css;
	}

	private static string ReadTemplateSystemDefault(string templateCss, string role) {
		if (!ThemeTemplateDefaults.TryGetPaletteBase(templateCss, role, out string hex)) {
			throw new InvalidOperationException(
				$"The theme template does not define a default --crt-palette-{role}-500 colour.");
		}
		return hex;
	}

	private static string ApplyPalettes(string css, PaletteSet palettes) {
		string next = css;
		foreach (string name in GeneratedPaletteNames) {
			foreach (int step in PaletteGenerator.Steps) {
				Regex regex = GetDeclarationRegex($@"(--crt-palette-{name}-{step}:\s*)#[0-9a-f]{{6}}(;)");
				next = regex.Replace(next, $"$1{palettes[name][step]}$2", 1);
			}
		}
		return next;
	}

	private static string FinalizeTextTokens(string css, PaletteSet palettes) {
		Dictionary<string, string> declarations = ParseColorDeclarations(css);
		string next = css;
		Dictionary<string, int> resolvedSteps = new(StringComparer.Ordinal);
		foreach ((string role, string _) in TextTokenResolver.TextTokenPaletteOrdered) {
			(string Name, int Step)? current = ParsePaletteRef(declarations.GetValueOrDefault($"--crt-color-{role}"));
			TextTokenResolution resolution = TextTokenResolver.ResolveTextToken(role, palettes, current?.Step ?? 500);
			resolvedSteps[role] = resolution.Step;
			next = SetColorDeclaration(next, role, $"var(--crt-palette-{resolution.PaletteName}-{resolution.Step})");
		}
		TextTokenResolution linkHover = TextTokenResolver.ResolveLinkHover(resolvedSteps["text-link"]);
		next = SetColorDeclaration(next, "text-link-hover", $"var(--crt-palette-{linkHover.PaletteName}-{linkHover.Step})");
		foreach ((string token, string _) in TextTokenResolver.TextOnColorPaletteOrdered) {
			string backgroundToken = "--crt-color-background-" + token.Substring("text-on-".Length);
			(string Name, int Step)? background = ParsePaletteRef(declarations.GetValueOrDefault(backgroundToken));
			if (background == null || !palettes.TryGetValue(background.Value.Name, out IReadOnlyDictionary<int, string> backgroundPalette)) {
				continue;
			}
			TextOnColorResolution resolved = TextTokenResolver.ResolveTextOnColorToken(token, backgroundPalette[background.Value.Step], palettes);
			string value = resolved.Kind == TextOnColorKind.BaseLight
				? "var(--crt-color-base-light)"
				: $"var(--crt-palette-{resolved.PaletteName}-{resolved.Step})";
			next = SetColorDeclaration(next, token, value);
		}
		return next;
	}

	private static string ApplyFonts(string css, FontsInput fonts) {
		string headingFamily = string.IsNullOrEmpty(fonts?.Heading) ? DefaultFontFamily : fonts.Heading;
		string bodyFamily = string.IsNullOrEmpty(fonts?.Body) ? DefaultFontFamily : fonts.Body;
		if (headingFamily == DefaultFontFamily && bodyFamily == DefaultFontFamily) {
			return css;
		}
		List<FontFamilyEntry> families = new();
		if (headingFamily != DefaultFontFamily) {
			families.Add(new FontFamilyEntry(headingFamily, fonts?.Weights));
		}
		if (bodyFamily != DefaultFontFamily && bodyFamily != headingFamily) {
			families.Add(new FontFamilyEntry(bodyFamily, fonts?.Weights));
		}
		string importRule = FontImportBuilder.BuildRule(families);
		string next = ReplaceFontFamily(css, "heading", headingFamily);
		next = ReplaceFontFamily(next, "body", bodyFamily);
		return importRule + "\n" + next;
	}

	private static string ReplaceFontFamily(string css, string which, string family) {
		Regex regex = GetDeclarationRegex($@"(--crt-font-family-{which}:\s*)[^;]+(;)");
		return regex.Replace(css, match => match.Groups[1].Value + "'" + family + "', sans-serif" + match.Groups[2].Value, 1);
	}

	private static string SetColorDeclaration(string css, string token, string value) {
		Regex regex = GetDeclarationRegex($@"(--crt-color-{token}:\s*)[^;]+(;)");
		return regex.Replace(css, $"$1{value}$2", 1);
	}

	private static Dictionary<string, string> ParseColorDeclarations(string css) {
		return ColorDeclarationRegex.Matches(css)
			.GroupBy(match => match.Groups[1].Value, match => match.Groups[2].Value.Trim(), StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
	}

	private static (string Name, int Step)? ParsePaletteRef(string value) {
		if (value == null) {
			return null;
		}
		Match match = PaletteRefRegex.Match(value);
		return match.Success
			? (match.Groups[1].Value, int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
			: null;
	}

	private static void GuardFilledTemplate(string css, PaletteSet palettes) {
		if (css.Contains("<%", StringComparison.Ordinal)) {
			throw new InvalidOperationException(
				"Theme template fill left an unresolved '<%…%>' placeholder — the template does not match the expected contract.");
		}
		foreach (string name in GeneratedPaletteNames) {
			foreach (int step in PaletteGenerator.Steps) {
				Regex regex = GetDeclarationRegex($@"--crt-palette-{name}-{step}:\s*([^;]+);");
				Match match = regex.Match(css);
				if (!match.Success || match.Groups[1].Value.Trim() != palettes[name][step]) {
					throw new InvalidOperationException(
						$"Theme template fill did not substitute '--crt-palette-{name}-{step}' — the template is missing the expected declaration.");
				}
			}
		}
	}
}
