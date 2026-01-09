using System.Runtime.InteropServices;
using Clio.Common;
using Clio.Common.db;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.db;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
public class PostgresToolsPathDetectorTests
{
	private IFileSystem _fileSystem;

	[SetUp]
	public void Setup() {
		_fileSystem = Substitute.For<IFileSystem>();
	}

	[Test]
	[Description("Should return null when pg_restore is not found anywhere")]
	public void FindPgRestore_WhenNotFound_ReturnsNull() {
		// Arrange
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(false);
		_fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(false);

		var sut = new PostgresToolsPathDetector(_fileSystem);

		// Act
		string result = sut.FindPgRestore();

		// Assert
		result.Should().BeNull("because pg_restore is not found");
	}

	[Test]
	[Description("Should return null when pg_restore is not available")]
	public void IsPgRestoreAvailable_WhenNotAvailable_ReturnsFalse() {
		// Arrange
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(false);
		_fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(false);

		var sut = new PostgresToolsPathDetector(_fileSystem);

		// Act
		bool result = sut.IsPgRestoreAvailable();

		// Assert
		result.Should().BeFalse("because pg_restore is not available");
	}

	[Test]
	[Description("Should use explicit pgToolsPath when provided")]
	public void GetPgRestorePath_WithExplicitPath_ReturnsExplicitPath() {
		// Arrange
		string explicitPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? @"C:\CustomPostgres\bin"
			: "/custom/postgres/bin";

		string expectedExe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? @"C:\CustomPostgres\bin\pg_restore.exe"
			: "/custom/postgres/bin/pg_restore";

		_fileSystem.ExistsFile(expectedExe).Returns(true);

		var sut = new PostgresToolsPathDetector(_fileSystem);

		// Act
		string result = sut.GetPgRestorePath(explicitPath);

		// Assert
		result.Should().Be(expectedExe, "because explicit path is provided");
	}

	[Test]
	[Description("Should return null when explicit path doesn't contain pg_restore")]
	public void GetPgRestorePath_WithInvalidExplicitPath_ReturnsNull() {
		// Arrange
		string explicitPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? @"C:\InvalidPath"
			: "/invalid/path";

		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(false);

		var sut = new PostgresToolsPathDetector(_fileSystem);

		// Act
		string result = sut.GetPgRestorePath(explicitPath);

		// Assert
		result.Should().BeNull("because pg_restore is not at the explicit path");
	}
}
