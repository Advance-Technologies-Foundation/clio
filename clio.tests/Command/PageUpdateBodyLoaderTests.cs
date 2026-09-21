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

	[Test]
	[Description("A path that EXISTS but cannot be read is a different branch from 'File not found', with its own wording. An editor holding an exclusive lock, or a file with no read permission, must reach the caller as this tool's own error envelope rather than as a protocol-level MCP failure or a swallowed exception (PR #1352 review).")]
	public void TryResolveBody_WhenFileExistsButIsLocked_ReportsCannotRead() {
		// Arrange - FileShare.None is honoured in-process on Windows and on Unix, so this stays cross-OS.
		string tempFile = Path.Combine(Path.GetTempPath(), $"clio-body-locked-{Path.GetRandomFileName()}.json");
		File.WriteAllText(tempFile, "{\"viewConfigDiff\":[]}");
		try {
			using FileStream exclusiveLock = new(tempFile, FileMode.Open, FileAccess.Read, FileShare.None);

			// Act
			(bool ok, string resolvedBody, string error) = PageUpdateBodyLoader.TryResolveBody(null, tempFile);

			// Assert
			ok.Should().BeFalse(because: "an unreadable file is a failure, not an empty body silently passed on");
			resolvedBody.Should().BeNull(because: "nothing was read, so no body may travel alongside the failure");
			error.Should().Contain("Cannot read",
				because: "the wording has to separate this from the 'File not found' branch - the two send the caller to different remedies");
			error.Should().Contain(tempFile, because: "the error must name the file the caller has to unlock");
			error.Should().NotContain("File not found",
				because: "the file DOES exist; reporting it as missing is the misleading diagnosis this branch was added to remove");
		}
		finally {
			if (File.Exists(tempFile)) {
				File.Delete(tempFile);
			}
		}
	}

	[Test]
	[Description("The same lock through the options-carrying entry point: TryLoadBodyFromFile shares TryResolveBody's branch, and a caller on the save path must get the same envelope as validate-page (PR #1352 review).")]
	public void TryLoadBodyFromFile_WhenFileIsLocked_ReportsCannotReadAndLeavesBodyUnset() {
		// Arrange
		string tempFile = Path.Combine(Path.GetTempPath(), $"clio-body-locked-{Path.GetRandomFileName()}.json");
		File.WriteAllText(tempFile, "{\"viewConfigDiff\":[]}");
		try {
			using FileStream exclusiveLock = new(tempFile, FileMode.Open, FileAccess.Read, FileShare.None);
			PageUpdateOptions options = new() { BodyFile = tempFile };

			// Act
			(bool ok, string error) = PageUpdateBodyLoader.TryLoadBodyFromFile(options);

			// Assert
			ok.Should().BeFalse(because: "the save path must refuse rather than send an empty body to Creatio");
			error.Should().Contain("Cannot read", because: "both entry points share one branch and must not word it differently");
			options.Body.Should().BeNullOrEmpty(because: "no body must be set when the file could not be read");
		}
		finally {
			if (File.Exists(tempFile)) {
				File.Delete(tempFile);
			}
		}
	}
}
