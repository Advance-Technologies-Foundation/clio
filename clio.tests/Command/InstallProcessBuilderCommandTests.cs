using System;
using System.IO;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ClearExtensions;
using NSubstitute.Core;
using NUnit.Framework;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class InstallProcessBuilderCommandTests : BaseCommandTests<InstallProcessBuilderOptions> {

	#region Fields: Private

	private const string ClioRoot = "clio-root";

	// The archive's own descriptor is what the real catalog would read; there is no archive on the
	// substituted file system, so the catalog is substituted too and this is the version it reports.
	private const string ShippedVersion = "1.0.0.0";

	private IPackageInstaller _packageInstaller;
	private IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private IBundledPackageCatalog _bundledPackageCatalog;
	private IRequiredPackageChecker _requiredPackageChecker;
	private IFileSystem _fileSystem;
	private IPackageInstallOutcomeVerifier _outcomeVerifier;
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
		_outcomeVerifier = Substitute.For<IPackageInstallOutcomeVerifier>();
		_serverReadinessWaiter = Substitute.For<IServerReadinessWaiter>();
		_logger = Substitute.For<ILogger>();
		_workingDirectoriesProvider.ExecutingDirectory.Returns(ClioRoot);
		// Happy-path defaults, so each test only arranges the deviation it is actually about: the bundled
		// artifact is present, the instance comes back, and the package is operational afterwards. The
		// environment is arranged as carrying NOTHING - the first-install flow - which is what every
		// pre-existing test here assumes; the command does now ask, to decide whether it would downgrade.
		// _fileSystem and _workingDirectoriesProvider are CONTAINER plumbing, not inputs to this command any
		// more: archive presence is asked through the catalog (which is substituted below), so neither of them
		// can influence a verdict here. They stay registered because other services in the graph resolve them.
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		// HOW the outcome is established is the verifier's business and is tested in its own fixture; this
		// fixture only cares that the command asks the question at the right moment and obeys the answer.
		_outcomeVerifier
			.IsPackageOperational(Arg.Any<string>(), out string _)
			.Returns(call => {
				call[1] = null;
				return true;
			});
		_serverReadinessWaiter.WaitForReady(Arg.Any<ServerReadinessOptions>()).Returns(true);
		// Both halves of the downgrade comparison are substituted so a test can state the one it is about.
		// Default: clio ships ShippedVersion and the environment has nothing — the first-install flow, which
		// is what every pre-existing test in this fixture assumes.
		_bundledPackageCatalog = Substitute.For<IBundledPackageCatalog>();
		_bundledPackageCatalog.GetArchivePath(BundledPackages.ProcessBuilderPackageName)
			.Returns(ExpectedPackagePath);
		_bundledPackageCatalog.ArchiveExists(BundledPackages.ProcessBuilderPackageName).Returns(true);
		_bundledPackageCatalog
			.TryGetVersion(BundledPackages.ProcessBuilderPackageName, out Arg.Any<PackageVersion>(),
				out Arg.Any<string>())
			.Returns(call => {
				call[1] = PackageVersion.ParseVersion(ShippedVersion);
				call[2] = null;
				return true;
			});
		_requiredPackageChecker = Substitute.For<IRequiredPackageChecker>();
		containerBuilder.AddSingleton(_packageInstaller);
		containerBuilder.AddSingleton(_workingDirectoriesProvider);
		containerBuilder.AddSingleton(_bundledPackageCatalog);
		containerBuilder.AddSingleton(_requiredPackageChecker);
		containerBuilder.AddSingleton(_fileSystem);
		containerBuilder.AddSingleton(_outcomeVerifier);
		containerBuilder.AddSingleton(_serverReadinessWaiter);
		containerBuilder.AddSingleton(_logger);
	}

	private void ArrangeShippedVersion(string version) =>
		_bundledPackageCatalog
			.TryGetVersion(BundledPackages.ProcessBuilderPackageName, out Arg.Any<PackageVersion>(),
				out Arg.Any<string>())
			.Returns(call => {
				call[1] = PackageVersion.ParseVersion(version);
				call[2] = null;
				return true;
			});

	private void ArrangeUnreadableShippedVersion(string diagnosis) =>
		_bundledPackageCatalog
			.TryGetVersion(BundledPackages.ProcessBuilderPackageName, out Arg.Any<PackageVersion>(),
				out Arg.Any<string>())
			.Returns(call => {
				call[1] = null;
				call[2] = diagnosis;
				return false;
			});

	private void ArrangeInstalledVersion(string version) =>
		_requiredPackageChecker.GetInstalledVersion(BundledPackages.ProcessBuilderPackageName)
			.Returns(PackageVersion.ParseVersion(version));

	private void ArrangeSuccessfulInstall() =>
		_packageInstaller
			.Install(ExpectedPackagePath, Arg.Any<EnvironmentSettings>(), packageInstallOptions: null,
				reportPath: null, createBackup: true)
			.Returns(true);

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
		_outcomeVerifier.ClearReceivedCalls();
		_serverReadinessWaiter.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		// Belt-and-braces only, and worth saying why so nobody reasons from a hazard that is not there:
		// BaseClioModuleTests.Setup rebuilds the container on EVERY [SetUp] and re-runs AdditionalRegistrations,
		// so every substitute field above is a fresh instance per test and nothing can leak between them. The
		// genuinely shared state is EnvironmentSettings, reset immediately above - it is a fixture field set
		// outside AdditionalRegistrations, so it is the one thing that survives.
		_requiredPackageChecker.ClearSubstitute(ClearOptions.All);
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
		_outcomeVerifier.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IPackageInstallOutcomeVerifier.IsPackageOperational))
			.Should().Be(1,
				because: "the question is asked once, and only after the install: the package ships without an "
					+ "assembly, so nothing before the install can answer it. The one pre-install check the "
					+ "command does make is the downgrade guard, which asks a different question entirely");
		// NSubstitute's Received() takes no `because`; stated here. The command must name the package it is
		// asking about, because the diagnosis the verifier writes on failure quotes it — the verdict itself is
		// liveness, so no version is passed: what the target compiled is not readable back out of it.
		_outcomeVerifier.Received().IsPackageOperational(
			BundledPackages.ProcessBuilderPackageName, out string _);
		// Target only, not the timing budget: the command deliberately takes ServerReadinessOptions' default,
		// so asserting the value here would be a third copy of a number the code intentionally does not own.
		_serverReadinessWaiter.Received(1).WaitForReady(Arg.Is<ServerReadinessOptions>(o =>
			o.Uri == EnvironmentSettings.Uri && o.IsNetCore == EnvironmentSettings.IsNetCore));
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
		_outcomeVerifier.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IPackageInstallOutcomeVerifier.IsPackageOperational))
			.Should().Be(0,
				because: "verification must not run against a restarting instance: it races the restart in both "
					+ "directions — it can fail while the app warms up, and on an upgrade the outgoing app "
					+ "domain can answer with the OLD assembly and produce a false pass");
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
		// Not operational, and the verifier has nothing more specific to say — the shape it reports when the
		// service simply does not answer. HOW it reaches that verdict is its own fixture's business.
		_outcomeVerifier
			.IsPackageOperational(Arg.Any<string>(), out string _)
			.Returns(call => {
				call[1] = null;
				return false;
			});

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1,
			because: "'installed' and 'working' are different states when the target compiles the package; "
				+ "reporting success here would hide the one failure mode that is otherwise silent — the "
				+ "package present, the name-based gate satisfied, and every service call failing. Reporting 0 "
				+ "would also make the documented remediation a dead end: the recorded version satisfies the "
				+ "gate, so it stops emitting the hint that sends the caller here");
		// NSubstitute's Received() takes no `because`; the reason is stated here instead. A silent service after
		// an install means the target did not compile the package, so the message must send the caller to the
		// configuration build log — the one place that says why.
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("configuration build log")));
		_logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError))
			.Should().Be(1, because: "with no diagnosis from the verifier the command owns the whole report, so "
				+ "there is exactly one error line. The second line — the cause, carrying the WebException "
				+ "status / HTTP code — is written by the verifier and is asserted in its own fixture");
	}

	[Test]
	[Description("Execute should report the verifier's own diagnosis instead of its generic build-failure message when the verifier supplies one.")]
	public void Execute_ShouldReportTheVerifierDiagnosis_WhenTheVerifierSuppliesOne() {
		// Arrange
		const string diagnosisFromVerifier =
			"CrtProcessBuilder was installed and ProcessDesignService is responding, but it rejected the check";
		_packageInstaller
			.Install(
				Arg.Any<string>(),
				Arg.Any<EnvironmentSettings>(),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true)
			.Returns(true);
		_outcomeVerifier
			.IsPackageOperational(Arg.Any<string>(), out string _)
			.Returns(call => {
				call[1] = diagnosisFromVerifier;
				return false;
			});

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1,
			because: "not operational is a failure however precisely it is explained — the command could not "
				+ "establish that the package works, and reporting 0 would call an unusable environment ready");
		// NSubstitute's Received() takes no `because`; stated here. The generic message sends the reader to the
		// configuration build log, which is exactly wrong when the verifier already knows the build was fine and
		// the failure is inside the running package (typically authorization). Preferring the diagnosis is the
		// difference between a clean log the reader is told to inspect and the actual cause.
		_logger.Received().WriteError(diagnosisFromVerifier);
		_logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError)
				&& (call.GetArguments()[0] as string)!.Contains("configuration build log"))
			.Should().Be(0,
				because: "the generic build-failure message must be SUPPRESSED, not appended, when a diagnosis "
					+ "exists — two contradicting explanations are worse than the wrong one alone");
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
				because: "there is no per-runtime archive to choose between because there is no ASSEMBLY at all - this package ships as SOURCE and the target compiles it, so one archive serves both runtimes. The two-Files/Bin shape this comment used to describe is CLIOGATE's, from the other column of the comparison table in docs/agent-instructions/bundled-packages.md, and BundledArchive_ShouldNotCarryACompiledAssembly bans it here");
	}

	[Test]
	[Description("Execute should fail with a clear message when the clio installation does not carry the bundled archive.")]
	public void Execute_ShouldFailWithoutInstalling_WhenBundledArchiveIsMissing() {
		// Arrange
		_bundledPackageCatalog.ArchiveExists(BundledPackages.ProcessBuilderPackageName).Returns(false);

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
		_outcomeVerifier.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IPackageInstallOutcomeVerifier.IsPackageOperational))
			.Should().Be(0,
				because: "there is nothing to verify once the package never installed, and asking anyway would "
					+ "report the install failure as a service failure");
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
		_outcomeVerifier.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IPackageInstallOutcomeVerifier.IsPackageOperational))
			.Should().Be(0,
				because: "a throwing install must not proceed to the outcome check");
	}

	[Test]
	[Description("Refuses without installing when the environment carries a NEWER version than this clio ships, because nothing else stops a downgrade — not the installer, which never compares, and not the platform, which rewrites the recorded version whenever the descriptor timestamp merely DIFFERS.")]
	public void Execute_ShouldRefuseWithoutInstalling_WhenItWouldDowngradeTheEnvironment() {
		// Arrange
		ArrangeInstalledVersion("9.9.9.9");
		// Arranged to SUCCEED, so exit 1 can only come from the refusal. Without this the unarranged
		// installer returns false and the command exits 1 through the install-failed branch anyway - the
		// assertion would then pass whether or not the guard exists.
		ArrangeSuccessfulInstall();

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1, because: "rolling the package back for everyone on that environment is not a "
			+ "thing an install should do silently");
		_packageInstaller.DidNotReceive().Install(
			Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), Arg.Any<PackageInstallOptions>(),
			Arg.Any<string>(), Arg.Any<bool>());
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("9.9.9.9") && message.Contains("--force")));
	}

	[Test]
	[Description("Installs when the environment is BEHIND the version this clio ships — the single most common invocation, because the process-designer gate's refusal sends the caller straight here. One revision behind, not a wide gap: a comparison that only inspected Major would pass every other case in this fixture and still allow the shape a rebundle actually produces.")]
	[TestCase("0.9.9.9", TestName = "behind in every part")]
	[TestCase("1.0.0.0", TestName = "exactly equal - the repair path")]
	public void Execute_ShouldInstall_WhenTheEnvironmentIsNotAhead(string installedVersion) {
		// Arrange
		ArrangeInstalledVersion(installedVersion);
		ArrangeSuccessfulInstall();

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(0, because: "this is the flow the gate's refusal creates; blocking it would leave "
			+ "the user with a refusal whose named remedy also refuses");
		_packageInstaller.Received(1).Install(
			ExpectedPackagePath, Arg.Any<EnvironmentSettings>(), null, null, true);
	}

	[Test]
	[Description("Refuses a downgrade of a SINGLE revision, which is the only shape a rebundle actually produces — the 9.9.9.9 case differs in every part and so cannot tell a full comparison from one that inspects Major alone.")]
	public void Execute_ShouldRefuse_WhenTheEnvironmentIsOneRevisionAhead() {
		// Arrange
		ArrangeInstalledVersion("1.0.0.1");
		ArrangeSuccessfulInstall();

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1, because: "1.0.0.1 -> 1.0.0.0 is a rollback, and the runbook bumps exactly that "
			+ "part on every rebundle");
		_packageInstaller.DidNotReceive().Install(
			Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), Arg.Any<PackageInstallOptions>(),
			Arg.Any<string>(), Arg.Any<bool>());
	}

	[Test]
	[Description("A pre-release recorded in the environment is NOT newer than the release this clio ships, so installing the GA over an -rc proceeds. PackageVersion's own comparison says the opposite — it ranks an empty suffix BELOW a non-empty one — and using it would strand the caller: 'update clio' cannot help when clio already ships the release, and --force is unavailable over MCP.")]
	public void Execute_ShouldInstall_WhenTheEnvironmentCarriesAPreReleaseOfTheShippedVersion() {
		// Arrange
		ArrangeInstalledVersion($"{ShippedVersion}-rc");
		ArrangeSuccessfulInstall();

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(0,
			because: "SemVer orders a pre-release below its own release, so the GA is an upgrade over the rc");
		_packageInstaller.Received(1).Install(
			ExpectedPackagePath, Arg.Any<EnvironmentSettings>(), null, null, true);
	}

	[Test]
	[Description("Installing a PRE-RELEASE over the release of the same version is a rollback and must be refused — the other half of the same inversion, and the one that would have let the guard's own failure mode through silently.")]
	public void Execute_ShouldRefuse_WhenShippingAPreReleaseOverTheInstalledRelease() {
		// Arrange
		ArrangeShippedVersion($"{ShippedVersion}-rc");
		ArrangeInstalledVersion(ShippedVersion);
		ArrangeSuccessfulInstall();

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(1,
			because: "the environment holds the release and clio carries only a pre-release of it, so this "
				+ "install would move it backwards");
		_packageInstaller.DidNotReceive().Install(
			Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), Arg.Any<PackageInstallOptions>(),
			Arg.Any<string>(), Arg.Any<bool>());
	}

	[Test]
	[Description("The SHIPPED half of the comparison is read from the archive through IBundledPackageCatalog, never from a constant: the same installed version must produce opposite verdicts when only the catalog's answer changes. The ADR forbids a shipped-version constant, and a mutant that hardcoded 1.0.0.0 would pass every other test here.")]
	[TestCase("2.0.0.0", 0, TestName = "catalog ahead of the environment - installs")]
	[TestCase("1.0.0.0", 1, TestName = "catalog behind the environment - refuses")]
	public void Execute_ShouldTakeTheShippedVersionFromTheCatalog(string shippedVersion, int expected) {
		// Arrange
		ArrangeShippedVersion(shippedVersion);
		ArrangeInstalledVersion("1.5.0.0");
		ArrangeSuccessfulInstall();

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(expected,
			because: "only the catalog's answer differs between these two cases, so a verdict that did not "
				+ "come from the catalog could not change with it");
	}

	[Test]
	[Description("Installs, with a warning and no error, when clio cannot read the version out of its OWN archive — a distribution that cannot describe itself is clio's defect, and refusing would turn 'I could not check' into 'you may not proceed'.")]
	public void Execute_ShouldInstallWithAWarning_WhenTheShippedVersionCannotBeRead() {
		// Arrange
		const string diagnosis = "The bundled CrtProcessBuilder archive could not be read.";
		ArrangeUnreadableShippedVersion(diagnosis);
		// Would refuse if the comparison ran at all — that is what makes this test non-vacuous.
		ArrangeInstalledVersion("9.9.9.9");
		ArrangeSuccessfulInstall();

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(0, because: "clio's own broken archive must not block an install the environment "
			+ "is perfectly able to accept");
		_packageInstaller.Received(1).Install(
			ExpectedPackagePath, Arg.Any<EnvironmentSettings>(), null, null, true);
		// NSubstitute's Received takes no `because`; stated here. The warning is the only signal that the
		// guard was skipped, and the absence of an error is what distinguishes "skipped" from "refused".
		_logger.Received().WriteWarning(Arg.Is<string>(message => message.Contains(diagnosis)));
		_logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError))
			.Should().Be(0, because: "a skipped check is a warning; reporting it as an error would send the "
				+ "reader looking for a failure that did not happen");
	}

	[Test]
	[Description("--force installs anyway, because rolling a package back is legitimate for a bad release or a support repro — the refusal exists to make it deliberate, not impossible.")]
	public void Execute_ShouldInstall_WhenDowngradeIsForced() {
		// Arrange
		ArrangeInstalledVersion("9.9.9.9");
		ArrangeSuccessfulInstall();

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions { Force = true });

		// Assert
		result.Should().Be(0, because: "the operator asked for the rollback explicitly");
		_packageInstaller.Received(1).Install(
			ExpectedPackagePath, Arg.Any<EnvironmentSettings>(), null, null, true);
		// Not merely "it installed": --force must SKIP the check, not run it and ignore the verdict.
		// GetInstalledVersion is a real package-list fetch over HTTP against the target.
		_requiredPackageChecker.DidNotReceive().GetInstalledVersion(Arg.Any<string>());
		_bundledPackageCatalog.DidNotReceive().TryGetVersion(
			Arg.Any<string>(), out Arg.Any<PackageVersion>(), out Arg.Any<string>());
	}

	[Test]
	[Description("Reinstalling the version the environment already records must proceed: for a source-only package 'installed' and 'compiled' are different states, so this is the repair path for a package that never built.")]
	public void Execute_ShouldInstall_WhenTheEnvironmentCarriesTheSameVersion() {
		// Arrange
		ArrangeInstalledVersion(ShippedVersion);
		ArrangeSuccessfulInstall();

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(0,
			because: "refusing an equal version would block the one action that fixes a package which "
				+ "installed but never compiled");
		_packageInstaller.Received(1).Install(
			ExpectedPackagePath, Arg.Any<EnvironmentSettings>(), null, null, true);
	}

	[Test]
	[Description("Proceeds when the environment's version cannot be read at all, because 'I could not check' must not become 'you may not proceed' — the install fails on its own terms if the environment is genuinely unreachable.")]
	public void Execute_ShouldInstall_WhenTheInstalledVersionCannotBeRead() {
		// Arrange
		_requiredPackageChecker
			.GetInstalledVersion(BundledPackages.ProcessBuilderPackageName)
			.Returns(_ => throw new InvalidOperationException("the environment did not answer"));
		ArrangeSuccessfulInstall();

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(0,
			because: "a failed probe is not evidence of a downgrade, and blocking on it would make an "
				+ "unrelated transport failure look like a refusal");
		_packageInstaller.Received(1).Install(
			ExpectedPackagePath, Arg.Any<EnvironmentSettings>(), null, null, true);
		// The warning is the only trace that the guard was skipped; the Contains also pins that the readable
		// message is used rather than the bare exception type.
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("the environment did not answer")));
	}

	[Test]
	[Description("Proceeds when the package is absent, because there is nothing to move backwards — and this is the flow the process-designer gate's refusal sends people into.")]
	public void Execute_ShouldInstall_WhenThePackageIsAbsentFromTheEnvironment() {
		// Arrange
		_requiredPackageChecker
			.GetInstalledVersion(BundledPackages.ProcessBuilderPackageName)
			.Returns((PackageVersion)null);
		ArrangeSuccessfulInstall();

		// Act
		int result = _command.Execute(new InstallProcessBuilderOptions());

		// Assert
		result.Should().Be(0, because: "a first install is the command's primary purpose");
		_packageInstaller.Received(1).Install(
			ExpectedPackagePath, Arg.Any<EnvironmentSettings>(), null, null, true);
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
