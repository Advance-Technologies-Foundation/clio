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
	[Description("GetDeployCreatioDefaults returns the migrated built-in site-port range when no deployment defaults were previously configured.")]
	public void GetDeployCreatioDefaults_ShouldReturnBuiltInSitePortRange_WhenNoneConfigured() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);

		// Act
		DeployCreatioDefaults result = sut.GetDeployCreatioDefaults();

		// Assert
		result.Should().NotBeNull(because: "the accessor must never return null so callers can use deployment defaults safely");
		result.SitePortRange.Should().Equal(new[] { 40100, 40199 },
			because: "loading legacy settings should persist and expose the built-in automatic IIS port range");
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
			SitePortRange = [41000, 41010],
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
		result.SitePortRange.Should().Equal(new[] { 41000, 41010 },
			because: "the configured site-port range must persist across repository instances");
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

	[Test]
	[Description("Knowledge-feedback standing approval round-trips without exposing a mutable repository-owned instance.")]
	public void KnowledgeFeedbackSettings_ShouldRoundTripDetachedStandingApproval_WhenConfigured() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		KnowledgeFeedbackSettings settings = new() {
			Mode = "auto",
			Destination = "https://creatio.ghe.com/engineering/clio-feedback",
			ReportingScope = "full",
			StandingApproval = new KnowledgeFeedbackStandingApproval {
				PolicyHash = $"sha256:{new string('a', 64)}"
			}
		};

		// Act
		sut.SetKnowledgeFeedbackSettings(settings);
		settings.StandingApproval.PolicyHash = "mutated-after-save";
		SettingsRepository reloaded = new(_fileSystem);
		KnowledgeFeedbackSettings result = reloaded.GetKnowledgeFeedbackSettings();

		// Assert
		result.Mode.Should().Be("auto",
			because: "the requested standing preference must persist across repository instances");
		result.ReportingScope.Should().Be("full",
			because: "internal comprehensive scope must survive persistence");
		result.StandingApproval!.PolicyHash.Should().Be($"sha256:{new string('a', 64)}",
			because: "repository persistence must detach from caller mutations");
	}
}
