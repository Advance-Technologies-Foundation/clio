using System.IO;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Command;

/// <summary>
/// Pins only what distinguishes this verb from <c>install-process-builder</c>: which package the shared
/// install flow is pointed at, and the attributes on its options type. The flow itself — refusals, readiness
/// wait, outcome check — is exercised by <see cref="InstallProcessBuilderCommandTests"/> through the common
/// base class.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public class InstallDashboardsMigratorCommandTests : BaseCommandTests<InstallDashboardsMigratorOptions> {

	#region Fields: Private

	private const string ClioRoot = "clio-root";

	private IPackageInstaller _packageInstaller;
	private IBundledPackageCatalog _bundledPackageCatalog;
	private IPackageInstallOutcomeVerifier _outcomeVerifier;
	private IServerReadinessWaiter _serverReadinessWaiter;
	private IRequiredPackageChecker _requiredPackageChecker;
	private ILogger _logger;
	private InstallDashboardsMigratorCommand _command;

	#endregion

	#region Properties: Private

	private static string ExpectedPackagePath => Path.Combine(
		ClioRoot, BundledPackages.DashboardsMigratorPackageName, BundledPackages.DashboardsMigratorArchiveFileName);

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_packageInstaller = Substitute.For<IPackageInstaller>();
		_outcomeVerifier = Substitute.For<IPackageInstallOutcomeVerifier>();
		_serverReadinessWaiter = Substitute.For<IServerReadinessWaiter>();
		_requiredPackageChecker = Substitute.For<IRequiredPackageChecker>();
		_logger = Substitute.For<ILogger>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.ExecutingDirectory.Returns(ClioRoot);
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_packageInstaller
			.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), null, null, true)
			.Returns(true);
		_serverReadinessWaiter.WaitForReady(Arg.Any<ServerReadinessOptions>()).Returns(true);
		_outcomeVerifier
			.IsPackageOperational(Arg.Any<string>(), out string _)
			.Returns(call => {
				call[1] = null;
				return true;
			});
		_bundledPackageCatalog = Substitute.For<IBundledPackageCatalog>();
		_bundledPackageCatalog.GetArchivePath(BundledPackages.DashboardsMigratorPackageName)
			.Returns(ExpectedPackagePath);
		_bundledPackageCatalog.ArchiveExists(BundledPackages.DashboardsMigratorPackageName).Returns(true);
		_bundledPackageCatalog
			.TryGetVersion(BundledPackages.DashboardsMigratorPackageName, out Arg.Any<PackageVersion>(),
				out Arg.Any<string>())
			.Returns(call => {
				call[1] = PackageVersion.ParseVersion("1.1.4.0");
				call[2] = null;
				return true;
			});
		containerBuilder.AddSingleton(_packageInstaller);
		containerBuilder.AddSingleton(workingDirectoriesProvider);
		containerBuilder.AddSingleton(_bundledPackageCatalog);
		containerBuilder.AddSingleton(_requiredPackageChecker);
		containerBuilder.AddSingleton(fileSystem);
		containerBuilder.AddSingleton(_outcomeVerifier);
		containerBuilder.AddSingleton(_serverReadinessWaiter);
		containerBuilder.AddSingleton(_logger);
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public void Setup() {
		_command = Container.GetRequiredService<InstallDashboardsMigratorCommand>();
	}

	[TearDown]
	public void TearDown() {
		_packageInstaller.ClearReceivedCalls();
		_outcomeVerifier.ClearReceivedCalls();
		_requiredPackageChecker.ClearReceivedCalls();
	}

	[Test]
	[Description("Points the shared install flow at the dashboards-migrator archive and asks the outcome verifier about that package, so the verifier probes DashboardsMigratorService rather than ProcessDesignService.")]
	public void Execute_ShouldInstallTheDashboardsMigratorArchive_AndVerifyThatPackage() {
		// Arrange
		InstallDashboardsMigratorOptions options = new() { Environment = "env" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "the archive is present, installs, the instance comes back and the package answers");
		_packageInstaller.Received(1).Install(ExpectedPackagePath, Arg.Any<EnvironmentSettings>(), null, null, true);
		_outcomeVerifier.Received(1).IsPackageOperational(BundledPackages.DashboardsMigratorPackageName, out string _);
		_requiredPackageChecker.Received(1).GetInstalledVersion(BundledPackages.DashboardsMigratorPackageName);
	}

	[Test]
	[Description("Declares the verb and its update alias, and none of the gates that would make the installer unreachable — the same absences InstallProcessBuilderCommandTests pins for the other bundled package.")]
	public void InstallDashboardsMigratorOptions_ShouldDeclareVerb_AndNoSelfDefeatingGate() {
		// Arrange & Act
		VerbAttribute verb = typeof(InstallDashboardsMigratorOptions)
			.GetCustomAttributes(typeof(VerbAttribute), false)
			.Cast<VerbAttribute>()
			.Single();

		// Assert
		verb.Name.Should().Be("install-dashboards-migrator", because: "the docs, help and MCP tool name this verb");
		verb.Aliases.Should().BeEquivalentTo(["update-dashboards-migrator"],
			because: "the update alias mirrors install-process-builder's, and nothing else is promised");
		RequiresCreatioVersionAttribute.IsDefinedOn(typeof(InstallDashboardsMigratorOptions)).Should().BeFalse(
			because: "that attribute compares the CORE version (10.x on an 8.3 stand), so the package's "
				+ "RequiredPlatformVersion 8.3.1 cannot be expressed with it and a floor there would always pass");
		RequiresPackageAttribute.IsDefinedOn(typeof(InstallDashboardsMigratorOptions)).Should().BeFalse(
			because: "a self-gated installer could never install the package it is gated on");
		typeof(InstallDashboardsMigratorOptions).GetCustomAttributes(typeof(FeatureToggleAttribute), true)
			.Should().BeEmpty(because: "a gated options type is filtered out of the verb parse array");
	}

	#endregion

}
