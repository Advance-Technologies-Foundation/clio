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
/// <b>The OUTCOME is verified, not the install call.</b> Because the assembly is produced by the target
/// rather than shipped, "installed" and "working" are genuinely different states: accepting the archive and
/// compiling it are separate events, and only the second yields something that can serve. So after the
/// readiness wait this command asks <see cref="IPackageInstallOutcomeVerifier"/> whether the package became
/// operational, and fails when it did not. What today's verification can and cannot prove — in particular
/// that it cannot tell WHICH build answered — is documented on that interface and its implementation. The
/// Application Hub can recover a failed compile through its own <c>RestoreFromBackup</c> stage; this path has
/// no such button, which is exactly why the check belongs here.
/// </description></item>
/// <item><description>
/// <b>The bundled artifact's presence is checked first</b>, so a distribution that failed to carry it
/// says so plainly instead of surfacing as a generic failure from deep inside the installer, which has no
/// existence pre-check of its own.
/// </description></item>
/// </list>
/// <b>There is no short-circuit</b>: an explicitly requested install always installs. See the comment at the
/// install site for the reasoning and for the one part of it that is now open again.
/// </remarks>
public class InstallProcessBuilderCommand : Command<InstallProcessBuilderOptions> {

	#region Fields: Private

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IPackageInstaller _packageInstaller;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IFileSystem _fileSystem;
	private readonly IPackageInstallOutcomeVerifier _outcomeVerifier;
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
	/// <param name="outcomeVerifier">
	/// Verifier that answers whether the package became operational after being accepted — the question the
	/// install call itself cannot answer for a package the target has to compile.
	/// </param>
	/// <param name="serverReadinessWaiter">
	/// Waiter used to let the platform's self-triggered restart finish before the service is probed.
	/// </param>
	/// <param name="logger">Logger used for command output.</param>
	public InstallProcessBuilderCommand(
		EnvironmentSettings environmentSettings,
		IPackageInstaller packageInstaller,
		IWorkingDirectoriesProvider workingDirectoriesProvider,
		IFileSystem fileSystem,
		IPackageInstallOutcomeVerifier outcomeVerifier,
		IServerReadinessWaiter serverReadinessWaiter,
		ILogger logger) {
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		packageInstaller.CheckArgumentNull(nameof(packageInstaller));
		workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		outcomeVerifier.CheckArgumentNull(nameof(outcomeVerifier));
		serverReadinessWaiter.CheckArgumentNull(nameof(serverReadinessWaiter));
		logger.CheckArgumentNull(nameof(logger));
		_environmentSettings = environmentSettings;
		_packageInstaller = packageInstaller;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_fileSystem = fileSystem;
		_outcomeVerifier = outcomeVerifier;
		_serverReadinessWaiter = serverReadinessWaiter;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	/// <summary>
	/// Builds the settings the install runs under.
	/// </summary>
	/// <remarks>
	/// Duplicated deliberately from <see cref="InstallGateCommand"/> rather than shared: the two commands are
	/// the only bundled-package installers and they agree on this today, but folding them into a common helper
	/// would tie the process-builder install to any future change cliogate needs. If the developer-mode/unlock
	/// interaction changes, BOTH copies need looking at - the reason for the flag is documented here.
	/// </remarks>
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
	/// <para>
	/// The timing budget is deliberately NOT overridden: <see cref="ServerReadinessOptions"/>'s 600 s is
	/// what this command wants, and restating it here would put a second copy of the number in the codebase
	/// that a future retune of the shared default would silently skip. The other two callers override
	/// because their situations differ — <c>CreatioInstallerService</c> allows 45 s for a freshly deployed
	/// instance, <c>RestartCommand</c> passes the caller's own value — which is the convention: override
	/// when you need something else, not to echo the default.
	/// </para>
	/// <para>
	/// Generous on purpose, and the size is load-bearing in one direction: a configuration build plus a
	/// restart is the slowest thing this command triggers, and a false "not ready" would report a
	/// SUCCESSFUL install as a failure. Every live run so far answered on the FIRST probe.
	/// </para>
	/// <para>
	/// What the size costs is not the CLI wait, which prints progress per attempt and takes Ctrl+C: it is
	/// that on the MCP path the configuration-build reservation is held for the whole detached run, so a
	/// second install on the same environment is REFUSED for up to the full budget even once the target is
	/// plainly hopeless. That is the trade a shorter value — or an operator-facing knob — would be buying,
	/// and it needs a measurement of how long a slow-but-recovering instance actually takes, which nobody
	/// has made. A knob would also cost the whole doc quartet plus an MCP parity decision; <c>RestartCommand</c>
	/// carries one because waiting IS its job, whereas here the wait is incidental to an install whose
	/// healthy case measured 45-78 s end to end.
	/// </para>
	/// </remarks>
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
	/// Returns 0 only when the package installed AND <c>ProcessDesignService</c> answers afterwards;
	/// otherwise, returns 1. There is no already-current branch — see the comment at the install site.
	/// </returns>
	public override int Execute(InstallProcessBuilderOptions options) {
		try {
			string packagePath = GetPackagePath();
			if (!_fileSystem.ExistsFile(packagePath)) {
				// Says "do not retry" explicitly. Every failure branch here returns 1, which the MCP contract
				// documents as EXPECTED / caller-actionable — and an agent that reads it that way will retry
				// forever on a broken distribution. The exit code is left alone (changing it is a contract
				// change for every script that calls this verb); the message carries the distinction.
				_logger.WriteError(
					$"The bundled {BundledPackages.ProcessBuilderPackageName} package was not found at " +
					$"'{packagePath}'. This clio installation does not carry the package archive, so retrying " +
					"will not help — reinstall or update clio itself.");
				return 1;
			}
			// No short-circuit: an explicitly requested install always installs. It is invoked as
			// remediation, the install is backed up, and the cost of a needless run is one configuration build.
			// A version-based skip via the database is viable — the recorded version does move — and is left
			// unbuilt deliberately, not by oversight: it is a behaviour change, recorded as an open item in
			// spec/adr/adr-deliver-process-builder-package.md. A skip via the SERVICE is not viable, and that
			// is by design: Ping answers "this package is compiled and serving", not "which build" — so it
			// cannot tell a current assembly from a stale one, and would skip an install that is needed.
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
					+ "rest/ProcessDesignService/Ping -m POST -b {} -e <environment>'.");
				return 1;
			}
			// The install only proves the archive was ACCEPTED. The assembly is compiled BY THE TARGET, and a
			// configuration build can report success while leaving no route behind (observed on a stand), so
			// something has to establish that the package's own code answers — which no database read can say,
			// since SysPackage records the accepted version whether anything compiled or not.
			if (!_outcomeVerifier.IsPackageOperational(
					BundledPackages.ProcessBuilderPackageName,
					out string diagnosis)) {
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
