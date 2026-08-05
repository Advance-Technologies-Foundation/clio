using System;
using System.IO;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class InstallProcessBuilderCommandTests : BaseCommandTests<InstallProcessBuilderOptions> {

	#region Fields: Private

	private const string ClioRoot = "clio-root";

	/// <summary>A successful ListUserTasks envelope, as ProcessDesignService actually returns it.</summary>
	private const string ServiceAnswersResponse =
		"{\"ListUserTasksResult\":{\"errorMessage\":null,\"success\":true,"
		+ "\"userTasks\":[{\"name\":\"ActivityUserTask\",\"uid\":\"b5c726f2-af5b-4381-bac6-913074144308\"}]}}";

	private IPackageInstaller _packageInstaller;
	private IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private IFileSystem _fileSystem;
	private IRequiredPackageChecker _requiredPackageChecker;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private InstallProcessBuilderCommand _command;

	#endregion

	#region Properties: Private

	private static string ExpectedPackagePath => Path.Combine(
		ClioRoot, BundledPackages.ProcessBuilderPackageName, BundledPackages.ProcessBuilderArchiveFileName);

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_packageInstaller = Substitute.For<IPackageInstaller>();
		_workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		_fileSystem = Substitute.For<IFileSystem>();
		_requiredPackageChecker = Substitute.For<IRequiredPackageChecker>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_workingDirectoriesProvider.ExecutingDirectory.Returns(ClioRoot);
		// Happy-path defaults, so each test only arranges the deviation it is actually about: the bundled
		// artifact is present, the environment carries nothing yet, and the service answers after install.
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_requiredPackageChecker
			.IsCompatible(Arg.Any<string>(), Arg.Any<string>())
			.Returns(false);
		_serviceUrlBuilder
			.Build(ServiceUrlBuilder.KnownRoute.ListUserTasks)
			.Returns("http://localhost/0/rest/ProcessDesignService/ListUserTasks");
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ServiceAnswersResponse);
		containerBuilder.AddSingleton(_packageInstaller);
		containerBuilder.AddSingleton(_workingDirectoriesProvider);
		containerBuilder.AddSingleton(_fileSystem);
		containerBuilder.AddSingleton(_requiredPackageChecker);
		containerBuilder.AddSingleton(_applicationClient);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_logger);
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<InstallProcessBuilderCommand>();
	}

	[TearDown]
	public void TearDownCommand() {
		_packageInstaller.ClearReceivedCalls();
		_fileSystem.ClearReceivedCalls();
		_requiredPackageChecker.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Execute should install the bundled process-builder package and then prove ProcessDesignService answers.")]
	public void Execute_ShouldInstallPackageAndVerifyTheServiceAnswers() {
		// Arrange
		EnvironmentSettings capturedEnvironmentSettings = null;
		_packageInstaller
			.Install(
				ExpectedPackagePath,
				Arg.Do<EnvironmentSettings>(settings => capturedEnvironmentSettings = settings),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true)
			.Returns(true);

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(0,
			because: "a successful installation whose service answers should make the command succeed");
		capturedEnvironmentSettings.Should().NotBeNull(
			because: "the command should pass resolved environment settings to the package installer");
		capturedEnvironmentSettings!.DeveloperModeEnabled.Should().BeFalse(
			because: "installing must not unlock maintainer packages, whose unlock step routes through cliogate");
		_applicationClient.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest))
			.Should().Be(1,
				because: "the package ships without an assembly, so the service answering is the only proof "
					+ "the target actually compiled it");
	}

	[Test]
	[Description("Execute should fail when the package installs but ProcessDesignService does not answer, because that means the target never compiled it.")]
	public void Execute_ShouldFail_WhenPackageInstallsButServiceDoesNotAnswer() {
		// Arrange
		_packageInstaller
			.Install(
				Arg.Any<string>(),
				Arg.Any<EnvironmentSettings>(),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true)
			.Returns(true);
		// An IIS error page is exactly what an unbound route returns.
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<!DOCTYPE html><html><head><title>404 - Not Found</title></head></html>");

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1,
			because: "'installed' and 'working' are different states when the target compiles the package; "
				+ "reporting success here would hide the one failure mode that is otherwise silent — the "
				+ "package present, the name-based gate satisfied, and every service call failing");
		_logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError))
			.Should().Be(1, because: "the operator must be told the environment did not compile the package");
	}

	[Test]
	[Description("Execute should fail when the service returns a well-formed envelope reporting failure.")]
	public void Execute_ShouldFail_WhenServiceReportsUnsuccessfulEnvelope() {
		// Arrange
		_packageInstaller
			.Install(
				Arg.Any<string>(),
				Arg.Any<EnvironmentSettings>(),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true)
			.Returns(true);
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"ListUserTasksResult\":{\"errorMessage\":\"boom\",\"success\":false}}");

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1,
			because: "a parseable response is not the same as a working service; only success:true proves it");
	}

	[Test]
	[Description("Execute should resolve the bundled archive from the executing directory regardless of the target runtime.")]
	public void Execute_ShouldResolveTheSameArchive_WhenEnvironmentIsNetCore() {
		// Arrange
		EnvironmentSettings.IsNetCore = true;
		_packageInstaller
			.Install(
				Arg.Any<string>(),
				Arg.Any<EnvironmentSettings>(),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true)
			.Returns(true);

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(0, because: "a netcore environment installs the same bundled archive");
		_packageInstaller.ReceivedCalls()
			.Count(call =>
				call.GetMethodInfo().Name == nameof(IPackageInstaller.Install)
				&& call.GetArguments().FirstOrDefault() as string == ExpectedPackagePath)
			.Should().Be(1,
				because: "one archive carries both Files/Bin and Files/Bin/netstandard, so there is no "
					+ "per-runtime archive name to choose between");
	}

	[Test]
	[Description("Execute should skip both the install and the service check when a compatible version is already installed.")]
	public void Execute_ShouldSkipInstall_WhenCompatibleVersionAlreadyInstalled() {
		// Arrange
		_requiredPackageChecker
			.IsCompatible(BundledPackages.ProcessBuilderPackageName, BundledPackages.ProcessBuilderVersion)
			.Returns(true);

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(0, because: "an already-current environment needs no work and is not an error");
		_packageInstaller.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IPackageInstaller.Install))
			.Should().Be(0,
				because: "reinstalling an identical package would make the environment recompile it for nothing");
		_applicationClient.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest))
			.Should().Be(0,
				because: "the short-circuit reports what is already installed; probing a service this command "
					+ "did not touch would turn an unrelated outage into a failure of this command");
	}

	[Test]
	[Description("Execute should install anyway when the installed-version check fails, because the check is not the point of the command.")]
	public void Execute_ShouldInstallAnyway_WhenInstalledVersionCheckThrows() {
		// Arrange
		_requiredPackageChecker
			.IsCompatible(Arg.Any<string>(), Arg.Any<string>())
			.Returns(_ => throw new InvalidOperationException("SysPackage read denied"));
		_packageInstaller
			.Install(
				ExpectedPackagePath,
				Arg.Any<EnvironmentSettings>(),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true)
			.Returns(true);

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(0,
			because: "an unreachable host or a denied SysPackage read must not block an explicitly "
				+ "requested install");
		_packageInstaller.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IPackageInstaller.Install))
			.Should().Be(1, because: "the version check fails open, so the install still proceeds");
	}

	[Test]
	[Description("Execute should fail with a clear message when the clio installation does not carry the bundled archive.")]
	public void Execute_ShouldFailWithoutInstalling_WhenBundledArchiveIsMissing() {
		// Arrange
		_fileSystem.ExistsFile(ExpectedPackagePath).Returns(false);

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1, because: "there is nothing to install when the bundled archive is absent");
		_packageInstaller.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IPackageInstaller.Install))
			.Should().Be(0,
				because: "a missing artifact must be reported as such instead of surfacing as a generic "
					+ "install failure from inside the installer");
		_logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError))
			.Should().Be(1, because: "the operator needs to be told the distribution lacks the package");
	}

	[Test]
	[Description("Execute should return failure and skip the service check when package installation fails.")]
	public void Execute_ShouldReturnFailureAndSkipServiceCheck_WhenPackageInstallFails() {
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
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1, because: "a failed package installation should make the command fail");
		_applicationClient.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest))
			.Should().Be(0,
				because: "there is nothing to verify when the package never installed, and probing anyway "
					+ "would report the install failure as a service failure");
		_logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError))
			.Should().Be(1, because: "a failed install should report an error");
	}

	[Test]
	[Description("Execute should report the readable message before the stack trace when the installer throws.")]
	public void Execute_ShouldReportReadableMessageFirst_WhenInstallerThrows() {
		// Arrange
		_packageInstaller
			.Install(
				Arg.Any<string>(),
				Arg.Any<EnvironmentSettings>(),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true)
			.Returns(_ => throw new InvalidOperationException("upload rejected"));

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1, because: "an exception during installation should make the command fail");
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("upload rejected")));
		_applicationClient.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest))
			.Should().Be(0, because: "a throwing install must not proceed to the service check");
	}

	[Test]
	[Description("The options class must not declare a package requirement, or the installer would be refused by the requirement it exists to satisfy.")]
	public void InstallProcessBuilderOptions_ShouldNotDeclareAnyPackageRequirement() {
		// Arrange & Act
		bool hasRequirement = RequiresPackageAttribute.IsDefinedOn(typeof(InstallProcessBuilderOptions));

		// Assert
		hasRequirement.Should().BeFalse(
			because: "both dispatch chokepoints enforce [RequiresPackage] BEFORE the command runs, so a "
				+ "self-gated installer could never install the package it is gated on");
	}

	#endregion

}
