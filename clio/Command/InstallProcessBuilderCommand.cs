using System;
using System.IO;
using System.Text.Json;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Command-line options for installing or updating the bundled process-builder package.
/// </summary>
/// <remarks>
/// This options class deliberately carries neither <c>[RequiresPackage]</c> nor <c>[FeatureToggle]</c>.
/// <list type="bullet">
/// <item><description>
/// A <c>[RequiresPackage]</c> here would be self-defeating: both dispatch chokepoints enforce package
/// requirements BEFORE the command runs, so the installer would be refused by the very requirement it
/// exists to satisfy.
/// </description></item>
/// <item><description>
/// A <c>[FeatureToggle]</c> would make the remediation unreachable. A gated options type is filtered out
/// of the verb parse array, so the verb becomes indistinguishable from a typo — and the hint on the
/// process-designer commands points users straight at this verb.
/// </description></item>
/// </list>
/// </remarks>
[Verb("install-process-builder", Aliases = ["update-process-builder", "installprocessbuilder"],
	HelpText = "Install or update the bundled process-builder package in Creatio")]
public class InstallProcessBuilderOptions : EnvironmentNameOptions { }

/// <summary>
/// Installs the bundled process-builder package into a Creatio environment, making
/// <c>ProcessDesignService</c> reachable there.
/// </summary>
/// <remarks>
/// Modelled on <see cref="InstallGateCommand"/>, with four deliberate differences that the on-stand
/// experiments justified:
/// <list type="number">
/// <item><description>
/// <b>No <c>IsNetCore</c> branch and no per-framework archive.</b> The package ships as SOURCE ONLY —
/// with no compiled assembly — and the target compiles it against its own core, choosing the target
/// framework for its own host. Verified with the same bytes on .NET Framework 4.8 and .NET 8.0.29.
/// </description></item>
/// <item><description>
/// <b>No restart of OURS, but one still happens — and from a different place on each runtime.</b> Unlike
/// <see cref="InstallGateCommand"/> this command never asks for a restart, yet both live runs restarted:
/// on .NET Framework the platform recycled itself once the workspace assembly changed, and on .NET the
/// package installer issued the restart because the target is a .NET host. Both outlive the install call,
/// which is why <see cref="WaitForPlatformRestart"/> sits between installing and judging the result.
/// </description></item>
/// <item><description>
/// <b>The outcome is verified against the RUNNING assembly, not the install call and not the database.</b>
/// Because the assembly is produced by the target rather than shipped, "installed" and "working" are
/// genuinely different states, and the database cannot tell them apart: Creatio records a version when it
/// ACCEPTS an archive, keeps serving the assembly from its last successful configuration build, and does not
/// even update that record when re-installing a package it already has. So this command asks
/// <c>ProcessDesignService.GetVersion</c> which build is serving and fails when it is older than
/// <see cref="BundledPackages.ProcessBuilderBuildVersion"/>. The Application Hub can recover that class of
/// failure through its own <c>RestoreFromBackup</c> stage; this path has no such button, which is exactly why
/// the check belongs here.
/// </description></item>
/// <item><description>
/// <b>The bundled artifact's presence is checked first</b>, so a distribution that failed to carry it
/// says so plainly instead of surfacing as a generic failure from deep inside the installer, which has no
/// existence pre-check of its own.
/// </description></item>
/// </list>
/// An environment already RUNNING the bundled build short-circuits, so re-running the command does no work —
/// and in particular does not make the environment recompile the package for nothing. That test is the
/// service's own report rather than the recorded package version, for the reason above: the record does not
/// move on a re-install, so a database-based short-circuit would report "nothing to do" for exactly the
/// environment still running an old build.
/// </remarks>
public class InstallProcessBuilderCommand : Command<InstallProcessBuilderOptions> {

	#region Fields: Private

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IPackageInstaller _packageInstaller;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IFileSystem _fileSystem;
	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IServerReadinessWaiter _serverReadinessWaiter;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="InstallProcessBuilderCommand"/> class.
	/// </summary>
	/// <param name="environmentSettings">Resolved target environment settings.</param>
	/// <param name="packageInstaller">Package installer used to install the bundled archive.</param>
	/// <param name="workingDirectoriesProvider">Provider used to locate bundled clio assets.</param>
	/// <param name="fileSystem">File system used to verify the bundled archive is present.</param>
	/// <param name="applicationClient">Client used to prove the service answers after installation.</param>
	/// <param name="serviceUrlBuilder">Builder for the <c>ProcessDesignService</c> route.</param>
	/// <param name="serverReadinessWaiter">
	/// Waiter used to let the platform's self-triggered restart finish before the service is probed.
	/// </param>
	/// <param name="logger">Logger used for command output.</param>
	public InstallProcessBuilderCommand(
		EnvironmentSettings environmentSettings,
		IPackageInstaller packageInstaller,
		IWorkingDirectoriesProvider workingDirectoriesProvider,
		IFileSystem fileSystem,
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		IServerReadinessWaiter serverReadinessWaiter,
		ILogger logger) {
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		packageInstaller.CheckArgumentNull(nameof(packageInstaller));
		workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		applicationClient.CheckArgumentNull(nameof(applicationClient));
		serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
		serverReadinessWaiter.CheckArgumentNull(nameof(serverReadinessWaiter));
		logger.CheckArgumentNull(nameof(logger));
		_environmentSettings = environmentSettings;
		_packageInstaller = packageInstaller;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_fileSystem = fileSystem;
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_serverReadinessWaiter = serverReadinessWaiter;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	private EnvironmentSettings CreateInstallEnvironmentSettings() {
		EnvironmentSettings installEnvironmentSettings = new();
		installEnvironmentSettings.Merge(_environmentSettings);
		// Installing must never unlock maintainer packages: on an environment with developer mode on,
		// push-pkg's unlock step routes through cliogate and fails when that call is unavailable, even
		// though the package itself installed correctly.
		installEnvironmentSettings.DeveloperModeEnabled = false;
		return installEnvironmentSettings;
	}

	private string GetPackagePath() => Path.Combine(
		_workingDirectoriesProvider.ExecutingDirectory,
		BundledPackages.ProcessBuilderPackageName,
		BundledPackages.ProcessBuilderArchiveFileName);

	/// <summary>
	/// Determines whether the environment is ALREADY RUNNING the build this clio bundles.
	/// </summary>
	/// <returns><c>true</c> only when the service confirmed it; otherwise <c>false</c>.</returns>
	/// <remarks>
	/// Asks the service, not the database, and that is a correction rather than a preference. The obvious
	/// implementation — <c>IRequiredPackageChecker.IsCompatible</c> against the recorded package version —
	/// cannot answer this question, because Creatio does not update the recorded version when it re-installs a
	/// package it already has (it matches by <c>UId</c>). Verified on both runtimes: after installing the
	/// 1.1.0.0 archive, <c>GetVersion</c> reported 1.1.0.0 while <c>list-packages</c> still reported 1.0.0.0.
	/// A database-based short-circuit therefore either never fires (floor raised) or fires on an environment
	/// still running an old build (floor left alone) — the second being worse, since it reports "nothing to
	/// do" for exactly the environment that needs upgrading.
	/// <para>
	/// Fails OPEN, like the check it replaces: an unreachable host, an absent route, or a package too old to
	/// offer <c>GetVersion</c> all yield <c>false</c> here and fall through to installing, rather than
	/// aborting an explicitly requested install.
	/// </para>
	/// </remarks>
	private bool IsAlreadyRunningBundledBuild() =>
		DoesServiceReportCurrentBuild(reportOlderAsError: false, out _) == true;

	/// <summary>
	/// Proves that the <c>ProcessDesignService</c> build now serving is the one just shipped.
	/// </summary>
	/// <returns><c>true</c> when the service confirms it; otherwise <c>false</c>.</returns>
	/// <remarks>
	/// Fails CLOSED, unlike <see cref="IsAlreadyRunningBundledBuild"/> — this check IS the point of the
	/// command's contract. A successful install proves the package was accepted, not that the target compiled
	/// it: no assembly ships in the archive, so the only proof that the code exists and is loaded comes from
	/// the service itself.
	/// <para>
	/// Two-tier by necessity. <see cref="DoesServiceReportCurrentBuild"/> is the real check — it compares the
	/// version compiled INTO the serving assembly against the bundled one, which is the only thing that
	/// detects a failed upgrade. It returns <see langword="null"/> when the environment's package predates
	/// that operation, and only then does <see cref="DoesListUserTasksAnswer"/> supply the weaker
	/// proof-of-life.
	/// </para>
	/// <para>
	/// MUST be called only after <see cref="WaitForPlatformRestart"/>. Installing a package whose assembly
	/// changed makes the platform restart ITSELF — observed in a stand's <c>Application.log</c> as
	/// "Workspace assembly changed - Run restart application", with <c>Application_Start</c> following
	/// AFTER the install call had already returned success. Probing immediately therefore races the
	/// restart in both directions: it can fail while the app is still warming up (a false "did not
	/// compile"), and on an UPGRADE it can be answered by the outgoing app domain still serving the OLD
	/// assembly (a false pass).
	/// </para>
	/// </remarks>
	private bool DoesServiceAnswer(out string diagnosis) {
		diagnosis = null;
		bool? current = DoesServiceReportCurrentBuild(reportOlderAsError: true, out diagnosis);
		return current ?? DoesListUserTasksAnswer();
	}

	/// <summary>
	/// Asks the service which BUILD is serving, and compares it against the bundled version.
	/// </summary>
	/// <returns>
	/// <c>true</c> when the serving build is at least the bundled version; <c>false</c> when it is older;
	/// <see langword="null"/> when the environment carries a package too old to answer at all, so the caller
	/// must fall back.
	/// </returns>
	/// <remarks>
	/// This is the only check that can catch a failed UPGRADE. Installing writes the archive's descriptor
	/// version into <c>SysPackage</c> when the archive is ACCEPTED, and the platform keeps serving the
	/// assembly it last built successfully — so after an upgrade whose configuration build failed, the
	/// database reports the new version while the old code runs. No database read can see that: both sides of
	/// any such comparison come from the descriptor. <c>GetVersion</c> returns a constant compiled INTO the
	/// assembly, which only a build that actually compiled can carry.
	/// <para>
	/// Returns <see langword="null"/> rather than <c>false</c> for a missing route, and that distinction is
	/// the whole reason this method is nullable: <c>GetVersion</c> shipped in package 1.1.0.0, so an
	/// environment carrying 1.0.0.0 — including every environment installed before this clio — has no such
	/// operation and answers with an IIS error page. Treating that as a failure would refuse to upgrade
	/// exactly the environments that need upgrading.
	/// </para>
	/// </remarks>
	private bool? DoesServiceReportCurrentBuild(bool reportOlderAsError, out string diagnosis) {
		diagnosis = null;
		string reported;
		try {
			string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetProcessBuilderVersion);
			// ProcessDesignService uses BodyStyle=Wrapped: a parameterless operation accepts an empty object.
			string response = _applicationClient.ExecutePostRequest(url, "{}");
			using JsonDocument document = JsonDocument.Parse(response);
			if (!document.RootElement.TryGetProperty("GetVersionResult", out JsonElement result)
				|| !result.TryGetProperty("version", out JsonElement version)
				|| version.ValueKind != JsonValueKind.String) {
				return null;
			}
			reported = version.GetString();
		} catch (Exception) {
			// Absent route, HTML error page, unparseable body — all indistinguishable here from "this package
			// predates GetVersion", so all fall back rather than failing the install.
			return null;
		}
		if (!Version.TryParse(reported, out Version servingVersion)
			|| !Version.TryParse(BundledPackages.ProcessBuilderBuildVersion, out Version bundledVersion)) {
			_logger.WriteInfo(
				$"ProcessDesignService reported an unparseable version ('{reported}'); falling back to the "
				+ "ListUserTasks check.");
			return null;
		}
		if (servingVersion >= bundledVersion) {
			return true;
		}
		if (!reportOlderAsError) {
			// Pre-install: an older serving build is the NORMAL reason to be here, not a failure. Reporting it
			// as one would open every ordinary upgrade with an alarming "the build FAILED" error.
			return false;
		}
		// Returned rather than logged, so Execute emits exactly ONE message. Logging here and letting Execute
		// add its generic "does not answer" line produced two contradicting diagnoses for the same outcome —
		// and the generic one is the wrong of the two: the service DID answer, with the wrong version.
		diagnosis =
			$"{BundledPackages.ProcessBuilderPackageName} {BundledPackages.ProcessBuilderBuildVersion} was " +
			$"installed, but ProcessDesignService is still serving {reported}. The environment accepted the " +
			"package and recorded the new version, then kept running the assembly from its last successful " +
			"configuration build — so the build of the new sources FAILED. Check the environment's " +
			"configuration build log; the package list will show the new version regardless, which is why " +
			"this check exists.";
		return false;
	}

	/// <summary>
	/// Fallback proof of life for a package that predates <c>GetVersion</c>.
	/// </summary>
	/// <returns><c>true</c> only when the service returned a successful <c>ListUserTasks</c> envelope.</returns>
	/// <remarks>
	/// Weaker than <see cref="DoesServiceReportCurrentBuild"/> in two ways worth naming. It cannot tell WHICH
	/// build answered, so on an upgrade a still-serving old assembly passes it. And <c>ListUserTasks</c> is
	/// gated on <c>CanManageProcessDesign</c> inside the package, and the package returns a guard rejection as
	/// an UNSUCCESSFUL envelope — so an installer who may deploy packages but was never granted process-design
	/// rights fails this check on a perfectly good install. Both are why <c>GetVersion</c> exists and is
	/// ungated; this path only runs against a package too old to offer it.
	/// <para>
	/// The response is parsed rather than pattern-matched, because the interesting failure is an HTML error
	/// page from IIS when the route does not resolve — that fails <see cref="JsonDocument.Parse"/> and is
	/// correctly reported as "not answering", whereas a substring search over it could accidentally match.
	/// </para>
	/// </remarks>
	private bool DoesListUserTasksAnswer() {
		try {
			string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ListUserTasks);
			string response = _applicationClient.ExecutePostRequest(url, "{}");
			using JsonDocument document = JsonDocument.Parse(response);
			return document.RootElement.TryGetProperty("ListUserTasksResult", out JsonElement result)
				&& result.TryGetProperty("success", out JsonElement success)
				&& success.ValueKind == JsonValueKind.True;
		} catch (Exception e) {
			_logger.WriteInfo($"ProcessDesignService did not answer: {e.GetReadableMessageException()}");
			return false;
		}
	}

	/// <summary>
	/// Waits for the platform's own post-install restart to complete.
	/// </summary>
	/// <returns><c>true</c> when the instance answered its health check within the budget.</returns>
	/// <remarks>
	/// The restart is never ours, but it comes from a different place on each runtime — observed on both:
	/// on .NET Framework the PLATFORM recycles itself because the workspace assembly changed
	/// ("Workspace assembly changed - Run restart application"), while on .NET
	/// <c>BasePackageInstaller</c> issues it because <c>IsNetCore</c> is true. Passing
	/// <see cref="EnvironmentSettings.IsNetCore"/> below therefore matters twice: it selects the right
	/// health-check flavour (WebHost vs WebAppLoader) for the wait itself.
	/// <para>
	/// Reusing <see cref="IServerReadinessWaiter"/> rather than retrying the service probe is deliberate —
	/// its <c>InitialDelay</c> exists precisely because "the previous app domain may still answer briefly
	/// after a restart request", which is the false-pass this command must not report. A live net472 run
	/// showed the interleaving exactly: the platform logged its restart at 16:44:57,419, the install call
	/// returned at 16:44:57,842, and <c>Application_Start</c> followed at 16:44:58,735 — so an immediate
	/// probe would have landed inside the restart.
	/// </para>
	/// </remarks>
	/// <summary>
	/// Reports success for a target already SERVING the bundled build, WITHOUT installing.
	/// </summary>
	/// <returns>Always <c>0</c>.</returns>
	/// <remarks>
	/// Reached only when the service itself reported the bundled build, which is why this can be an
	/// unconditional success. The earlier shape of this method short-circuited on the RECORDED package version
	/// instead and had to re-probe to stay honest: a version in <c>SysPackage</c> proves the archive was
	/// accepted at some point, not that the target ever compiled it. Asking the service up front collapses
	/// both problems — an environment whose build failed no longer satisfies the short-circuit at all, so it
	/// falls through and gets reinstalled rather than being told "nothing to do".
	/// <para>
	/// No readiness wait on this path: nothing was installed, so nothing restarted.
	/// </para>
	/// </remarks>
	private int ReportAlreadyInstalled() {
		// No probe here, deliberately: this method is only reached because IsAlreadyRunningBundledBuild ALREADY
		// asked the service and got the bundled build back. That answer IS the proof — re-asking would spend a
		// second round-trip to learn what the caller of this method already established.
		_logger.WriteInfo(
			$"{BundledPackages.ProcessBuilderPackageName} " +
			$"{BundledPackages.ProcessBuilderBuildVersion} is already installed and serving on this " +
			"environment. Nothing to do.");
		return 0;
	}

	private bool WaitForPlatformRestart() =>
		_serverReadinessWaiter.WaitForReady(new ServerReadinessOptions {
			Uri = _environmentSettings.Uri,
			IsNetCore = _environmentSettings.IsNetCore
		});

	#endregion

	#region Methods: Public

	/// <summary>
	/// Executes the install-process-builder command.
	/// </summary>
	/// <param name="options">The parsed install-process-builder command options.</param>
	/// <returns>
	/// Returns 0 when a compatible version is already present, or when the package installed AND
	/// <c>ProcessDesignService</c> answers afterwards; otherwise, returns 1.
	/// </returns>
	public override int Execute(InstallProcessBuilderOptions options) {
		try {
			string packagePath = GetPackagePath();
			if (!_fileSystem.ExistsFile(packagePath)) {
				_logger.WriteError(
					$"The bundled {BundledPackages.ProcessBuilderPackageName} package was not found at " +
					$"'{packagePath}'. This clio installation does not carry the package archive.");
				return 1;
			}
			if (!options.Force && IsAlreadyRunningBundledBuild()) {
				return ReportAlreadyInstalled();
			}
			bool success = _packageInstaller.Install(
				packagePath,
				CreateInstallEnvironmentSettings(),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true);
			if (!success) {
				_logger.WriteError(
					$"Failed to install the bundled {BundledPackages.ProcessBuilderPackageName} package.");
				return 1;
			}
			// Installing a package whose assembly changed makes the platform restart itself, and that
			// restart outlives the install call — so wait for the instance to come back before judging it.
			if (!WaitForPlatformRestart()) {
				_logger.WriteError(
					$"{BundledPackages.ProcessBuilderPackageName} was installed, but the environment did not "
					+ "become ready within the timeout after the platform's post-install restart. Check the "
					+ "instance, then verify with 'clio call-service --service-path "
					+ "rest/ProcessDesignService/ListUserTasks -m POST -b {} -e <environment>'.");
				return 1;
			}
			// The install only proves the archive was accepted. The assembly is compiled BY THE TARGET, so
			// the service answering is the only proof the code exists and is loaded.
			if (!DoesServiceAnswer(out string diagnosis)) {
				_logger.WriteError(diagnosis ??
					$"{BundledPackages.ProcessBuilderPackageName} was installed, but ProcessDesignService " +
					"does not answer, which means the environment did not compile the package. Check the " +
					"environment's configuration build log, and verify the bundled archive still contains " +
					"its Source Code schema — without it the package installs but is never compiled.");
				return 1;
			}
			_logger.WriteLine("Done");
			return 0;
		} catch (Exception e) {
			// Readable message FIRST: it carries the WebException status / HTTP code, so a failed install
			// surfaces *why* — an auth 401 versus a connect timeout during upload — instead of a bare
			// stack with no message, which is how push-pkg loses this information today.
			_logger.WriteError(e.GetReadableMessageException());
			_logger.WriteError(e.StackTrace);
			return 1;
		}
	}

	#endregion

}
