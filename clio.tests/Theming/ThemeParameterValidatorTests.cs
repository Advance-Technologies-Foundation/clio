namespace Clio.Tests.Theming;

using System;
using System.Text.RegularExpressions;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

/// <summary>
/// Unit coverage for <see cref="ThemeParameterValidator"/>: deterministic derivation and resolution of the
/// css-class-name, and validation of the theme id, caption, and css-content parameters.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Theming")]
public sealed class ThemeParameterValidatorTests {

	private static readonly Regex CssClassNamePattern = new("^[A-Za-z][A-Za-z0-9_-]*$");

	[Test]
	[Description("DeriveCssClassNameFromCaption lowercases a multi-word caption and joins words with single hyphens.")]
	public void DeriveCssClassNameFromCaption_ShouldLowercaseAndHyphenate_WhenGivenWords() {
		// Act / Assert
		ThemeParameterValidator.DeriveCssClassNameFromCaption("Ocean Blue").Should().Be("ocean-blue", because: "spaces become a single hyphen and the result is lowercased");
		ThemeParameterValidator.DeriveCssClassNameFromCaption("Acme  Brand!! 2025").Should().Be("acme-brand-2025", because: "runs of non-alphanumerics collapse to one hyphen");
	}

	[Test]
	[Description("DeriveCssClassNameFromCaption prefixes a letter when the caption starts with a digit (the class name must start with a letter).")]
	public void DeriveCssClassNameFromCaption_ShouldPrefixLetter_WhenStartsWithDigit() {
		// Act
		string cssClassName = ThemeParameterValidator.DeriveCssClassNameFromCaption("2025 Theme");

		// Assert
		cssClassName.Should().Be("t-2025-theme", because: "a leading digit is invalid, so a letter prefix is added");
		CssClassNamePattern.IsMatch(cssClassName).Should().BeTrue(because: "the derived name must satisfy the css-class-name contract");
	}

	[Test]
	[Description("DeriveCssClassNameFromCaption falls back to 'theme' when the caption has no usable alphanumerics.")]
	public void DeriveCssClassNameFromCaption_ShouldFallBackToTheme_WhenNoAlphanumerics() {
		// Act / Assert
		ThemeParameterValidator.DeriveCssClassNameFromCaption("!!!").Should().Be("theme", because: "nothing usable remains, so a safe default is used");
	}

	[Test]
	[Description("DeriveCssClassNameFromCaption returns null for an empty or whitespace caption (nothing to derive from).")]
	public void DeriveCssClassNameFromCaption_ShouldReturnNull_WhenCaptionEmpty() {
		// Act / Assert
		ThemeParameterValidator.DeriveCssClassNameFromCaption(null).Should().BeNull(because: "a null caption yields no css-class-name to derive");
		ThemeParameterValidator.DeriveCssClassNameFromCaption("   ").Should().BeNull(because: "a whitespace caption yields no css-class-name to derive");
	}

	[Test]
	[Description("DeriveCssClassNameFromCaption caps the result at 100 characters without leaving a trailing hyphen.")]
	public void DeriveCssClassNameFromCaption_ShouldCapAtHundred_WhenCaptionLong() {
		// Act
		string cssClassName = ThemeParameterValidator.DeriveCssClassNameFromCaption(new string('a', 250));

		// Assert
		cssClassName.Length.Should().BeLessThanOrEqualTo(100, because: "the css-class-name contract caps at 100 characters");
		CssClassNamePattern.IsMatch(cssClassName).Should().BeTrue(because: "the capped name must still satisfy the contract");
	}

	[Test]
	[Description("TryResolveCssClassName returns the explicit css-class-name as-is when one is supplied.")]
	public void TryResolveCssClassName_ShouldReturnExplicit_WhenCssClassNameGiven() {
		// Act
		bool ok = ThemeParameterValidator.TryResolveCssClassName("MyClass", "ignored caption", out string resolved, out string error);

		// Assert
		ok.Should().BeTrue(because: "an explicit css-class-name is accepted");
		resolved.Should().Be("MyClass", because: "an explicit value is used verbatim, not re-derived");
		error.Should().BeNull(because: "success carries no error");
	}

	[Test]
	[Description("TryResolveCssClassName derives the css-class-name from the caption when none is supplied.")]
	public void TryResolveCssClassName_ShouldDeriveFromCaption_WhenCssClassNameEmpty() {
		// Act
		bool ok = ThemeParameterValidator.TryResolveCssClassName("", "Ocean Blue", out string resolved, out string error);

		// Assert
		ok.Should().BeTrue(because: "a caption is enough to derive a css-class-name");
		resolved.Should().Be("ocean-blue", because: "the css-class-name is derived from the caption");
		error.Should().BeNull(because: "success carries no error");
	}

	[Test]
	[Description("TryResolveCssClassName fails with a clear 'at least one required' message when both css-class-name and caption are empty.")]
	public void TryResolveCssClassName_ShouldFail_WhenBothEmpty() {
		// Act
		bool ok = ThemeParameterValidator.TryResolveCssClassName("  ", null, out string resolved, out string error);

		// Assert
		ok.Should().BeFalse(because: "there is nothing to name the theme when both inputs are empty");
		resolved.Should().BeNull(because: "no class name could be resolved");
		error.Should().Contain("at least one is required", because: "the message must state a caption or a css-class-name is required");
	}

	[Test]
	[Description("TryValidateCssClassName accepts a valid name and reports the field-specific message for empty, over-long, and pattern-violating names — sharing the pass/fail decision with IsValidCssClassName.")]
	public void TryValidateCssClassName_ShouldMirrorSharedRule_ForEachFailureKind() {
		// Act
		bool okValid = ThemeParameterValidator.TryValidateCssClassName("MyTheme", out string validError);
		bool okEmpty = ThemeParameterValidator.TryValidateCssClassName("  ", out string emptyError);
		bool okLong = ThemeParameterValidator.TryValidateCssClassName("a" + new string('b', 100), out string longError);
		bool okPattern = ThemeParameterValidator.TryValidateCssClassName("1bad", out string patternError);

		// Assert
		okValid.Should().BeTrue(because: "a name satisfying the shared rule passes");
		validError.Should().BeNull(because: "success carries no error");
		okEmpty.Should().BeFalse(because: "a blank css-class-name is rejected");
		emptyError.Should().Contain("required", because: "the message must say the field is required");
		okLong.Should().BeFalse(because: "a 101-character name exceeds the shared length limit");
		longError.Should().Contain("at most 100", because: "the message must state the shared length limit");
		okPattern.Should().BeFalse(because: "a name starting with a digit violates the shared character rule");
		patternError.Should().Contain("hyphen, underscore only", because: "the message must spell out the full character rule, matching TryResolveCssClassName");
	}

	[Test]
	[Description("TryResolveCssClassName rejects an explicit css-class-name that could escape the theme directory or otherwise violate the identifier rule, because the resolved value becomes a filesystem path segment.")]
	[TestCase("../evil", TestName = "TryResolveCssClassName rejects a parent-directory traversal css-class-name")]
	[TestCase("a/b", TestName = "TryResolveCssClassName rejects a forward-slash css-class-name")]
	[TestCase("a\\b", TestName = "TryResolveCssClassName rejects a back-slash css-class-name")]
	[TestCase("..", TestName = "TryResolveCssClassName rejects a bare parent-directory css-class-name")]
	[TestCase("1bad", TestName = "TryResolveCssClassName rejects a css-class-name not starting with a letter")]
	[TestCase("has space", TestName = "TryResolveCssClassName rejects a css-class-name with whitespace")]
	public void TryResolveCssClassName_ShouldFail_WhenExplicitCssClassNameViolatesTheRule(string cssClassName) {
		// Act
		bool ok = ThemeParameterValidator.TryResolveCssClassName(cssClassName, "ignored caption", out string resolved, out string error);

		// Assert
		ok.Should().BeFalse(because: $"'{cssClassName}' is not a valid css-class-name and must not become a path segment as-is");
		resolved.Should().BeNull(because: "a malformed class name must not be resolved");
		error.Should().Contain("css-class-name", because: "the failure must name the exact field the caller has to fix");
	}

	[Test]
	[Description("TryResolveCssClassName rejects an explicit css-class-name longer than the shared maximum length.")]
	public void TryResolveCssClassName_ShouldFail_WhenExplicitCssClassNameTooLong() {
		// Arrange
		string tooLong = "a" + new string('b', 100);

		// Act
		bool ok = ThemeParameterValidator.TryResolveCssClassName(tooLong, "ignored caption", out string resolved, out string error);

		// Assert
		ok.Should().BeFalse(because: "a css-class-name over the shared length limit is invalid");
		resolved.Should().BeNull(because: "an over-length class name must not be resolved");
		error.Should().Contain("css-class-name", because: "the failure must name the exact field the caller has to fix");
	}

	[Test]
	[TestCase("ok-id_1", true, TestName = "TryValidateId accepts a valid id")]
	[TestCase("bad id", false, TestName = "TryValidateId rejects a space")]
	[TestCase("bad.id", false, TestName = "TryValidateId rejects a dot")]
	[TestCase("ok-id\n", false, TestName = "TryValidateId rejects a trailing newline")]
	[TestCase("", false, TestName = "TryValidateId rejects empty")]
	[Description("Validates the theme id against ^[A-Za-z0-9_-]+$ and the length cap.")]
	public void TryValidateId_ShouldEnforceContract_WhenGivenId(string id, bool expected) {
		// Act
		bool ok = ThemeParameterValidator.TryValidateId(id, out string error);

		// Assert
		ok.Should().Be(expected, because: "the id must match the server regex and length contract");
		if (!expected) {
			error.Should().NotBeNullOrWhiteSpace(because: "a rejected id must carry a diagnostic");
		}
	}

	[Test]
	[Description("Accepts an auto-generated UUID v4 ('D' format) as a valid id.")]
	public void TryValidateId_ShouldAccept_WhenGivenGuidDFormat() {
		// Arrange
		string id = Guid.NewGuid().ToString("D");

		// Act
		bool ok = ThemeParameterValidator.TryValidateId(id, out string _);

		// Assert
		ok.Should().BeTrue(because: "a UUID in 'D' format contains only hex digits and hyphens, matching ^[A-Za-z0-9_-]+$");
	}

	[Test]
	[Description("TryValidateCaption accepts a non-empty caption within the length cap and rejects empty or over-long ones.")]
	[TestCase("Ocean", true, TestName = "TryValidateCaption accepts a normal caption")]
	[TestCase("", false, TestName = "TryValidateCaption rejects empty")]
	[TestCase("   ", false, TestName = "TryValidateCaption rejects whitespace")]
	public void TryValidateCaption_ShouldEnforceContract_WhenGivenCaption(string caption, bool expected) {
		// Act
		bool ok = ThemeParameterValidator.TryValidateCaption(caption, out string error);

		// Assert
		ok.Should().Be(expected, because: "a caption is required and bounded in length");
		if (!expected) {
			error.Should().Contain("caption", because: "a rejected caption must name the offending field");
		}
	}

	[Test]
	[Description("TryValidateCaption rejects a caption longer than 250 characters.")]
	public void TryValidateCaption_ShouldFail_WhenCaptionTooLong() {
		// Arrange
		string tooLong = new('a', 251);

		// Act
		bool ok = ThemeParameterValidator.TryValidateCaption(tooLong, out string error);

		// Assert
		ok.Should().BeFalse(because: "a caption over 250 characters exceeds the contract");
		error.Should().Contain("at most", because: "the diagnostic must state the length cap");
	}

	[Test]
	[Description("TryValidateCssContent requires the content to be present and non-empty; null, empty, and whitespace-only values are all rejected.")]
	public void TryValidateCssContent_ShouldEnforcePresence_WhenGivenContent() {
		// Act
		bool nullContent = ThemeParameterValidator.TryValidateCssContent(null, out string nullError);
		bool emptyContent = ThemeParameterValidator.TryValidateCssContent(string.Empty, out string emptyError);
		bool whitespaceContent = ThemeParameterValidator.TryValidateCssContent("   ", out string whitespaceError);
		bool realContent = ThemeParameterValidator.TryValidateCssContent(".ocean-theme{}", out string realError);

		// Assert
		nullContent.Should().BeFalse(because: "absent CSS content is invalid");
		nullError.Should().Contain("required", because: "the diagnostic must state the content is required");
		emptyContent.Should().BeFalse(because: "an explicitly empty string carries no CSS");
		emptyError.Should().Contain("cannot be empty", because: "the diagnostic must state that empty CSS is rejected");
		whitespaceContent.Should().BeFalse(because: "whitespace-only content is as meaningless as empty content");
		whitespaceError.Should().Contain("cannot be empty", because: "the diagnostic must state that empty CSS is rejected");
		realContent.Should().BeTrue(because: "well-formed content within the size limit is valid");
		realError.Should().BeNull(because: "valid content carries no error");
	}

	[Test]
	[Description("TryValidateCssContent rejects content larger than the 1 MiB limit.")]
	public void TryValidateCssContent_ShouldFail_WhenContentExceedsLimit() {
		// Arrange
		string oversized = new('a', ThemeParameterValidator.MaxCssContentBytes + 1);

		// Act
		bool ok = ThemeParameterValidator.TryValidateCssContent(oversized, out string error);

		// Assert
		ok.Should().BeFalse(because: "content over 1 MiB exceeds the server contract");
		error.Should().Contain("1 MiB", because: "the diagnostic must state the size cap");
	}
}
