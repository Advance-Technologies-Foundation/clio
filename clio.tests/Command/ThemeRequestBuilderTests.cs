namespace Clio.Tests.Command;

using System;
using System.IO;
using System.Text;
using Clio.Command;
using Clio.Command.Theming;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public class ThemeRequestBuilderTests
{
	[Test]
	[Category("Unit")]
	[Description("Returns a failure when neither --css-content nor --css-content-file is supplied, without touching the filesystem.")]
	public void TryResolveCssContent_ShouldFail_WhenNoCssInputSupplied() {
		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(null, null, out string resolved, out string error);

		// Assert
		ok.Should().BeFalse(because: "a theme requires CSS from exactly one of the two inputs");
		resolved.Should().BeNull(because: "no CSS could be resolved");
		error.Should().NotBeNullOrWhiteSpace(because: "the caller must learn which input to provide");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure when both --css-content and --css-content-file are supplied (mutually exclusive).")]
	public void TryResolveCssContent_ShouldFail_WhenBothCssInputsSupplied() {
		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(".x{}", "theme.css", out string _, out string error);

		// Assert
		ok.Should().BeFalse(because: "the inline and file inputs are mutually exclusive");
		error.Should().Contain("not both", because: "the diagnostic must explain the mutual exclusion");
	}

	[Test]
	[Category("Unit")]
	[Description("Treats an explicitly empty --css-content as valid empty CSS (present, not absent).")]
	public void TryResolveCssContent_ShouldReturnEmptyCss_WhenInlineContentIsEmptyString() {
		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(string.Empty, null, out string resolved, out string error);

		// Assert
		ok.Should().BeTrue(because: "an empty string means the flag was supplied with empty CSS, which is allowed");
		resolved.Should().BeEmpty(because: "the resolved CSS is the supplied empty string");
		error.Should().BeNull(because: "an explicitly empty inline value is not an error");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves inline CSS verbatim when only --css-content is supplied.")]
	public void TryResolveCssContent_ShouldReturnInlineCss_WhenOnlyInlineSupplied() {
		// Arrange
		const string css = ".freedom-theme{--crt-x:1}";

		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(css, null, out string resolved, out string error);

		// Assert
		ok.Should().BeTrue(because: "a single inline input resolves cleanly");
		resolved.Should().Be(css, because: "the inline value must be forwarded verbatim");
		error.Should().BeNull(because: "a valid resolution carries no error");
	}

	[Test]
	[Category("Unit")]
	[Description("Passes validation for a well-formed id/caption/cssClassName/cssContent set.")]
	public void TryValidateRequest_ShouldSucceed_WhenAllFieldsValid() {
		// Arrange
		ThemeRequest request = new() { Id = "ocean-theme", Caption = "Ocean", CssClassName = "ocean-theme", CssContent = ".ocean-theme{}" };

		// Act
		bool ok = ThemeRequestBuilder.TryValidateRequest(request, out string error);

		// Assert
		ok.Should().BeTrue(because: "every field satisfies the contract");
		error.Should().BeNull(because: "a valid set carries no error");
	}

	[Test]
	[Category("Unit")]
	[TestCase("", "cap", "cls", ".x{}", TestName = "TryValidateRequest rejects empty id")]
	[TestCase("id", "", "cls", ".x{}", TestName = "TryValidateRequest rejects empty caption")]
	[TestCase("id", "cap", "", ".x{}", TestName = "TryValidateRequest rejects empty cssClassName")]
	[TestCase("id", "cap", "1bad", ".x{}", TestName = "TryValidateRequest rejects cssClassName not starting with a letter")]
	[Description("Fails validation when a required field is missing or a format rule is violated.")]
	public void TryValidateRequest_ShouldFail_WhenFieldInvalid(string id, string caption, string cssClassName, string cssContent) {
		// Arrange
		ThemeRequest request = new() { Id = id, Caption = caption, CssClassName = cssClassName, CssContent = cssContent };

		// Act
		bool ok = ThemeRequestBuilder.TryValidateRequest(request, out string error);

		// Assert
		ok.Should().BeFalse(because: "an invalid field must fail the contract check");
		error.Should().NotBeNullOrWhiteSpace(because: "the failure must point at the offending field");
	}

	[Test]
	[Category("Unit")]
	[Description("Allows empty CSS content (empty string is valid; only null is rejected).")]
	public void TryValidateRequest_ShouldAllowEmptyCssContent_WhenContentIsEmptyString() {
		// Arrange
		ThemeRequest request = new() { Id = "id", Caption = "Caption", CssClassName = "css-cls", CssContent = string.Empty };

		// Act
		bool ok = ThemeRequestBuilder.TryValidateRequest(request, out string error);

		// Assert
		ok.Should().BeTrue(because: "the contract allows empty CSS content (empty string), only null is rejected");
		error.Should().BeNull(because: "empty CSS is valid");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects CSS content larger than the 1 MiB cap before any HTTP call.")]
	public void TryValidateRequest_ShouldFail_WhenCssContentExceedsOneMebibyte() {
		// Arrange
		string oversized = new('a', ThemeParameterValidator.MaxCssContentBytes + 1);
		ThemeRequest request = new() { Id = "id", Caption = "Caption", CssClassName = "css-cls", CssContent = oversized };

		// Act
		bool ok = ThemeRequestBuilder.TryValidateRequest(request, out string error);

		// Assert
		ok.Should().BeFalse(because: "content above 1 MiB violates the server contract");
		error.Should().Contain("1 MiB", because: "the diagnostic must name the size limit");
	}

	[Test]
	[Category("Unit")]
	[TestCase("ocean-theme", "Ocean", TestName = "Derive strips trailing theme word and title-cases")]
	[TestCase("my-cool-theme", "My Cool", TestName = "Derive title-cases multiple words")]
	[TestCase("freedom", "Freedom", TestName = "Derive single word without theme suffix")]
	[TestCase("my--cool-theme", "My Cool", TestName = "Derive collapses repeated separators")]
	[TestCase("x", "X", TestName = "Derive single character")]
	[TestCase("theme", "Theme", TestName = "Derive keeps a lone theme word")]
	[TestCase("ocean_theme", "Ocean", TestName = "Derive strips an underscore theme suffix")]
	[Description("Derives a human-readable caption from a CSS class name by dropping a trailing 'theme' word and title-casing.")]
	public void DeriveCaptionFromCssClassName_ShouldProduceTitleCasedCaption_WhenGivenClassName(string cssClassName, string expected) {
		// Act
		string caption = ThemeRequestBuilder.DeriveCaptionFromCssClassName(cssClassName);

		// Assert
		caption.Should().Be(expected, because: "the caption must be derived from the class name by dropping a trailing 'theme' word and title-casing");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an empty string when the css class name is blank.")]
	public void DeriveCaptionFromCssClassName_ShouldReturnEmpty_WhenClassNameBlank() {
		// Act
		string caption = ThemeRequestBuilder.DeriveCaptionFromCssClassName("   ");

		// Assert
		caption.Should().BeEmpty(because: "a blank class name yields no caption");
	}
}

[TestFixture]
[Property("Module", "Command")]
public class ThemeRequestBuilderFileTests
{
	private string _tempFile;

	[TearDown]
	public void TearDown() {
		if (_tempFile is not null && File.Exists(_tempFile)) {
			File.Delete(_tempFile);
		}
		_tempFile = null;
	}

	[Test]
	[Category("Integration")]
	[Description("Reads CSS from a UTF-8 file when only --css-content-file is supplied.")]
	public void TryResolveCssContent_ShouldReadFile_WhenOnlyFileSupplied() {
		// Arrange
		const string css = ".freedom-theme{--crt-font-family:'Montserrat'}";
		_tempFile = Path.Combine(Path.GetTempPath(), $"clio-theme-{Guid.NewGuid():N}.css");
		File.WriteAllText(_tempFile, css, Encoding.UTF8);

		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(null, _tempFile, out string resolved, out string error);

		// Assert
		ok.Should().BeTrue(because: "an existing UTF-8 CSS file resolves cleanly");
		resolved.Should().Be(css, because: "the file content must be read verbatim as UTF-8");
		error.Should().BeNull(because: "a successful read carries no error");
	}

	[Test]
	[Category("Integration")]
	[Description("Fails fast when --css-content-file points at a non-existent path, without resolving any content.")]
	public void TryResolveCssContent_ShouldFail_WhenFileDoesNotExist() {
		// Arrange
		string missing = Path.Combine(Path.GetTempPath(), $"clio-theme-missing-{Guid.NewGuid():N}.css");

		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(null, missing, out string resolved, out string error);

		// Assert
		ok.Should().BeFalse(because: "a missing file cannot supply CSS");
		resolved.Should().BeNull(because: "nothing could be read");
		error.Should().Contain("not found", because: "the diagnostic must explain the file is missing");
	}

	[Test]
	[Category("Integration")]
	[Description("Fails fast when --css-content-file is larger than the 1 MiB cap, without reading the whole file into memory.")]
	public void TryResolveCssContent_ShouldFail_WhenFileExceedsOneMebibyte() {
		// Arrange
		_tempFile = Path.Combine(Path.GetTempPath(), $"clio-theme-oversized-{Guid.NewGuid():N}.css");
		File.WriteAllText(_tempFile, new string('a', ThemeParameterValidator.MaxCssContentBytes + 1), Encoding.UTF8);

		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(null, _tempFile, out string resolved, out string error);

		// Assert
		ok.Should().BeFalse(because: "a CSS file above 1 MiB must be rejected before it is loaded into memory");
		resolved.Should().BeNull(because: "an oversized file must not be read");
		error.Should().Contain("1 MiB", because: "the diagnostic must name the size limit");
	}
}
