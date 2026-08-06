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
/// readiness wait this command calls <c>ProcessDesignService.ListUserTasks</c> and fails when the service
/// does not answer. What that probe can and cannot prove — in particular that it cannot tell WHICH build
/// answered — is documented on <see cref="DoesListUserTasksAnswer"/>. The Application Hub can recover a
/// failed compile through its own <c>RestoreFromBackup</c> stage; this path has no such button, which is
/// exactly why the check belongs here.
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

	/// <summary>
	/// Per-request budget for a service probe, in milliseconds.
	/// </summary>
	/// <remarks>
	/// <see cref="IApplicationClient.ExecutePostRequest"/> defaults to <see cref="Timeout.Infinite"/>, which
	/// is wrong for the one call that decides this command's exit code: an instance that accepts the
	/// connection right after its restart but stalls behind the configuration-build lock would hang the CLI
	/// with no output and no way out but Ctrl+C. Every probe in <see cref="IServerReadinessWaiter"/> is
	/// bounded for exactly this reason; the final probe must not be the only unbounded call in the flow. A
	/// serving GetVersion answers in well under a second.
	/// </remarks>
	private const int ProbeTimeoutMs = 15_000;

	/// <summary>
	/// Attempts for the POST-install probe, retried because the readiness gate is weaker than the question.
	/// </summary>
	/// <remarks>
	/// <c>WaitForReady</c> proves the host answers <c>/api/HealthCheck/Ping</c>, which a still-draining
	/// worker or one whose configuration workspace has not finished loading can also do. A single probe
	/// therefore risks reporting "the environment did not compile the package" about an environment that
	/// answers correctly a few seconds later. The pre-install probe is NOT retried: an unanswerable route
	/// there simply means "install", so waiting on it would only slow the ordinary path.
	/// </remarks>
	private const int PostInstallProbeAttempts = 3;

	/// <summary>Delay between post-install probe attempts, in seconds.</summary>
	private const int PostInstallProbeDelaySec = 5;

	private string GetPackagePath() => Path.Combine(
		_workingDirectoriesProvider.ExecutingDirectory,
		BundledPackages.ProcessBuilderPackageName,
		BundledPackages.ProcessBuilderArchiveFileName);

	/// <summary>
	/// Proves that <c>ProcessDesignService</c> answers on the target after the install.
	/// </summary>
	/// <returns><c>true</c> only when the service returned a successful <c>ListUserTasks</c> envelope.</returns>
	/// <remarks>
	/// Weak in two ways worth naming. It cannot tell WHICH
	/// build answered, so on an upgrade a still-serving old assembly passes it. And <c>ListUserTasks</c> is
	/// gated on <c>CanManageProcessDesign</c> inside the package, which returns the guard's rejection as an
	/// UNSUCCESSFUL envelope — so an installer who may deploy packages but was never granted process-design
	/// rights would fail this check on a perfectly good install. The <c>errorMessage</c> branch below exists
	/// to keep that from being reported as a build failure.
	/// <para>
	/// A per-package <c>GetVersion</c> operation answering "which build is serving" was tried and REVERTED:
	/// it does not scale (every bundled package would have to re-implement it) and it duplicates two
	/// mechanisms the platform already has — <c>SysPackage.Version</c> and the <c>ConfActivityLog</c>
	/// Compilation record. The package-agnostic replacement belongs in clio, reading the platform's own
	/// signals: the installation log clio already receives, and <c>ConfActivityLog</c>. Until that lands this
	/// probe is the whole outcome check, and its weakness is stated above rather than hidden.
	/// </para>
	/// <para>
	/// The response is parsed rather than pattern-matched, because the interesting failure is an HTML error
	/// page from IIS when the route does not resolve — that fails <see cref="JsonDocument.Parse"/> and is
	/// correctly reported as "not answering", whereas a substring search over it could accidentally match.
	/// </para>
	/// </remarks>
	private bool DoesListUserTasksAnswer(out string diagnosis) {
		diagnosis = null;
		try {
			string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ListUserTasks);
			string response = _applicationClient.ExecutePostRequest(
				url, "{}", ProbeTimeoutMs, PostInstallProbeAttempts, PostInstallProbeDelaySec);
			using JsonDocument document = JsonDocument.Parse(response);
			if (!document.RootElement.TryGetProperty("ListUserTasksResult", out JsonElement result)) {
				return false;
			}
			if (result.TryGetProperty("success", out JsonElement success)
				&& success.ValueKind == JsonValueKind.True) {
				return true;
			}
			// A PARSEABLE envelope saying success:false proves the assembly exists and is serving — the
			// failure is INSIDE it, and errorMessage is the only field that says what. Discarding it and
			// letting the caller print "the environment did not compile the package" sends the reader to a
			// build log that is clean. The likeliest cause is authorization: the package returns the
			// process-design guard's rejection this way, and installing a package does not grant
			// CanManageProcessDesign.
			if (result.TryGetProperty("errorMessage", out JsonElement message)
				&& message.ValueKind == JsonValueKind.String
				&& !string.IsNullOrWhiteSpace(message.GetString())) {
				diagnosis =
					$"{BundledPackages.ProcessBuilderPackageName} was installed and ProcessDesignService is " +
					$"responding, but it rejected the check: {message.GetString()}. The package compiled — " +
					"this is not a build failure. If the message is about permissions, note that ListUserTasks " +
					"requires the CanManageProcessDesign operation and a General (non-portal) user, which " +
					"installing a package does not grant.";
			}
			return false;
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
			// remediation, the install is backed up, and the cost of a needless run is one configuration
			// build. What survives of the original reasoning is that asking the SERVICE cannot answer the
			// question — ListUserTasks proves something answers, not which build, so it would happily report
			// "nothing to do" for an environment still serving an old assembly.
			//
			// OPEN: the other half of that reasoning has been retracted. It said the recorded package version
			// is inert because Creatio does not rewrite the SysPackage row on re-install. It does — the row
			// is rewritten when the descriptor's ModifiedOnUtc moves, and `clio set-pkg-version` stamps it
			// alongside PackageVersion (see BundledPackages.ProcessBuilderVersion). So a database short-circuit
			// via IRequiredPackageChecker.IsCompatible is viable again and would save a needless configuration
			// build on an up-to-date environment. Left unbuilt deliberately rather than by oversight: it is a
			// behaviour change, not a doc fix.
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
			if (!DoesListUserTasksAnswer(out string diagnosis)) {
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
