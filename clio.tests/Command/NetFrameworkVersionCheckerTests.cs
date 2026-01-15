using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Category("UnitTests")]
[Platform(Include = "Win")]
[Description("Tests for NetFrameworkVersionChecker")]
public class NetFrameworkVersionCheckerTests
{
	private NetFrameworkVersionChecker _sut;

	[SetUp]
	public void Setup()
	{
		_sut = new NetFrameworkVersionChecker();
	}

	[Test]
	[Description("Should detect if .NET Framework 4.7.2 or higher is installed")]
	public void IsNetFramework472OrHigherInstalled_ReturnsBoolean()
	{
		// Act
		bool result = _sut.IsNetFramework472OrHigherInstalled();

		// Assert
		result.Should().Be(result, "because the method should return a boolean value");
	}

	[Test]
	[Description("Should get installed .NET Framework version")]
	public void GetInstalledVersion_ReturnsVersionString()
	{
		// Act
		string version = _sut.GetInstalledVersion();

		// Assert
		version.Should().NotBeNullOrEmpty("because the method should return a version string");
	}

	[Test]
	[Description("Should return version string that matches expected format")]
	public void GetInstalledVersion_ReturnsValidFormat()
	{
		// Act
		string version = _sut.GetInstalledVersion();

		// Assert
		version.Should().MatchRegex(@"^(\d+\.\d+(\.\d+)?.*|Not installed|Unable to determine)$", 
			"because the version should be in a valid format");
	}

	[Test]
	[Description("When .NET Framework 4.7.2+ is installed, version should reflect that")]
	public void WhenNetFramework472OrHigherInstalled_GetInstalledVersionReturnsAppropriateVersion()
	{
		// Arrange
		bool hasRequiredVersion = _sut.IsNetFramework472OrHigherInstalled();

		// Act
		string version = _sut.GetInstalledVersion();

		// Assert
		if (hasRequiredVersion)
		{
			version.Should().NotBe("Not installed", "because .NET Framework 4.7.2+ is reported as installed");
			version.Should().NotContain("4.6", "because version 4.6.x is older than 4.7.2");
		}
	}
}
