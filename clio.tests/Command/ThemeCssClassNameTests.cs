namespace Clio.Tests.Command;

using System.Text.RegularExpressions;
using Clio.Command.Theming;
using FluentAssertions;
using NUnit.Framework;

/// <summary>
/// Unit coverage for <see cref="ThemeCssClassName"/>: deterministic slugification of a human caption into a
/// valid css-class-name, and the resolve rule requiring at least one of css-class-name / caption.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ThemeCssClassNameTests {

	private static readonly Regex CssClassNamePattern = new("^[A-Za-z][A-Za-z0-9_-]*$");

	[Test]
	[Description("Slugify lowercases a multi-word caption and joins words with single hyphens.")]
	public void Slugify_ShouldLowercaseAndHyphenate_WhenGivenWords() {
		// Act / Assert
		ThemeCssClassName.Slugify("Ocean Blue").Should().Be("ocean-blue", because: "spaces become a single hyphen and the result is lowercased");
		ThemeCssClassName.Slugify("Acme  Brand!! 2025").Should().Be("acme-brand-2025", because: "runs of non-alphanumerics collapse to one hyphen");
	}

	[Test]
	[Description("Slugify prefixes a letter when the caption starts with a digit (the class name must start with a letter).")]
	public void Slugify_ShouldPrefixLetter_WhenStartsWithDigit() {
		// Act
		string slug = ThemeCssClassName.Slugify("2025 Theme");

		// Assert
		slug.Should().Be("t-2025-theme", because: "a leading digit is invalid, so a letter prefix is added");
		CssClassNamePattern.IsMatch(slug).Should().BeTrue(because: "the slug must satisfy the css-class-name contract");
	}

	[Test]
	[Description("Slugify falls back to 'theme' when the caption has no usable alphanumerics.")]
	public void Slugify_ShouldFallBackToTheme_WhenNoAlphanumerics() {
		// Act / Assert
		ThemeCssClassName.Slugify("!!!").Should().Be("theme", because: "nothing usable remains, so a safe default is used");
	}

	[Test]
	[Description("Slugify returns null for an empty or whitespace caption (nothing to derive from).")]
	public void Slugify_ShouldReturnNull_WhenCaptionEmpty() {
		// Act / Assert
		ThemeCssClassName.Slugify(null).Should().BeNull(because: "a null caption cannot be slugified");
		ThemeCssClassName.Slugify("   ").Should().BeNull(because: "a whitespace caption cannot be slugified");
	}

	[Test]
	[Description("Slugify caps the result at 100 characters without leaving a trailing hyphen.")]
	public void Slugify_ShouldCapAtHundred_WhenCaptionLong() {
		// Act
		string slug = ThemeCssClassName.Slugify(new string('a', 250));

		// Assert
		slug.Length.Should().BeLessThanOrEqualTo(100, because: "the css-class-name contract caps at 100 characters");
		CssClassNamePattern.IsMatch(slug).Should().BeTrue(because: "the capped slug must still satisfy the contract");
	}

	[Test]
	[Description("TryResolve returns the explicit css-class-name as-is when one is supplied.")]
	public void TryResolve_ShouldReturnExplicit_WhenCssClassNameGiven() {
		// Act
		bool ok = ThemeCssClassName.TryResolve("MyClass", "ignored caption", out string resolved, out string error);

		// Assert
		ok.Should().BeTrue(because: "an explicit css-class-name is accepted");
		resolved.Should().Be("MyClass", because: "an explicit value is used verbatim, not re-derived");
		error.Should().BeNull(because: "success carries no error");
	}

	[Test]
	[Description("TryResolve derives the css-class-name from the caption when none is supplied.")]
	public void TryResolve_ShouldSlugifyCaption_WhenCssClassNameEmpty() {
		// Act
		bool ok = ThemeCssClassName.TryResolve("", "Ocean Blue", out string resolved, out string error);

		// Assert
		ok.Should().BeTrue(because: "a caption is enough to derive a css-class-name");
		resolved.Should().Be("ocean-blue", because: "the caption is slugified");
		error.Should().BeNull(because: "success carries no error");
	}

	[Test]
	[Description("TryResolve fails with a clear 'at least one required' message when both css-class-name and caption are empty.")]
	public void TryResolve_ShouldFail_WhenBothEmpty() {
		// Act
		bool ok = ThemeCssClassName.TryResolve("  ", null, out string resolved, out string error);

		// Assert
		ok.Should().BeFalse(because: "there is nothing to name the theme when both inputs are empty");
		resolved.Should().BeNull(because: "no class name could be resolved");
		error.Should().Contain("at least one is required", because: "the message must state a caption or a css-class-name is required");
	}
}
