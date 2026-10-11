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
	[Description("ENG-102757: upload, backup, install, log reads and restart of one installation all go through one session, prepared by the design-node guard before the upload.")]
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
		arrangement.Application.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IApplication.Restart))
			.Select(call => call.GetArguments().FirstOrDefault())
			.Should().Equal([arrangement.Session],
				because: "the restart must reach the node that installed the package, through the same session");
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
	}

	private static int CallsTo(object substitute, string methodName) =>
		substitute.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == methodName);

	private InstallerArrangement ArrangeInstaller(string installResponse) {
		string packagePath = "T:\\Package.gz";
		FileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
		EnvironmentSettings environmentSettings = new() { IsNetCore = true };
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
		PackageInstaller installer = new(logProvider, environmentSettings, clientFactory, application,
			Substitute.For<IPackageArchiver>(), Substitute.For<ISqlScriptExecutor>(), serviceUrlBuilder,
			new FileSystem(FileSystem), Substitute.For<ILogger>(), Substitute.For<IPackageLockManager>(), guard);
		return new InstallerArrangement(installer, environmentSettings, packagePath, session, clientFactory,
			logProvider, application, guard);
	}

	private sealed record InstallerArrangement(PackageInstaller Installer, EnvironmentSettings EnvironmentSettings,
		string PackagePath, IOwnedApplicationClient Session, IApplicationClientFactory ClientFactory,
		IApplicationLogProvider LogProvider, IApplication Application, IDesignNodeSessionGuard Guard);
}
