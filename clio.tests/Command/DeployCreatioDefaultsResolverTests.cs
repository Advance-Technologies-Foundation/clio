using System.IO.Abstractions.TestingHelpers;
using Clio;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class DeployCreatioDefaultsResolverTests {

	private ISettingsRepository _settingsRepository;
	private MockFileSystem _fileSystem;
	private ILogger _logger;
	private DeployCreatioDefaultsResolver _sut;

	[SetUp]
	public void SetUp() {
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_settingsRepository.GetDeployCreatioDefaults().Returns(new DeployCreatioDefaults());
		_fileSystem = TestFileSystem.MockFileSystem();
		_logger = Substitute.For<ILogger>();
		_sut = new DeployCreatioDefaultsResolver(_settingsRepository, _fileSystem, _logger);
	}

	[Test]
	[Description("Fills the db server name from the configured default when it is omitted on the command line.")]
	public void ApplyDefaults_ShouldFillDbServerName_WhenUnsetAndDefaultConfigured() {
		// Arrange
		_settingsRepository.GetDeployCreatioDefaults()
			.Returns(new DeployCreatioDefaults { DbServerName = "my-local-postgres" });
		PfInstallerOptions options = new();

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.DbServerName.Should().Be("my-local-postgres",
			because: "an omitted db server name falls back to the configured default");
	}

	[Test]
	[Description("Keeps the command-line db server name and does not override it from the configured default.")]
	public void ApplyDefaults_ShouldNotOverrideDbServerName_WhenExplicitlyProvided() {
		// Arrange
		_settingsRepository.GetDeployCreatioDefaults()
			.Returns(new DeployCreatioDefaults { DbServerName = "my-local-postgres" });
		PfInstallerOptions options = new() { DbServerName = "explicit-server" };

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.DbServerName.Should().Be("explicit-server",
			because: "an explicitly supplied option must always take precedence over a configured default");
	}

	[Test]
	[Description("Fills the site port from the configured default when the port is left at its zero (unset) value.")]
	public void ApplyDefaults_ShouldFillSitePort_WhenZeroAndDefaultConfigured() {
		// Arrange
		_settingsRepository.GetDeployCreatioDefaults()
			.Returns(new DeployCreatioDefaults { SitePort = 40018 });
		PfInstallerOptions options = new() { SitePort = 0 };

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.SitePort.Should().Be(40018,
			because: "a zero (unset) site port falls back to the configured default");
	}

	[Test]
	[Description("Overrides the parser 'auto' deployment default with the configured deployment method.")]
	public void ApplyDefaults_ShouldOverrideAutoDeployment_WhenDefaultConfigured() {
		// Arrange
		_settingsRepository.GetDeployCreatioDefaults()
			.Returns(new DeployCreatioDefaults { DeploymentMethod = "iis" });
		PfInstallerOptions options = new() { DeploymentMethod = "auto" };

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.DeploymentMethod.Should().Be("iis",
			because: "the 'auto' parser default is treated as unset and is overridden by a configured deployment method");
	}

	[Test]
	[Description("Keeps an explicitly chosen non-auto deployment method over the configured default.")]
	public void ApplyDefaults_ShouldNotOverrideExplicitDeployment_WhenNotAuto() {
		// Arrange
		_settingsRepository.GetDeployCreatioDefaults()
			.Returns(new DeployCreatioDefaults { DeploymentMethod = "iis" });
		PfInstallerOptions options = new() { DeploymentMethod = "dotnet" };

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.DeploymentMethod.Should().Be("dotnet",
			because: "an explicitly chosen deployment method must not be overridden by the configured default");
	}

	[Test]
	[Description("Derives the site name from the zip file name when no site name is configured or supplied.")]
	public void ApplyDefaults_ShouldDeriveSiteNameFromZip_WhenSiteNameUnsetAndNoDefault() {
		// Arrange
		PfInstallerOptions options = new() { ZipFile = @"F:\CreatioBuilds\lcap_local.zip" };

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.SiteName.Should().Be("lcap_local",
			because: "with no configured or supplied site name, the name is derived from the zip file name");
	}

	[Test]
	[Description("Prefers the configured default site name over the name derived from the zip file.")]
	public void ApplyDefaults_ShouldPreferConfiguredSiteName_OverDerivedFromZip() {
		// Arrange
		_settingsRepository.GetDeployCreatioDefaults()
			.Returns(new DeployCreatioDefaults { SiteName = "lcap-local" });
		PfInstallerOptions options = new() { ZipFile = @"F:\CreatioBuilds\some_build.zip" };

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.SiteName.Should().Be("lcap-local",
			because: "a configured default site name takes precedence over deriving one from the zip file");
	}

	[Test]
	[Description("Sanitizes separators when deriving a site name so the result is usable as a database name.")]
	public void ApplyDefaults_ShouldSanitizeDerivedSiteName_WhenZipNameHasSeparators() {
		// Arrange
		PfInstallerOptions options = new() {
			ZipFile = @"F:\CreatioBuilds\10.0.0.777_BankSales_MarketingNet8_PostgreSQL_ENU.zip"
		};

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.SiteName.Should().Be("_10_0_0_777_BankSales_MarketingNet8_PostgreSQL_ENU",
			because: "dots and other separators fold to single underscores and a leading digit is prefixed so the derived name is a valid unquoted database identifier");
	}

	[Test]
	[Description("Folds non-ASCII characters when deriving a site name so the result is a valid ASCII identifier.")]
	public void ApplyDefaults_ShouldFoldNonAsciiCharacters_WhenDerivingSiteName() {
		// Arrange
		PfInstallerOptions options = new() { ZipFile = @"F:\CreatioBuilds\Проект_local.zip" };

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.SiteName.Should().Be("local",
			because: "non-ASCII characters are folded so the derived name is a valid ASCII database/site identifier");
	}

	[Test]
	[Description("Leaves options untouched when no defaults are configured and no zip file is supplied.")]
	public void ApplyDefaults_ShouldLeaveOptionsUntouched_WhenNoDefaultsAndNoZip() {
		// Arrange
		PfInstallerOptions options = new();

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.DbServerName.Should().BeNullOrEmpty(because: "no default was configured to apply");
		options.SiteName.Should().BeNullOrEmpty(because: "there is no zip file to derive a site name from");
	}
}
