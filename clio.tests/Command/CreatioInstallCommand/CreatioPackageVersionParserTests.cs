using System;
using Clio.Command.CreatioInstallCommand;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.CreatioInstallCommand;

[TestFixture]
public class CreatioPackageVersionParserTests{
	[Test]
	[Description("Should parse full version from Creatio zip filename that starts with version token.")]
	public void TryParseVersion_ReturnsTrue_WhenZipFileNameContainsVersionPrefix() {
		// Arrange
		ICreatioPackageVersionParser parser = new CreatioPackageVersionParser();
		const string packagePath = @"C:\Builds\8.3.4.425_Studio_Softkey_PostgreSQL_ENU.zip";

		// Act
		bool result = parser.TryParseVersion(packagePath, out Version version);

		// Assert
		result.Should().BeTrue("because Creatio distribution zip names start with semantic version");
		version.Should().Be(new Version(8, 3, 4, 425), "because parser should extract the whole version token");
	}

	[Test]
	[Description("Should parse version from extracted directory names that follow the same naming convention.")]
	public void TryParseVersion_ReturnsTrue_WhenDirectoryNameContainsVersionPrefix() {
		// Arrange
		ICreatioPackageVersionParser parser = new CreatioPackageVersionParser();
		const string packagePath = "/mnt/builds/8.3.3.100_Studio_Softkey_PostgreSQL_ENU";

		// Act
		bool result = parser.TryParseVersion(packagePath, out Version version);

		// Assert
		result.Should().BeTrue("because extracted folders can preserve the same versioned naming pattern");
		version.Should().Be(new Version(8, 3, 3, 100),
			"because parser should work with package directories as well as zip files");
	}

	[Test]
	[Description("Should return false when package name does not start with version token.")]
	public void TryParseVersion_ReturnsFalse_WhenVersionPrefixIsMissing() {
		// Arrange
		ICreatioPackageVersionParser parser = new CreatioPackageVersionParser();
		const string packagePath = @"C:\Builds\Studio_Softkey_PostgreSQL_ENU.zip";

		// Act
		bool result = parser.TryParseVersion(packagePath, out _);

		// Assert
		result.Should().BeFalse("because script execution must be skipped when build version cannot be determined");
	}
}
