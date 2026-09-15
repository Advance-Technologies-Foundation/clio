using System;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using CommandLine;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Guards what is specific to installing the dashboards migrator: it is fetched from a feed rather than
/// shipped inside clio, and the verdict rests on the app answering afterwards.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class InstallDashboardsMigratorCommandTests {

	#region Fields: Private

	private IInstallNugetPackage _installNugetPackage;
	private IServerReadinessWaiter _serverReadinessWaiter;
	private IPackageInstallOutcomeVerifier _outcomeVerifier;
	private IRequiredPackageChecker _requiredPackageChecker;
	private IInstalledAppVersions _installedAppVersions;
	private ILogger _logger;
	private InstallDashboardsMigratorCommand _command;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_installNugetPackage = Substitute.For<IInstallNugetPackage>();
		_serverReadinessWaiter = Substitute.For<IServerReadinessWaiter>();
		_outcomeVerifier = Substitute.For<IPackageInstallOutcomeVerifier>();
		_requiredPackageChecker = Substitute.For<IRequiredPackageChecker>();
		_installedAppVersions = Substitute.For<IInstalledAppVersions>();
		_logger = Substitute.For<ILogger>();
		_serverReadinessWaiter.WaitForReady(Arg.Any<ServerReadinessOptions>()).Returns(true);
		_outcomeVerifier.IsPackageOperational(Arg.Any<string>(), out Arg.Any<string>()).Returns(true);
		_command = new InstallDashboardsMigratorCommand(new EnvironmentSettings(), _installNugetPackage,
			_serverReadinessWaiter, _outcomeVerifier, _requiredPackageChecker, _installedAppVersions, _logger);
	}

	[Test]
	[Description("Fetches the app from the default feed when the caller names none, so 'install the migrator' needs no feed knowledge.")]
	public void Execute_ShouldFetchFromTheDefaultFeed_WhenNoSourceIsGiven() {
		// Arrange
		InstallDashboardsMigratorOptions options = new() { EnvironmentName = "env" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0);
		_installNugetPackage.Received(1).Install(DashboardsMigratorDistribution.PackageName, null,
			DashboardsMigratorDistribution.DefaultFeedUrl);
	}

	[Test]
	[Description("Fails when the app does not answer after the install, because an accepted package that never compiled looks identical to a healthy one.")]
	public void Execute_ShouldFail_WhenThePackageDoesNotAnswer() {
		// Arrange
		_outcomeVerifier.IsPackageOperational(Arg.Any<string>(), out Arg.Any<string>()).Returns(false);

		// Act
		int exitCode = _command.Execute(new InstallDashboardsMigratorOptions { EnvironmentName = "env" });

		// Assert
		exitCode.Should().Be(1);
		_installedAppVersions.DidNotReceive().Record(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Records the version read back from the environment, so clio info names the build that is serving rather than the one that was asked for.")]
	public void Execute_ShouldRecordTheVersionReadFromTheEnvironment() {
		// Arrange
		_requiredPackageChecker.GetInstalledVersion(DashboardsMigratorDistribution.PackageName)
			.Returns(PackageVersion.ParseVersion("1.1.4.7"));

		// Act
		_command.Execute(new InstallDashboardsMigratorOptions { EnvironmentName = "env", Version = "1.1.4" });

		// Assert
		_installedAppVersions.Received(1).Record(DashboardsMigratorDistribution.DisplayName, "1.1.4.7");
	}

	[Test]
	[Description("Keeps the verb and its aliases, which scripts and the MCP tool call by name.")]
	public void Options_ShouldKeepTheVerbAndItsAliases() {
		// Arrange
		VerbAttribute verb = typeof(InstallDashboardsMigratorOptions)
			.GetCustomAttributes(typeof(VerbAttribute), inherit: false).Cast<VerbAttribute>().Single();

		// Act & Assert
		verb.Name.Should().Be("install-dashboards-migrator");
		verb.Aliases.Should().Contain("update-dashboards-migrator",
			because: "an update is the same operation and callers already use this name");
	}

	#endregion

}
