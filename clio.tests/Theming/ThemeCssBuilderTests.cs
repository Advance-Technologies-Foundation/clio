using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Theming;

/// <summary>
/// Tests for <see cref="ThemeCssBuilder"/>: the full-pipeline golden (<c>Build</c> equals the committed
/// golden output hex-for-hex on the same template), the contains/throws contract, the regex-replacement
/// edge cases, the post-fill guard, and the 1 MiB advisory.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Theming")]
public sealed class ThemeCssBuilderTests {

	private const string TemplateRelativePath = "Theming/Fixtures/theme.css.tpl";
	private const string GoldenRelativePath = "Theming/Fixtures/theme-css-golden.json";

	private static string Template() {
		return File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, TemplateRelativePath));
	}

	private static IThemeCssBuilder Builder() {
		return new ThemeCssBuilder();
	}

	[Test]
	[Description("Build reproduces the golden output exactly for every case (the full-pipeline bit-exact gate).")]
	public void Build_ShouldMatchTsGolden_ForEveryBuilderFixtureCase() {
		// Arrange
		string template = Template();
		IReadOnlyList<GoldenCase> cases = LoadGolden();
		List<string> mismatches = new();

		// Act
		foreach (GoldenCase golden in cases) {
			string actual = Builder().Build(template, golden.Input.ToBuildThemeInput());
			if (actual != golden.Css) {
				mismatches.Add($"case '{golden.Name}': output differs from the golden (lengths {actual.Length} vs {golden.Css.Length})");
			}
		}

		// Assert
		mismatches.Should().BeEmpty(
			because: "Build must reproduce the golden output exactly; divergent cases:\n" + string.Join("\n", mismatches));
	}

	[Test]
	[Description("Build applies the theme class, generated palette stops, finalized text tokens, and omits the @import for the default font.")]
	public void Build_ShouldApplyClassPalettesAndTokens_ForDefaultInput() {
		// Act
		string css = Builder().Build(Template(), new BuildThemeInput { Primary = "#004fd6", ThemeCssClass = "MyTheme" });

		// Assert
		css.Should().Contain(".MyTheme {", because: "the theme class fills the selector");
		css.Should().Contain("--crt-palette-primary-500: #004fd6;", because: "the primary -500 stop is substituted");
		css.Should().Contain("--crt-palette-primary-900: #001c5a;", because: "the darkest primary stop is substituted");
		css.Should().Contain("--crt-color-text-accent: var(--crt-palette-accent-600);", because: "text-accent finalizes to the first AA-passing accent stop");
		css.Should().Contain("--crt-color-text-link-hover: var(--crt-palette-primary-600);", because: "link-hover is one stop darker than link");
		css.Should().Contain("--crt-color-text-on-primary: var(--crt-color-base-light);", because: "white passes over the dark primary background");
		css.Should().NotContain("<%", because: "no template placeholder may survive");
		css.Should().NotContain("@import", because: "the default Montserrat font emits no Google Fonts import");
	}

	[Test]
	[Description("Build adds the Google Fonts @import and rewrites the family for a custom font.")]
	public void Build_ShouldAddImportAndFamily_ForCustomFont() {
		// Act
		string css = Builder().Build(
			Template(),
			new BuildThemeInput {
				Primary = "#004fd6",
				ThemeCssClass = "T",
				Fonts = new FontsInput("Inter", "Inter", new[] { 400, 600 }),
			});

		// Assert
		css.Should().Contain("@import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;600&display=swap');",
			because: "a custom font prepends its Google Fonts import");
		css.Should().Contain("--crt-font-family-heading: 'Inter', sans-serif;",
			because: "the heading family is rewritten to the custom font");
	}

	[Test]
	[Description("Build imports only the customized family when just the heading font is overridden — the untouched default body font is not fetched from Google Fonts.")]
	public void Build_ShouldImportOnlyHeadingFamily_WhenOnlyHeadingFontOverridden() {
		// Act
		string css = Builder().Build(
			Template(),
			new BuildThemeInput {
				Primary = "#004fd6",
				ThemeCssClass = "T",
				Fonts = new FontsInput("Playfair Display", null),
			});

		// Assert
		css.Should().Contain("family=Playfair+Display", because: "the customized heading family is imported");
		css.Should().NotContain("family=Montserrat", because: "the untouched default body family must not be fetched from Google Fonts");
		css.Should().Contain("--crt-font-family-heading: 'Playfair Display', sans-serif;",
			because: "the heading family is rewritten to the custom font");
		css.Should().Contain("--crt-font-family-body: 'Montserrat', sans-serif;",
			because: "the body family stays on the platform default");
	}

	[Test]
	[Description("Build imports only the customized family when just the body font is overridden — the untouched default heading font is not fetched from Google Fonts.")]
	public void Build_ShouldImportOnlyBodyFamily_WhenOnlyBodyFontOverridden() {
		// Act
		string css = Builder().Build(
			Template(),
			new BuildThemeInput {
				Primary = "#004fd6",
				ThemeCssClass = "T",
				Fonts = new FontsInput(null, "Inter"),
			});

		// Assert
		css.Should().Contain("family=Inter", because: "the customized body family is imported");
		css.Should().NotContain("family=Montserrat", because: "the untouched default heading family must not be fetched from Google Fonts");
		css.Should().Contain("--crt-font-family-body: 'Inter', sans-serif;",
			because: "the body family is rewritten to the custom font");
		css.Should().Contain("--crt-font-family-heading: 'Montserrat', sans-serif;",
			because: "the heading family stays on the platform default");
	}

	[Test]
	[Description("Build throws the documented validation errors for missing/invalid primary, class, and font family.")]
	public void Build_ShouldThrow_ForTheDocumentedInvalidInputs() {
		// Act / Assert
		Build(new BuildThemeInput()).Should().Throw<ArgumentException>().WithMessage("PRIMARY_REQUIRED*",
			because: "a primary colour is required");
		Build(new BuildThemeInput { Primary = "#004fd6" }).Should().Throw<ArgumentException>().WithMessage("THEME_CSS_CLASS_REQUIRED*",
			because: "a themeCssClass is required");
		Build(new BuildThemeInput { Primary = "#004fd6", ThemeCssClass = "T {} body" }).Should().Throw<ArgumentException>().WithMessage("INVALID_THEME_CSS_CLASS*",
			because: "a class with spaces/braces would break the selector");
		Build(new BuildThemeInput { Primary = "#004fd6", ThemeCssClass = "1Theme" }).Should().Throw<ArgumentException>().WithMessage("INVALID_THEME_CSS_CLASS*",
			because: "a class must not start with a digit");
		Build(new BuildThemeInput { Primary = "#004fd6", ThemeCssClass = "T", Fonts = new FontsInput("Evil'; }", "Evil'; }") })
			.Should().Throw<ArgumentException>().WithMessage("INVALID_FONT_FAMILY*",
				because: "a family with quotes/braces is rejected");
	}

	[Test]
	[Description("Build rejects a themeCssClass with a trailing newline (the class pattern is anchored to the absolute end of the string).")]
	public void Build_ShouldRejectTrailingNewlineClass_BecausePatternUsesEndOfStringAnchor() {
		// Act / Assert
		Build(new BuildThemeInput { Primary = "#004fd6", ThemeCssClass = "Foo\n" })
			.Should().Throw<ArgumentException>().WithMessage("INVALID_THEME_CSS_CLASS*",
				because: "the class pattern is anchored to the absolute end of the string, so a trailing newline is rejected");
	}

	[Test]
	[Description("Palette substitution replaces only the FIRST matching declaration when the template has a duplicate.")]
	public void Build_ShouldReplaceFirstPaletteOccurrenceOnly_WhenTemplateHasDuplicate() {
		// Arrange — duplicate the primary-500 line; build with a DIFFERENT primary so the generated value is distinguishable.
		string duplicated = Template().Replace(
			"--crt-palette-primary-500: #004fd6;",
			"--crt-palette-primary-500: #004fd6;\r\n\t--crt-palette-primary-500: #004fd6;");

		// Act
		string css = Builder().Build(duplicated, new BuildThemeInput { Primary = "#e91e63", ThemeCssClass = "Dup" });

		// Assert
		css.Should().Contain("--crt-palette-primary-500: #e91e63;",
			because: "the first occurrence is substituted with the generated primary");
		css.Should().Contain("--crt-palette-primary-500: #004fd6;",
			because: "the duplicate (second) occurrence is left untouched — only the first match is replaced");
	}

	[Test]
	[Description("Build fails loudly when the template leaves an unresolved placeholder (post-fill guard).")]
	public void Build_ShouldThrowInvalidOperation_WhenTemplateLeavesPlaceholder() {
		// Arrange
		string drifted = Template() + "\r\n.extra { content: '<%foo%>'; }\r\n";

		// Act / Assert
		Build(new BuildThemeInput { Primary = "#004fd6", ThemeCssClass = "T" }, drifted)
			.Should().Throw<InvalidOperationException>().WithMessage("*placeholder*",
				because: "an unresolved <%…%> means the template drifted from the contract");
	}

	[Test]
	[Description("Build fails loudly when the template is missing a palette declaration the builder must substitute (post-fill guard).")]
	public void Build_ShouldThrowInvalidOperation_WhenPaletteStopIsMissing() {
		// Arrange — remove a required palette declaration.
		string drifted = Template().Replace("--crt-palette-primary-500: #004fd6;", string.Empty);

		// Act / Assert
		Build(new BuildThemeInput { Primary = "#004fd6", ThemeCssClass = "T" }, drifted)
			.Should().Throw<InvalidOperationException>().WithMessage("*primary-500*",
				because: "a missing palette declaration means the producer template no longer matches the builder contract");
	}

	[Test]
	[Description("The fixture template declares every token the builder rewrites — dropping one fails here (template/builder contract).")]
	public void FixtureTemplate_ShouldContainEveryTokenTheBuilderRewrites() {
		// Arrange
		string template = Template();

		// Assert
		template.Should().Contain("<%themeCssClass%>", because: "the builder fills the theme class placeholder");
		template.Should().Contain("Creatio custom theme template", because: "the builder strips the leading header comment by this phrase");
		foreach (string name in new[] { "primary", "secondary", "accent", "success", "error" }) {
			foreach (int step in new[] { 10, 25, 50, 100, 200, 300, 400, 500, 600, 700, 800, 900 }) {
				template.Should().Contain($"--crt-palette-{name}-{step}:",
					because: $"the builder substitutes the --crt-palette-{name}-{step} declaration, so the template must declare it");
			}
		}
	}

	private static Action Build(BuildThemeInput options, string template = null) {
		return () => new ThemeCssBuilder().Build(template ?? Template(), options);
	}

	private static IReadOnlyList<GoldenCase> LoadGolden() {
		string goldenPath = Path.Combine(TestContext.CurrentContext.TestDirectory, GoldenRelativePath);
		File.Exists(goldenPath).Should().BeTrue(because: $"the builder golden must be copied to '{goldenPath}'");
		List<GoldenCase> cases = JsonSerializer.Deserialize<List<GoldenCase>>(File.ReadAllText(goldenPath));
		cases.Should().NotBeNullOrEmpty(because: "the builder golden must deserialise into cases");
		return cases;
	}

	private sealed record GoldenCase {
		[JsonPropertyName("name")] public string Name { get; init; }
		[JsonPropertyName("input")] public GoldenInput Input { get; init; }
		[JsonPropertyName("css")] public string Css { get; init; }
	}

	private sealed record GoldenInput {
		[JsonPropertyName("primary")] public string Primary { get; init; }
		[JsonPropertyName("secondary")] public string Secondary { get; init; }
		[JsonPropertyName("accent")] public string Accent { get; init; }
		[JsonPropertyName("success")] public string Success { get; init; }
		[JsonPropertyName("error")] public string Error { get; init; }
		[JsonPropertyName("themeCssClass")] public string ThemeCssClass { get; init; }
		[JsonPropertyName("fonts")] public GoldenFonts Fonts { get; init; }

		internal BuildThemeInput ToBuildThemeInput() {
			return new() {
				Primary = Primary,
				Secondary = Secondary,
				Accent = Accent,
				Success = Success,
				Error = Error,
				ThemeCssClass = ThemeCssClass,
				Fonts = Fonts == null ? null : new FontsInput(Fonts.Heading, Fonts.Body, Fonts.Weights),
			};
		}
	}

	private sealed record GoldenFonts {
		[JsonPropertyName("heading")] public string Heading { get; init; }
		[JsonPropertyName("body")] public string Body { get; init; }
		[JsonPropertyName("weights")] public int[] Weights { get; init; }
	}
}
