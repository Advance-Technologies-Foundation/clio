using Clio.Common;
using Clio.Package;
using Clio.Requests;
using Clio.Tests.Command;
using Clio.WebApplication;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Linq;

namespace Clio.Tests;

[TestFixture]
[NonParallelizable]
[Property("Module", "Package")]
internal class PackageInstallerTests : BaseClioModuleTests
{
	private bool _originalFailOnError;

	// GlobalContext.FailOnError is process-wide; a value left by another fixture would make these cases
	// require a success message their logs do not contain.
	public override void Setup() {
		base.Setup();
		_originalFailOnError = GlobalContext.FailOnError;
		GlobalContext.FailOnError = false;
	}

	public override void TearDown() {
		GlobalContext.FailOnError = _originalFailOnError;
		base.TearDown();
	}

	[Test]
	[Description("Install should keep package archive failures as a regular failed install without application-specific exception mapping.")]
	public void Install_Should_Not_Throw_InvalidGZipArchiveInstallException_When_Package_Log_Contains_Invalid_GZip_Exception() {
		// Arrange
		string packagePath = "T:\\Package.gz";
		FileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
		var applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		var applicationClient = Substitute.For<IOwnedApplicationClient>();
		applicationClientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(applicationClient);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns("{\"success\":false}");
		var application = Substitute.For<IApplication>();
		var packageArchiver = Substitute.For<IPackageArchiver>();
		var scriptExecutor = Substitute.For<ISqlScriptExecutor>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => callInfo.ArgAt<string>(0));
		var logger = Substitute.For<ILogger>();
		var packageLockManager = Substitute.For<IPackageLockManager>();
		var clioFileSystem = new FileSystem(FileSystem);
		var applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		applicationLogProvider.GetInstallationLog(Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>())
			.Returns(string.Empty,
				"Terrasoft.Common.InvalidGZipArchiveException: Unable to open \"Package.gz\". The file is invalid or corrupted.");
		PackageInstaller packageInstaller = new PackageInstaller(applicationLogProvider,
			environmentSettings,
			applicationClientFactory,
			application,
			packageArchiver,
			scriptExecutor,
			serviceUrlBuilder,
			clioFileSystem,
			logger,
			packageLockManager,
			Substitute.For<IDesignNodeSessionGuard>()
		);
		bool result = true;

		// Act
		Action act = () => result = packageInstaller.Install(packagePath, environmentSettings);

		// Assert
		act.Should().NotThrow(
			because: "exit code five is scoped to application installation");
		result.Should().BeFalse(
			because: "package installation should keep the existing general failed-install behavior");
	}

	[Test]
	[Description("ENG-102757: upload, backup, install, log reads and restart of one installation all go through one session, prepared by the design-node guard before the upload and followed by the wait for the restarted design node.")]
	public void Install_ShouldRunEveryStepThroughOneSession_WhenInstallingAPackage() {
		// Arrange
		InstallerArrangement arrangement = ArrangeInstaller(installResponse: "{\"success\":true}");

		// Act
		bool result = arrangement.Installer.Install(arrangement.PackagePath, arrangement.EnvironmentSettings);

		// Assert
		result.Should().BeTrue(because: "the installation service reported success");
		CallsTo(arrangement.ClientFactory, nameof(IApplicationClientFactory.CreateClient)).Should().Be(1,
			because: "one installation must log in once instead of once per step");
		// Order matters and NSubstitute's InOrder check cannot carry a reason: the session must be prepared
		// (StickySession obtained) before the first upload chunk, or that chunk lands on a common node.
		Received.InOrder(() => {
			arrangement.Guard.Prepare(arrangement.Session, arrangement.EnvironmentSettings);
			arrangement.Session.UploadFile(Arg.Any<string>(), arrangement.PackagePath);
		});
		arrangement.LogProvider.ReceivedCalls()
			.Select(call => call.GetArguments()[0])
			.Should().NotBeEmpty(because: "the installation log is read during the installation")
			.And.OnlyContain(client => ReferenceEquals(client, arrangement.Session),
				because: "the log is kept per node, so it must be read through the installation session");
		object[] restartArguments = arrangement.Application.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IApplication.Restart))
			.Select(call => call.GetArguments())
			.Should().ContainSingle(because: "a successful .NET installation restarts the application once")
			.Subject;
		restartArguments[0].Should().BeSameAs(arrangement.Session,
			because: "the restart must reach the node that installed the package, through the same session");
		restartArguments[1].Should().BeSameAs(arrangement.EnvironmentSettings,
			because: "the restart targets the environment of this installation, not the one the installer was built with");
		arrangement.Guard.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IDesignNodeSessionGuard.Prepare))
			.Select(call => call.GetArguments()[1])
			.Should().ContainSingle(because: "the session is prepared once per installation")
			.Which.Should().BeSameAs(arrangement.EnvironmentSettings,
				because: "the warm-up and the design-node probe must target the environment the package goes to");
		// The installation must not return while the restarted design node of a web farm is still going down,
		// or the next installation of the pipeline starts on it; InOrder cannot carry a reason.
		Received.InOrder(() => {
			arrangement.Application.Restart(arrangement.Session, arrangement.EnvironmentSettings);
			arrangement.Guard.WaitForRestartedDesignNode(arrangement.Session, arrangement.EnvironmentSettings);
		});
	}

	[Test]
	[Description("ENG-102757: when the session cannot be prepared (for example the login is rejected), nothing is uploaded and the failure reaches the caller.")]
	public void Install_ShouldNotUpload_WhenSessionPreparationFails() {
		// Arrange
		InstallerArrangement arrangement = ArrangeInstaller(installResponse: "{\"success\":true}");
		arrangement.Guard
			.When(guard => guard.Prepare(arrangement.Session, arrangement.EnvironmentSettings))
			.Do(_ => throw new UnauthorizedAccessException("Unauthorized Supervisor"));

		// Act
		Action act = () => arrangement.Installer.Install(arrangement.PackagePath, arrangement.EnvironmentSettings);

		// Assert
		act.Should().Throw<UnauthorizedAccessException>(
			because: "a failed login surfaces exactly as it did on the first upload chunk before");
		CallsTo(arrangement.Session, nameof(IApplicationClient.UploadFile)).Should().Be(0,
			because: "nothing may be uploaded through a session that could not be prepared");
	}

	[Test]
	[Description("ENG-102757: a failed installation does not restart the application.")]
	public void Install_ShouldNotRestartApplication_WhenInstallationFails() {
		// Arrange
		InstallerArrangement arrangement = ArrangeInstaller(installResponse: "{\"success\":false}");

		// Act
		bool result = arrangement.Installer.Install(arrangement.PackagePath, arrangement.EnvironmentSettings);

		// Assert
		result.Should().BeFalse(because: "the installation service reported a failure");
		CallsTo(arrangement.Application, nameof(IApplication.Restart)).Should().Be(0,
			because: "a failed installation must not restart the application");
		CallsTo(arrangement.Guard, nameof(IDesignNodeSessionGuard.WaitForRestartedDesignNode)).Should().Be(0,
			because: "without a restart there is nothing to wait for");
	}

	[Test]
	[Description("ENG-102757 EC-4: a restart whose answer is lost (a connection reset by the stopping node) may still have happened, so the installation still waits for the restarted design node and still succeeds.")]
	public void Install_ShouldStillWaitForTheDesignNode_WhenTheRestartCallFails() {
		// Arrange
		InstallerArrangement arrangement = ArrangeInstaller(installResponse: "{\"success\":true}");
		arrangement.Application
			.When(application => application.Restart(arrangement.Session, arrangement.EnvironmentSettings))
			.Do(_ => throw new System.Net.Http.HttpRequestException("connection reset"));

		// Act
		bool result = arrangement.Installer.Install(arrangement.PackagePath, arrangement.EnvironmentSettings);

		// Assert
		result.Should().BeTrue(because: "the package is installed; only the restart answer was lost");
		arrangement.Logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteLine))
			.Select(call => (string)call.GetArguments()[0])
			.Should().Contain(line => line.Contains("Error while restarting application"),
				because: "the arranged restart failure must actually have been raised and reported");
		CallsTo(arrangement.Guard, nameof(IDesignNodeSessionGuard.WaitForRestartedDesignNode)).Should().Be(1,
			because: "the next installation must not start on a design node that may be stopping");
	}

	[Test]
	[Description("ENG-102757: a failure while waiting for the restarted design node is reported as a warning and does not fail an installation that already succeeded.")]
	public void Install_ShouldWarnAndSucceed_WhenWaitingForTheDesignNodeFails() {
		// Arrange
		InstallerArrangement arrangement = ArrangeInstaller(installResponse: "{\"success\":true}");
		arrangement.Guard
			.When(guard => guard.WaitForRestartedDesignNode(arrangement.Session, arrangement.EnvironmentSettings))
			.Do(_ => throw new System.Net.Http.HttpRequestException("gateway unreachable"));

		// Act
		bool result = arrangement.Installer.Install(arrangement.PackagePath, arrangement.EnvironmentSettings);

		// Assert
		result.Should().BeTrue(because: "the package is installed before the wait starts");
		arrangement.Logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning))
			.Select(call => (string)call.GetArguments()[0])
			.Should().ContainSingle(because: "the operator must learn that the design node was not confirmed")
			.Which.Should().Contain("Could not wait for the design node",
				because: "the warning names the step that failed");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("ENG-102757: on .NET Framework the restart that developer mode asks for goes through the installation session and is followed by the wait for the design node, but only after a successful installation.")]
	public void Install_ShouldRestartThroughTheSessionAndWait_WhenDeveloperModeIsOnOnNetFramework(bool succeeds) {
		// Arrange
		InstallerArrangement arrangement =
			ArrangeInstaller(installResponse: succeeds ? "{\"success\":true}" : "{\"success\":false}");
		arrangement.EnvironmentSettings.IsNetCore = false;
		arrangement.EnvironmentSettings.DeveloperModeEnabled = true;
		int expectedCalls = succeeds ? 1 : 0;

		// Act
		bool result = arrangement.Installer.Install(arrangement.PackagePath, arrangement.EnvironmentSettings);

		// Assert
		result.Should().Be(succeeds, because: "the installation service decides the outcome");
		CallsTo(arrangement.Application, nameof(IApplication.Restart)).Should().Be(expectedCalls,
			because: "developer mode restarts .NET Framework after a successful installation only");
		CallsTo(arrangement.Guard, nameof(IDesignNodeSessionGuard.WaitForRestartedDesignNode)).Should().Be(expectedCalls,
			because: "every restart is followed by the wait for the restarted design node");
		if (succeeds) {
			// The order matters and NSubstitute's InOrder check cannot carry a reason: the wait must follow the
			// restart it waits for, through the same session.
			Received.InOrder(() => {
				arrangement.Application.Restart(arrangement.Session, arrangement.EnvironmentSettings);
				arrangement.Guard.WaitForRestartedDesignNode(arrangement.Session, arrangement.EnvironmentSettings);
			});
		}
	}

	private static int CallsTo(object substitute, string methodName) =>
		substitute.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == methodName);

	private InstallerArrangement ArrangeInstaller(string installResponse) {
		string packagePath = "T:\\Package.gz";
		FileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
		// The installer is built for one environment and installs into another, the way WorkspaceInstaller and
		// apply-environment-manifest use it, so a step that reads the constructor's settings is caught.
		EnvironmentSettings constructorSettings = new() { IsNetCore = true, Uri = "http://constructor-environment" };
		EnvironmentSettings environmentSettings = new() { IsNetCore = true, Uri = "http://install-environment" };
		IOwnedApplicationClient session = Substitute.For<IOwnedApplicationClient>();
		session.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns(installResponse);
		IApplicationClientFactory clientFactory = Substitute.For<IApplicationClientFactory>();
		clientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(session);
		IApplicationLogProvider logProvider = Substitute.For<IApplicationLogProvider>();
		logProvider.GetInstallationLog(Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>())
			.Returns("baseline\r\n");
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => callInfo.ArgAt<string>(0));
		IApplication application = Substitute.For<IApplication>();
		IDesignNodeSessionGuard guard = Substitute.For<IDesignNodeSessionGuard>();
		ILogger logger = Substitute.For<ILogger>();
		PackageInstaller installer = new(logProvider, constructorSettings, clientFactory, application,
			Substitute.For<IPackageArchiver>(), Substitute.For<ISqlScriptExecutor>(), serviceUrlBuilder,
			new FileSystem(FileSystem), logger, Substitute.For<IPackageLockManager>(), guard);
		return new InstallerArrangement(installer, environmentSettings, packagePath, session, clientFactory,
			logProvider, application, guard, logger);
	}

	private sealed record InstallerArrangement(PackageInstaller Installer, EnvironmentSettings EnvironmentSettings,
		string PackagePath, IOwnedApplicationClient Session, IApplicationClientFactory ClientFactory,
		IApplicationLogProvider LogProvider, IApplication Application, IDesignNodeSessionGuard Guard, ILogger Logger);
}
