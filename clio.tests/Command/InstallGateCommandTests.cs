using System.IO;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.WebApplication;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class InstallGateCommandTests : BaseCommandTests<InstallGateOptions> {

	#region Fields: Private

	private const string ClioRoot = "clio-root";
	private IPackageInstaller _packageInstaller;
	private IApplication _application;
	private IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private ILogger _logger;
	private InstallGateCommand _command;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_packageInstaller = Substitute.For<IPackageInstaller>();
		_application = Substitute.For<IApplication>();
		_workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		_logger = Substitute.For<ILogger>();
		_workingDirectoriesProvider.ExecutingDirectory.Returns(ClioRoot);
		containerBuilder.AddSingleton(_packageInstaller);
		containerBuilder.AddSingleton(_application);
		containerBuilder.AddSingleton(_workingDirectoriesProvider);
		containerBuilder.AddSingleton(_logger);
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<InstallGateCommand>();
	}

	[Test]
	[Description("Execute should install the bundled cliogate package and restart the application after success.")]
	public void Execute_ShouldInstallCliogateAndRestartApplication() {
		// Arrange
		string expectedPackagePath = Path.Combine(ClioRoot, "cliogate", "cliogate.gz");
		EnvironmentSettings capturedEnvironmentSettings = null;
		_packageInstaller
			.Install(
				expectedPackagePath,
				Arg.Do<EnvironmentSettings>(settings => capturedEnvironmentSettings = settings),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true)
			.Returns(true);

		// Act
		int result = _command.Execute(new InstallGateOptions());

		// Assert
		result.Should().Be(0, because: "successful package installation should make install-gate succeed");
		capturedEnvironmentSettings.Should().NotBeNull(
			because: "install-gate should pass resolved environment settings to the package installer");
		capturedEnvironmentSettings!.DeveloperModeEnabled.Should().BeFalse(
			because: "cliogate installation should not unlock maintainer packages through developer mode");
		_application.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IApplication.Restart))
			.Should().Be(1, because: "install-gate should restart Creatio after a successful cliogate install");
		_logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteLine))
			.Should().Be(1, because: "successful install-gate execution should report completion");
	}

	[Test]
	[Description("Execute should install the bundled netcore cliogate package when the target environment is netcore.")]
	public void Execute_ShouldInstallNetCoreCliogatePackage_WhenEnvironmentIsNetCore() {
		// Arrange
		EnvironmentSettings.IsNetCore = true;
		string expectedPackagePath = Path.Combine(ClioRoot, "cliogate", "cliogate_netcore.gz");
		_packageInstaller
			.Install(
				expectedPackagePath,
				Arg.Any<EnvironmentSettings>(),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true)
			.Returns(true);

		// Act
		int result = _command.Execute(new InstallGateOptions());

		// Assert
		result.Should().Be(0, because: "successful netcore package installation should make install-gate succeed");
		_packageInstaller.ReceivedCalls()
			.Count(call =>
				call.GetMethodInfo().Name == nameof(IPackageInstaller.Install)
				&& call.GetArguments().FirstOrDefault() as string == expectedPackagePath)
			.Should().Be(1, because: "netcore environments should install the bundled cliogate_netcore package");
	}

	[Test]
	[Description("Execute should return failure and skip restart when cliogate installation fails.")]
	public void Execute_ShouldReturnFailureAndSkipRestart_WhenPackageInstallFails() {
		// Arrange
		_packageInstaller
			.Install(
				Arg.Any<string>(),
				Arg.Any<EnvironmentSettings>(),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true)
			.Returns(false);

		// Act
		int result = _command.Execute(new InstallGateOptions());

		// Assert
		result.Should().Be(1, because: "failed package installation should make install-gate fail");
		_application.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IApplication.Restart))
			.Should().Be(0, because: "install-gate should not restart Creatio when package installation fails");
		_logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError))
			.Should().Be(1, because: "failed install-gate execution should report an error");
	}

	#endregion

}
