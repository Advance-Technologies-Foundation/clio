using System;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using Clio.Tests.Infrastructure;
using Clio.Utilities;
using Clio.Workspaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Workspace;

/// <summary>
/// A workspace installation the environment reports as failed fails the install, instead of being dropped.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Workspace")]
public sealed class WorkspaceInstallerFailureTests : BaseClioModuleTests {

	private static readonly string PackagesPath = TestFileSystem.GetRootedPath("ws-exit-code", "packages");

	private IPackageInstaller _packageInstaller;
	private IApplicationInstaller _applicationInstaller;
	private IOwnedApplicationClient _applicationClient;
	private ILogger _logger;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_packageInstaller = Substitute.For<IPackageInstaller>();
		_applicationInstaller = Substitute.For<IApplicationInstaller>();
		_applicationClient = Substitute.For<IOwnedApplicationClient>();
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		applicationClientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(_applicationClient);
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		workspacePathBuilder.PackagesFolderPath.Returns(PackagesPath);
		IOSPlatformChecker osPlatformChecker = Substitute.For<IOSPlatformChecker>();
		osPlatformChecker.IsWindowsEnvironment.Returns(true);
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(Substitute.For<IPackageArchiver>());
		containerBuilder.AddSingleton(_packageInstaller);
		containerBuilder.AddSingleton(_applicationInstaller);
		containerBuilder.AddSingleton(applicationClientFactory);
		containerBuilder.AddSingleton(workspacePathBuilder);
		containerBuilder.AddSingleton(osPlatformChecker);
		containerBuilder.AddSingleton(_logger);
	}

	public override void TearDown() {
		_packageInstaller.ClearReceivedCalls();
		_applicationInstaller.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		_applicationClient.Dispose();
		base.TearDown();
	}

	[Test]
	[Description("When the package installer answers false (it has logged \"Package installation failed: <reason>\"), Install throws and names the packages, so push-workspace cannot log Done and exit 0.")]
	public void Install_ShouldThrowAndNameThePackages_WhenThePackageInstallerReportsAFailure() {
		// Arrange
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), Arg.Any<PackageInstallOptions>(),
			Arg.Any<string>(), Arg.Any<bool>()).Returns(false);
		IWorkspaceInstaller sut = Container.GetRequiredService<IWorkspaceInstaller>();

		// Act
		Action act = () => sut.Install(["UsrA", "UsrB"]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the environment reported the installation as failed")
			.WithMessage("Workspace packages were not installed: UsrA, UsrB.*",
				because: "the closing error names what was not installed; the reason is the installer's line above");
	}

	[Test]
	[Description("When the application installer answers false, Install throws and does not log 'Installation completed successfully.' first.")]
	public void Install_ShouldThrowWithoutClaimingSuccess_WhenTheApplicationInstallerReportsAFailure() {
		// Arrange
		_applicationInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), Arg.Any<string>(),
			Arg.Any<bool?>(), Arg.Any<bool>()).Returns(false);
		IWorkspaceInstaller sut = Container.GetRequiredService<IWorkspaceInstaller>();

		// Act
		Action act = () => sut.Install(["UsrA"], useApplicationInstaller: true);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the environment reported the installation as failed");
		_logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteInfo))
			.Select(call => call.GetArguments().FirstOrDefault()?.ToString())
			.Should().NotContain("Installation completed successfully.",
				because: "a failed installation must not be reported as completed");
	}

	[Test]
	[Description("A successful installation still completes without an error, and the application-installer path still confirms it.")]
	public void Install_ShouldComplete_WhenTheInstallerReportsSuccess() {
		// Arrange
		_applicationInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), Arg.Any<string>(),
			Arg.Any<bool?>(), Arg.Any<bool>()).Returns(true);
		IWorkspaceInstaller sut = Container.GetRequiredService<IWorkspaceInstaller>();

		// Act
		Action act = () => sut.Install(["UsrA"], useApplicationInstaller: true);

		// Assert
		act.Should().NotThrow(because: "the installer reported success");
		_logger.Received(1).WriteInfo("Installation completed successfully.");
	}
}
