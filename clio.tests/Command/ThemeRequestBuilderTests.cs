namespace Clio.Tests.Command;

using System.IO;
using Clio.Command;
using Clio.Command.Theming;
using Clio.Common;
using Clio.Theming;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class ThemeRequestBuilderTests
{
	[Test]
	[Category("Unit")]
	[Description("Returns a failure when neither --css-content nor --css-content-file is supplied, without touching the filesystem.")]
	public void TryResolveCssContent_ShouldFail_WhenNoCssInputSupplied() {
		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(Substitute.For<IFileSystem>(), null, null,
			out string resolved, out string error);

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
		bool ok = ThemeRequestBuilder.TryResolveCssContent(Substitute.For<IFileSystem>(), ".x{}", "theme.css",
			out string _, out string error);

		// Assert
		ok.Should().BeFalse(because: "the inline and file inputs are mutually exclusive");
		error.Should().Contain("not both", because: "the diagnostic must explain the mutual exclusion");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves an explicitly empty --css-content as a present input.")]
	public void TryResolveCssContent_ShouldResolveEmptyString_WhenInlineContentIsEmptyString() {
		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(Substitute.For<IFileSystem>(), string.Empty, null,
			out string resolved, out string error);

		// Assert
		ok.Should().BeTrue(because: "resolution only distinguishes present from absent inputs");
		resolved.Should().BeEmpty(because: "the resolved CSS is the supplied empty string");
		error.Should().BeNull(because: "resolution reports input-shape errors only");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves inline CSS verbatim when only --css-content is supplied.")]
	public void TryResolveCssContent_ShouldReturnInlineCss_WhenOnlyInlineSupplied() {
		// Arrange
		const string css = ".freedom-theme{--crt-x:1}";

		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(Substitute.For<IFileSystem>(), css, null,
			out string resolved, out string error);

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
	[Description("Rejects an explicitly empty CSS content string.")]
	public void TryValidateRequest_ShouldFail_WhenCssContentIsEmptyString() {
		// Arrange
		ThemeRequest request = new() { Id = "id", Caption = "Caption", CssClassName = "css-cls", CssContent = string.Empty };

		// Act
		bool ok = ThemeRequestBuilder.TryValidateRequest(request, out string error);

		// Assert
		ok.Should().BeFalse(because: "empty CSS content is invalid");
		error.Should().Contain("cannot be empty", because: "the diagnostic must state that empty CSS is rejected");
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
	private const string CssFilePath = "themes/custom.css";

	[Test]
	[Category("Unit")]
	[Description("Reads CSS from the file system when only --css-content-file is supplied.")]
	public void TryResolveCssContent_ShouldReadFile_WhenOnlyFileSupplied() {
		// Arrange
		const string css = ".freedom-theme{--crt-font-family:'Montserrat'}";
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile(CssFilePath).Returns(true);
		fileSystem.GetFileSize(CssFilePath).Returns(css.Length);
		fileSystem.ReadAllText(CssFilePath).Returns(css);

		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(fileSystem, null, CssFilePath,
			out string resolved, out string error);

		// Assert
		ok.Should().BeTrue(because: "an existing CSS file within the size cap resolves cleanly");
		resolved.Should().Be(css, because: "the file content must be read verbatim");
		error.Should().BeNull(because: "a successful read carries no error");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails fast when --css-content-file points at a non-existent path, without resolving any content.")]
	public void TryResolveCssContent_ShouldFail_WhenFileDoesNotExist() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile(CssFilePath).Returns(false);

		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(fileSystem, null, CssFilePath,
			out string resolved, out string error);

		// Assert
		ok.Should().BeFalse(because: "a missing file cannot supply CSS");
		resolved.Should().BeNull(because: "nothing could be read");
		error.Should().Contain("not found", because: "the diagnostic must explain the file is missing");
		fileSystem.DidNotReceive().ReadAllText(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Fails fast when --css-content-file is larger than the 1 MiB cap, without reading the file into memory.")]
	public void TryResolveCssContent_ShouldFail_WhenFileExceedsOneMebibyte() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile(CssFilePath).Returns(true);
		fileSystem.GetFileSize(CssFilePath).Returns(ThemeParameterValidator.MaxCssContentBytes + 1);

		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(fileSystem, null, CssFilePath,
			out string resolved, out string error);

		// Assert
		ok.Should().BeFalse(because: "a CSS file above 1 MiB must be rejected before it is loaded into memory");
		resolved.Should().BeNull(because: "an oversized file must not be read");
		error.Should().Contain("1 MiB", because: "the diagnostic must name the size limit");
		fileSystem.DidNotReceive().ReadAllText(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Fails with a friendly diagnostic when reading the CSS file throws an I/O error (e.g. the file is locked).")]
	public void TryResolveCssContent_ShouldFail_WhenFileReadThrowsIoError() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile(CssFilePath).Returns(true);
		fileSystem.GetFileSize(CssFilePath).Returns(10);
		fileSystem.ReadAllText(CssFilePath).Returns(_ => throw new IOException("locked"));

		// Act
		bool ok = ThemeRequestBuilder.TryResolveCssContent(fileSystem, null, CssFilePath,
			out string resolved, out string error);

		// Assert
		ok.Should().BeFalse(because: "an unreadable file cannot supply CSS");
		resolved.Should().BeNull(because: "nothing could be read");
		error.Should().Contain("Could not read", because: "the diagnostic must explain the read failure instead of crashing");
	}
}
