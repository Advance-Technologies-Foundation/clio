using System.IO;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUpdateBodyLoaderTests {

	[Test]
	[Description("Body already populated: loader is a no-op even when BodyFile is also set.")]
	public void TryLoadBodyFromFile_WhenBodyAlreadySet_IsNoOp() {
		// Arrange
		PageUpdateOptions options = new() {
			Body = "inline-body",
			BodyFile = "/this/path/does/not/exist.json"
		};

		// Act
		(bool ok, string error) = PageUpdateBodyLoader.TryLoadBodyFromFile(options);

		// Assert
		ok.Should().BeTrue(because: "an inline body must take precedence and short-circuit the loader without touching the filesystem");
		error.Should().BeNull(because: "the no-op path must not produce an error");
		options.Body.Should().Be("inline-body", because: "the loader must not overwrite an inline body");
	}

	[Test]
	[Description("Both Body and BodyFile empty: loader is a no-op and returns success.")]
	public void TryLoadBodyFromFile_WhenBothEmpty_IsNoOp() {
		// Arrange
		PageUpdateOptions options = new();

		// Act
		(bool ok, string error) = PageUpdateBodyLoader.TryLoadBodyFromFile(options);

		// Assert
		ok.Should().BeTrue(because: "the loader must not fail when there is nothing to load — the caller is responsible for the missing-body error");
		error.Should().BeNull(because: "no error is expected for the no-op case");
		options.Body.Should().BeNullOrEmpty(because: "the loader must not invent body content");
	}

	[Test]
	[Description("BodyFile points to a non-existing file: loader fails with a descriptive error.")]
	public void TryLoadBodyFromFile_WhenFileMissing_ReturnsError() {
		// Arrange
		string missingPath = Path.Combine(Path.GetTempPath(), $"clio-missing-{Path.GetRandomFileName()}.json");
		PageUpdateOptions options = new() { BodyFile = missingPath };

		// Act
		(bool ok, string error) = PageUpdateBodyLoader.TryLoadBodyFromFile(options);

		// Assert
		ok.Should().BeFalse(because: "a missing body file must surface as a load failure");
		error.Should().Contain(missingPath, because: "the error must identify the file that could not be found");
		options.Body.Should().BeNullOrEmpty(because: "no body must be set when the file cannot be found");
	}

	[Test]
	[Description("BodyFile points to an existing file: file content is loaded into Body.")]
	public void TryLoadBodyFromFile_WhenFileExists_LoadsContent() {
		// Arrange
		string tempFile = Path.Combine(Path.GetTempPath(), $"clio-body-{Path.GetRandomFileName()}.json");
		string expectedContent = "{\"viewConfigDiff\":[]}";
		File.WriteAllText(tempFile, expectedContent);
		try {
			PageUpdateOptions options = new() { BodyFile = tempFile };

			// Act
			(bool ok, string error) = PageUpdateBodyLoader.TryLoadBodyFromFile(options);

			// Assert
			ok.Should().BeTrue(because: "an existing body file must be loaded successfully");
			error.Should().BeNull(because: "no error is expected on a successful load");
			options.Body.Should().Be(expectedContent, because: "the body must equal the file content verbatim");
		}
		finally {
			if (File.Exists(tempFile)) {
				File.Delete(tempFile);
			}
		}
	}
}
