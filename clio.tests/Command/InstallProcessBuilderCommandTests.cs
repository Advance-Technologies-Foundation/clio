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

	/// <summary>A build older than the bundled one — what an environment reports before an upgrade.</summary>
	private const string PreviousBuildVersion = "1.0.0.0";

	private int _getVersionCallCount;

	private const string ListUserTasksUrl = "http://localhost/0/rest/ProcessDesignService/ListUserTasks";
	private const string GetVersionUrl = "http://localhost/0/rest/ProcessDesignService/GetVersion";

	/// <summary>A GetVersion envelope, as ProcessDesignService returns it.</summary>
	private static string BuildVersionResponse(string version) =>
		$"{{\"GetVersionResult\":{{\"success\":true,\"version\":\"{version}\"}}}}";

	private IPackageInstaller _packageInstaller;
	private IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private IFileSystem _fileSystem;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IServerReadinessWaiter _serverReadinessWaiter;
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
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serverReadinessWaiter = Substitute.For<IServerReadinessWaiter>();
		_logger = Substitute.For<ILogger>();
		_workingDirectoriesProvider.ExecutingDirectory.Returns(ClioRoot);
		// Happy-path defaults, so each test only arranges the deviation it is actually about: the bundled
		// artifact is present, the environment carries nothing yet, and the service answers after install.
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_serviceUrlBuilder
			.Build(ServiceUrlBuilder.KnownRoute.ListUserTasks)
			.Returns(ListUserTasksUrl);
		_serviceUrlBuilder
			.Build(ServiceUrlBuilder.KnownRoute.GetProcessBuilderVersion)
			.Returns(GetVersionUrl);
		// Route-specific, because the two checks are a preference order and not interchangeable: GetVersion is
		// asked first and ListUserTasks only answers for a package too old to offer it. A single any-URL stub
		// would make every test exercise the fallback.
		// GetVersion is asked TWICE on the happy path — before installing (may I skip?) and after (did it
		// compile?) — against the same route, so the default answers by call order: BEHIND first, CURRENT
		// after. A single fixed answer would make every test either short-circuit or report a failed upgrade.
		_getVersionCallCount = 0;
		_applicationClient
			.ExecutePostRequest(GetVersionUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => BuildVersionResponse(++_getVersionCallCount == 1
				? PreviousBuildVersion
				: BundledPackages.ProcessBuilderBuildVersion));
		_applicationClient
			.ExecutePostRequest(ListUserTasksUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ServiceAnswersResponse);
		_serverReadinessWaiter.WaitForReady(Arg.Any<ServerReadinessOptions>()).Returns(true);
		containerBuilder.AddSingleton(_packageInstaller);
		containerBuilder.AddSingleton(_workingDirectoriesProvider);
		containerBuilder.AddSingleton(_fileSystem);
		containerBuilder.AddSingleton(_applicationClient);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_serverReadinessWaiter);
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
		// EnvironmentSettings is a FIELD on the fixture instance and NUnit reuses that instance across tests
		// (default SingleInstance lifecycle), so a test that flips IsNetCore would otherwise decide the
		// runtime for every test declared after it - passing or failing by declaration order rather than by
		// the code under test.
		EnvironmentSettings.IsNetCore = false;
		_packageInstaller.ClearReceivedCalls();
		_fileSystem.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		_serverReadinessWaiter.ClearReceivedCalls();
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
			.Should().Be(2,
				because: "GetVersion is asked twice and both are load-bearing: BEFORE installing (may this be "
					+ "skipped?) and AFTER (did the target actually compile it?). Neither falls back to "
					+ "ListUserTasks, because GetVersion answered both times");
		_serviceUrlBuilder.Received().Build(ServiceUrlBuilder.KnownRoute.GetProcessBuilderVersion);
		_serverReadinessWaiter.Received(1).WaitForReady(Arg.Is<ServerReadinessOptions>(o =>
			o.Uri == EnvironmentSettings.Uri && o.IsNetCore == EnvironmentSettings.IsNetCore));
	}

	[Test]
	[Description("Fails when the service reports an older serving version than the one installed, which is the only detectable signature of an upgrade whose configuration build failed.")]
	public void Execute_ShouldFail_WhenServiceReportsAnOlderServingBuild() {
		// Arrange
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
			Arg.Any<PackageInstallOptions>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
		_applicationClient
			.ExecutePostRequest(GetVersionUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(BuildVersionResponse(PreviousBuildVersion));

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1,
			because: "the platform recorded the new version when it ACCEPTED the archive and then kept serving "
				+ "the assembly from its last successful build, so the package list will show the new version "
				+ "either way — reporting success here would leave the gate satisfied while the old code runs");
		_applicationClient.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest))
			.Should().Be(2,
				because: "the version is asked once BEFORE installing (an older build is the normal reason to "
					+ "install, so that call is silent) and once after; a definite older version is an answer "
					+ "rather than a missing route, so neither call falls back to ListUserTasks, which the old "
					+ "assembly would happily pass");
		_logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError))
			.Should().Be(1,
				because: "only the POST-install mismatch is a failure worth reporting; the pre-install one must "
					+ "stay silent or every ordinary upgrade would open with 'the build FAILED'");
	}

	[Test]
	[Description("Falls back to the ListUserTasks check when the environment carries a package that predates GetVersion, so upgrading the very environments that need it is not refused.")]
	public void Execute_ShouldFallBackToListUserTasks_WhenGetVersionRouteIsAbsent() {
		// Arrange
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
			Arg.Any<PackageInstallOptions>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
		_applicationClient
			.ExecutePostRequest(GetVersionUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html><body>404 Not Found</body></html>");

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(0,
			because: "GetVersion shipped in package 1.1.0.0, so every environment installed before it answers "
				+ "the route with an IIS error page; treating that as a failure would refuse to upgrade exactly "
				+ "the environments that need upgrading");
		_serviceUrlBuilder.Received().Build(ServiceUrlBuilder.KnownRoute.ListUserTasks);
		_applicationClient.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest))
			.Should().Be(3,
				because: "the version route is asked before installing and again after (2), and only the second "
					+ "one falls back to the weaker proof-of-life (3) — the pre-install probe has nothing to "
					+ "fall back to, since an unanswerable route there just means 'install'");
	}

	[Test]
	[Description("Execute should wait for the platform's own post-install restart before probing, and fail without probing when the instance does not come back.")]
	public void Execute_ShouldFailWithoutProbing_WhenInstanceDoesNotBecomeReady() {
		// Arrange
		_packageInstaller
			.Install(
				Arg.Any<string>(),
				Arg.Any<EnvironmentSettings>(),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true)
			.Returns(true);
		_serverReadinessWaiter.WaitForReady(Arg.Any<ServerReadinessOptions>()).Returns(false);

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1, because: "an instance that never came back cannot be reported as a success");
		_applicationClient.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest))
			.Should().Be(1,
				because: "only the PRE-install version probe may have run; the outcome probe must not, because "
					+ "probing a restarting instance races the restart in both directions: it can fail while "
					+ "the app warms up, and on an upgrade the outgoing app domain can answer with the OLD "
					+ "assembly and produce a false pass");
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
	[Description("Execute should skip the install when the environment is already running the build clio bundles, as reported by the service itself.")]
	public void Execute_ShouldSkipInstall_WhenEnvironmentAlreadyRunsTheBundledBuild() {
		// Arrange — override the by-call-order default: this environment is current from the first probe
		_applicationClient
			.ExecutePostRequest(GetVersionUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(BuildVersionResponse(BundledPackages.ProcessBuilderBuildVersion));

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(0, because: "an already-current environment whose service answers needs no work");
		_packageInstaller.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IPackageInstaller.Install))
			.Should().Be(0,
				because: "reinstalling an identical package would make the environment recompile it for nothing");
		_serverReadinessWaiter.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IServerReadinessWaiter.WaitForReady))
			.Should().Be(0,
				because: "nothing was installed on this path, so nothing restarted and there is nothing to wait "
					+ "for — paying the readiness InitialDelay here would add 10 seconds to a no-op");
	}

	[Test]
	[Description("Reports a failure when an install succeeds but the service still does not answer at all, pointing the caller at the configuration build log.")]
	public void Execute_ShouldFail_WhenServiceDoesNotAnswerAtAll() {
		// Arrange
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
			Arg.Any<PackageInstallOptions>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
		_applicationClient
			.ExecutePostRequest(GetVersionUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html>404</html>");
		_applicationClient
			.ExecutePostRequest(ListUserTasksUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html>404</html>");

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1,
			because: "this is the state the whole command exists to detect — a previous install whose "
				+ "configuration build failed leaves the version recorded, so the name-and-version gate stops "
				+ "emitting its hint and the designer commands fail with raw service errors. Reporting 0 here "
				+ "made the documented remediation a dead end");
		// NSubstitute's Received() takes no `because`; the reason is stated here instead. After an install, a
		// silent service means the target did not compile the package, so the message has to send the caller
		// to the configuration build log — the one place that says why. (It deliberately does NOT suggest
		// --force: the short-circuit is asked of the SERVICE now, so a broken installation never satisfies it
		// and simply gets reinstalled on the next run without any flag.)
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("configuration build log")));
	}

	[Test]
	[Description("Installs even when the environment already runs the bundled build, when force is requested, so a broken installation can be repaired.")]
	public void Execute_ShouldInstall_WhenForceRequestedDespiteCurrentBuild() {
		// Arrange — the environment is already current, so the short-circuit WOULD fire without force
		_applicationClient
			.ExecutePostRequest(GetVersionUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(BuildVersionResponse(BundledPackages.ProcessBuilderBuildVersion));
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
			Arg.Any<PackageInstallOptions>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
		_serverReadinessWaiter.WaitForReady(Arg.Any<ServerReadinessOptions>()).Returns(true);

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions { Force = true });

		// Assert
		result.Should().Be(0, because: "the forced reinstall succeeded and the service answers afterwards");
		_packageInstaller.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IPackageInstaller.Install))
			.Should().Be(1,
				because: "force exists precisely to bypass the compatible-version short-circuit; if it did not "
					+ "reach the installer the flag would parse, be accepted, and silently do nothing");
		_applicationClient.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest))
			.Should().Be(1,
				because: "force skips the pre-install version QUESTION, not just its answer — one probe is left, "
					+ "the post-install one; asking first would cost a round-trip whose result cannot change "
					+ "anything");
	}

	[Test]
	[Description("Execute should install anyway when the pre-install version probe throws, because that check fails open: it must never block an explicitly requested install.")]
	public void Execute_ShouldInstallAnyway_WhenPreInstallVersionProbeThrows() {
		// Arrange
		bool firstProbe = true;
		_applicationClient
			.ExecutePostRequest(GetVersionUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => {
				if (firstProbe) {
					firstProbe = false;
					throw new InvalidOperationException("host unreachable");
				}
				return BuildVersionResponse(BundledPackages.ProcessBuilderBuildVersion);
			});
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
			because: "an unreachable host during the pre-install probe must not block an explicitly "
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
			.Should().Be(1,
				because: "only the PRE-install version probe may have run; there is nothing to verify once the "
					+ "package never installed, and probing anyway would report the install failure as a "
					+ "service failure");
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
		// Assert the ORDER, which is the whole point of the name: the readable message carries the HTTP
		// status / WebException reason, and a stack printed first buries it. Comparing the recorded call
		// indexes is the only way to see it - a Received() check passes for either order.
		System.Collections.Generic.List<string> errors = _logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError))
			.Select(call => call.GetArguments()[0] as string)
			.ToList();
		errors.Should().HaveCountGreaterThanOrEqualTo(1,
			because: "a failed install must report something");
		errors[0].Should().Contain("upload rejected",
			because: "the readable message must come FIRST; push-pkg loses this information by printing the "
				+ "bare stack, which is the behaviour this ordering exists to avoid");
		_applicationClient.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest))
			.Should().Be(1,
				because: "only the PRE-install version probe may have run; a throwing install must not proceed "
					+ "to the outcome check");
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
