using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SettingsRepositoryDeployDefaultsTests {

	private MockFileSystem _fileSystem;

	[SetUp]
	public void SetUp() {
		_fileSystem = TestFileSystem.MockFileSystem();
		_fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(
			File.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-netcore-active-env.json"))));
	}

	[Test]
	[Description("GetDeployCreatioDefaults returns an empty, non-null instance when no defaults are configured.")]
	public void GetDeployCreatioDefaults_ShouldReturnEmptyInstance_WhenNoneConfigured() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);

		// Act
		DeployCreatioDefaults result = sut.GetDeployCreatioDefaults();

		// Assert
		result.Should().NotBeNull(because: "the accessor must never return null so callers can inspect IsEmpty safely");
		result.IsEmpty.Should().BeTrue(because: "no deploy-creatio defaults are configured in the base settings file");
	}

	[Test]
	[Description("SetDeployCreatioDefaults persists values that round-trip through a freshly loaded repository.")]
	public void SetDeployCreatioDefaults_ShouldPersistValues_WhenConfigured() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		DeployCreatioDefaults defaults = new() {
			DbServerName = "my-local-postgres",
			RedisServerName = "local-redis",
			SiteName = "lcap-local",
			SitePort = 40018,
			DeploymentMethod = "iis"
		};

		// Act
		sut.SetDeployCreatioDefaults(defaults);
		SettingsRepository reloaded = new(_fileSystem);
		DeployCreatioDefaults result = reloaded.GetDeployCreatioDefaults();

		// Assert
		result.DbServerName.Should().Be("my-local-postgres", because: "the configured db server name must persist across repository instances");
		result.RedisServerName.Should().Be("local-redis", because: "the configured redis server name must persist across repository instances");
		result.SiteName.Should().Be("lcap-local", because: "the configured site name must persist across repository instances");
		result.SitePort.Should().Be(40018, because: "the configured site port must persist across repository instances");
		result.DeploymentMethod.Should().Be("iis", because: "the configured deployment method must persist across repository instances");
	}

	[Test]
	[Description("SetDeployCreatioDefaults clears the stored defaults when passed null.")]
	public void SetDeployCreatioDefaults_ShouldClearDefaults_WhenPassedNull() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		sut.SetDeployCreatioDefaults(new DeployCreatioDefaults { DbServerName = "my-local-postgres" });

		// Act
		sut.SetDeployCreatioDefaults(null);
		SettingsRepository reloaded = new(_fileSystem);
		DeployCreatioDefaults result = reloaded.GetDeployCreatioDefaults();

		// Assert
		result.IsEmpty.Should().BeTrue(because: "passing null clears the persisted deploy-creatio defaults");
	}

	[Test]
	[Description("SetDeployCreatioDefaults clears the stored defaults when passed an all-empty instance.")]
	public void SetDeployCreatioDefaults_ShouldClearDefaults_WhenPassedEmptyInstance() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		sut.SetDeployCreatioDefaults(new DeployCreatioDefaults { DbServerName = "my-local-postgres" });

		// Act
		sut.SetDeployCreatioDefaults(new DeployCreatioDefaults());
		SettingsRepository reloaded = new(_fileSystem);
		DeployCreatioDefaults result = reloaded.GetDeployCreatioDefaults();

		// Assert
		result.IsEmpty.Should().BeTrue(because: "an all-empty instance is equivalent to clearing the persisted defaults");
	}
}
