using Clio;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class DeployCreatioDefaultsResolverTests : BaseClioModuleTests {

	private ISettingsRepository _settingsRepository;
	private ILogger _logger;
	private DeployCreatioDefaultsResolver _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton(_logger);
	}

	public override void Setup() {
		base.Setup();
		_settingsRepository.GetDeployCreatioDefaults().Returns(new DeployCreatioDefaults());
		_sut = (DeployCreatioDefaultsResolver)Container.GetRequiredService<IDeployCreatioDefaultsResolver>();
	}

	public override void TearDown() {
		_settingsRepository.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
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
	[Description("Uses the sole enabled local database server when no explicit or deploy-specific default is configured.")]
	public void ApplyDefaults_ShouldUseSoleEnabledLocalDbServer_WhenNoDeployDefaultConfigured() {
		// Arrange
		_settingsRepository.GetLocalDbServerNames().Returns(["my-local-postgres"]);
		PfInstallerOptions options = new() { ExplorerLaunch = true };

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.DbServerName.Should().Be("my-local-postgres",
			because: "a sole enabled local database is the unambiguous local deployment preference");
	}

	[Test]
	[Description("Preserves an omitted database server as Kubernetes intent outside the Explorer integration.")]
	public void ApplyDefaults_ShouldPreserveKubernetesIntent_WhenSoleLocalDbExistsOutsideExplorer() {
		// Arrange
		_settingsRepository.GetLocalDbServerNames().Returns(["my-local-postgres"]);
		PfInstallerOptions options = new();

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.DbServerName.Should().BeNullOrEmpty(
			because: "MCP and ClioRing use an omitted database server to explicitly select Kubernetes");
	}

	[Test]
	[Description("Leaves database selection unset when multiple local database servers are enabled and no deploy default exists.")]
	public void ApplyDefaults_ShouldKeepKubernetesFallback_WhenMultipleLocalDbServersEnabled() {
		// Arrange
		_settingsRepository.GetLocalDbServerNames().Returns(["postgres-a", "postgres-b"]);
		PfInstallerOptions options = new() { ExplorerLaunch = true };

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.DbServerName.Should().BeNullOrEmpty(
			because: "multiple local database servers are ambiguous and must not silently replace the existing Kubernetes path");
	}

	[Test]
	[Description("Leaves database selection unset when no enabled local database server or deploy default exists.")]
	public void ApplyDefaults_ShouldKeepKubernetesFallback_WhenNoLocalDbServerEnabled() {
		// Arrange
		_settingsRepository.GetLocalDbServerNames().Returns([]);
		PfInstallerOptions options = new() { ExplorerLaunch = true };

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.DbServerName.Should().BeNullOrEmpty(
			because: "the existing Kubernetes path remains the fallback when no local database preference is available");
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
	[Description("Leaves the site name unset when only a zip file is supplied so interactive deployment can prompt for it.")]
	public void ApplyDefaults_ShouldLeaveSiteNameUnset_WhenZipProvidedAndNoDefault() {
		// Arrange
		PfInstallerOptions options = new() {
			ZipFile = @"F:\CreatioBuilds\10.0.0.777_BankSales_MarketingNet8_PostgreSQL_ENU.zip"
		};

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.SiteName.Should().BeNullOrEmpty(
			because: "the installer must prompt instead of turning a long archive name into database and IIS identifiers");
	}

	[Test]
	[Description("Uses the configured default site name when no site name is supplied on the command line.")]
	public void ApplyDefaults_ShouldUseConfiguredSiteName_WhenSiteNameUnset() {
		// Arrange
		_settingsRepository.GetDeployCreatioDefaults()
			.Returns(new DeployCreatioDefaults { SiteName = "lcap-local" });
		PfInstallerOptions options = new() { ZipFile = @"F:\CreatioBuilds\some_build.zip" };

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.SiteName.Should().Be("lcap-local",
			because: "a configured default site name should continue to avoid the interactive prompt");
	}

	[Test]
	[Description("Keeps an explicitly supplied site name instead of replacing it with the configured default.")]
	public void ApplyDefaults_ShouldKeepExplicitSiteName_WhenDefaultConfigured() {
		// Arrange
		_settingsRepository.GetDeployCreatioDefaults()
			.Returns(new DeployCreatioDefaults { SiteName = "configured-site" });
		PfInstallerOptions options = new() { SiteName = "explicit-site" };

		// Act
		_sut.ApplyDefaults(options);

		// Assert
		options.SiteName.Should().Be("explicit-site",
			because: "command-line options must continue to take precedence over configured defaults");
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
		options.SiteName.Should().BeNullOrEmpty(because: "no site name was supplied or configured");
	}
}
