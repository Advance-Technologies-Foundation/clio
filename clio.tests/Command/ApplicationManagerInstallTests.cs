using System;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// deploy-app reports an application the destination did not install as a failure.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ApplicationManagerInstallTests {

	[Test]
	[Description("When the application installer reports the installation as failed, Install throws naming the application and the destination, so deploy-app exits 1 instead of 0.")]
	public void Install_ShouldThrowNamingTheApplication_WhenTheInstallerReportsAFailure() {
		// Arrange
		EnvironmentSettings destination = new() { Uri = "http://localhost:5000" };
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment("target").Returns(destination);
		IApplicationInstaller installer = Substitute.For<IApplicationInstaller>();
		installer.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<bool?>(),
			Arg.Any<bool>()).Returns(false);
		ApplicationManager manager = new(Substitute.For<IWorkingDirectoriesProvider>(), Substitute.For<IDataProvider>(),
			settingsRepository, Substitute.For<IApplicationClientFactory>(), installer);

		// Act
		Action act = () => manager.Install("UsrApp", "app.zip", "target");

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the destination reported the installation as failed")
			.WithMessage("Application UsrApp was not installed in target.",
				because: "the closing error names what was not installed and where; the installer has already logged why");
		installer.Received(1).Install("app.zip", destination, Arg.Any<string>(), Arg.Any<bool?>(), Arg.Any<bool>());
	}

	[Test]
	[Description("A successful installation completes without an error.")]
	public void Install_ShouldComplete_WhenTheInstallerReportsSuccess() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment("target").Returns(new EnvironmentSettings());
		IApplicationInstaller installer = Substitute.For<IApplicationInstaller>();
		installer.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<bool?>(),
			Arg.Any<bool>()).Returns(true);
		ApplicationManager manager = new(Substitute.For<IWorkingDirectoriesProvider>(), Substitute.For<IDataProvider>(),
			settingsRepository, Substitute.For<IApplicationClientFactory>(), installer);

		// Act
		Action act = () => manager.Install("UsrApp", "app.zip", "target");

		// Assert
		act.Should().NotThrow(because: "the installer reported success");
	}
}
