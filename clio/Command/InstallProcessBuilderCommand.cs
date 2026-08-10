using System;
using System.Linq;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
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
public class InstallProcessBuilderOptions : EnvironmentNameOptions {

	/// <summary>
	/// Installs even when the environment already carries a NEWER version than this clio ships.
	/// </summary>
	/// <remarks>
	/// Deliberately CLI-only — it is not exposed on the MCP tool. Rolling a package back is a decision with
	/// consequences for everyone else on that environment, and an agent working a user's business task is
	/// not the right party to take it. The refusal names this flag so a human can.
	/// <para>
	/// <c>new</c> on purpose: <see cref="EnvironmentOptions"/> already declares a <c>--force</c> ("Force
	/// restore") that is inherited here and means nothing for this verb. Shadowing it gives the flag this
	/// verb's own help text. The compiler will NOT point the shadowing out — <c>CS0108</c> is in the
	/// project's <c>NoWarn</c> list — hence this note. Keep the type <c>bool</c>: CommandLineParser and
	/// <c>CommandHelpRenderer</c> both de-duplicate by name+signature, so a differing signature would make
	/// <c>--force</c> appear TWICE in help, once with each description, with nothing to flag it.
	/// </para>
	/// </remarks>
	[Option("force", Required = false,
		HelpText = "Install even if it would downgrade the package already installed in the environment")]
	public new bool Force { get; set; }

}

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
/// <b>The only short-circuits</b> are about moving an environment BACKWARDS, and both are refused unless
/// <c>--force</c> is passed — see <see cref="ShouldRefuseInstall"/>: an install that would move the recorded
/// version backwards, and a distribution whose own bundled version is stamped so that such a move could not
/// be detected.
/// Otherwise an explicitly requested install always installs; see the comment at the install site for the
/// reasoning and for the one part of it that is now open again.
/// </remarks>
public class InstallProcessBuilderCommand : Command<InstallProcessBuilderOptions> {

	#region Constants: Private

	/// <summary>
	/// Budget for the advisory read of the environment's recorded version.
	/// </summary>
	/// <remarks>
	/// Generous, because a slow-but-answering instance must not lose the check — but finite, because the
	/// check is optional and the reservation it sits inside is not. See the call site.
	/// </remarks>
	private const int InstalledVersionProbeTimeoutMs = 60_000;

	/// <summary>
	/// Cap on any environment- or transport-supplied text this command quotes back to the reader.
	/// </summary>
	private const int QuotedTextLimit = 300;

	#endregion

	#region Fields: Private

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IPackageInstaller _packageInstaller;
	private readonly IBundledPackageCatalog _bundledPackageCatalog;
	private readonly IPackageInstallOutcomeVerifier _outcomeVerifier;
	private readonly IServerReadinessWaiter _serverReadinessWaiter;
	private readonly IRequiredPackageChecker _requiredPackageChecker;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="InstallProcessBuilderCommand"/> class.
	/// </summary>
	/// <param name="environmentSettings">Resolved target environment settings.</param>
	/// <param name="packageInstaller">Package installer used to install the bundled archive.</param>
	/// <param name="bundledPackageCatalog">
	/// Catalog used to locate the bundled archive, to confirm it is present, and to read the version it
	/// carries.
	/// </param>
	/// <param name="outcomeVerifier">
	/// Verifier that answers whether the package became operational after being accepted — the question the
	/// install call itself cannot answer for a package the target has to compile.
	/// </param>
	/// <param name="serverReadinessWaiter">
	/// Waiter used to let the platform's self-triggered restart finish before the service is probed.
	/// </param>
	/// <param name="requiredPackageChecker">
	/// Used only to read the version the environment currently records, so an install that would move it
	/// BACKWARDS can be refused.
	/// </param>
	/// <param name="logger">Logger used for command output.</param>
	public InstallProcessBuilderCommand(
		EnvironmentSettings environmentSettings,
		IPackageInstaller packageInstaller,
		IBundledPackageCatalog bundledPackageCatalog,
		IPackageInstallOutcomeVerifier outcomeVerifier,
		IServerReadinessWaiter serverReadinessWaiter,
		IRequiredPackageChecker requiredPackageChecker,
		ILogger logger) {
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		packageInstaller.CheckArgumentNull(nameof(packageInstaller));
		bundledPackageCatalog.CheckArgumentNull(nameof(bundledPackageCatalog));
		outcomeVerifier.CheckArgumentNull(nameof(outcomeVerifier));
		serverReadinessWaiter.CheckArgumentNull(nameof(serverReadinessWaiter));
		requiredPackageChecker.CheckArgumentNull(nameof(requiredPackageChecker));
		logger.CheckArgumentNull(nameof(logger));
		_environmentSettings = environmentSettings;
		_packageInstaller = packageInstaller;
		_bundledPackageCatalog = bundledPackageCatalog;
		_outcomeVerifier = outcomeVerifier;
		_serverReadinessWaiter = serverReadinessWaiter;
		_requiredPackageChecker = requiredPackageChecker;
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

	// Through the catalog rather than composing the path here, so the archive this installs and the archive
	// clio info / the convergence rule describe are the same file by construction, not by two copies of one
	// Path.Combine agreeing.
	private string GetPackagePath() =>
		_bundledPackageCatalog.GetArchivePath(BundledPackages.ProcessBuilderPackageName);

	/// <summary>
	/// Decides whether this install would move the environment's recorded version BACKWARDS.
	/// </summary>
	/// <param name="message">The refusal, naming both versions and the flag that overrides it.</param>
	/// <returns><c>true</c> when the install must be refused.</returns>
	/// <remarks>
	/// Nothing else stops a downgrade. The installer does not compare versions, and neither does the
	/// platform: Creatio rewrites <c>SysPackage.Version</c> whenever the descriptor's <c>ModifiedOnUtc</c>
	/// DIFFERS — not when it is later — and an earlier stamp was measured moving a recorded version down.
	/// So an older clio run against a shared environment silently rolls the package back for everyone on it.
	/// <para>
	/// Three cases deliberately proceed rather than refuse:
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// The SAME version. For a source-only package "installed" and "compiled" are different states, so
	/// reinstalling the version already recorded is the repair path for a package that never built. Refusing
	/// it would block the one action that fixes that.
	/// </description></item>
	/// <item><description>
	/// The package is absent. There is nothing to move backwards.
	/// </description></item>
	/// <item><description>
	/// Either version could not be READ — a distribution that cannot describe itself, or an environment that
	/// did not answer. Both are clio-side or transport failures, and neither is evidence of a downgrade;
	/// blocking on them would turn "I could not check" into "you may not proceed". The install fails on its
	/// own terms a moment later if the environment really is unreachable.
	/// </description></item>
	/// </list>
	/// <para>
	/// One case REFUSES for a different reason than a comparison: a bundled version carrying a pre-release
	/// suffix. Read the branch for why it must not join the list above — the short version is that this
	/// comparison is numbers-only, so a suffixed shipped version makes a rollback undetectable rather than
	/// merely unknown, and "proceed when unsure" is the wrong default for an artifact that is definitely wrong.
	/// </para>
	/// <para>
	/// There is a FOURTH, and it is silent: <c>PackageInfo</c> leaves its version <see langword="null"/> when
	/// <c>SysPackage.Version</c> does not parse, and <c>GetInstalledVersion</c> returns <c>?.Version</c> — so
	/// a recorded version of garbage is indistinguishable here from the package being absent, and takes the
	/// null branch with no warning. Distinguishing them means the checker exposing the difference, which is
	/// its own change; recorded so the gap is known rather than assumed away.
	/// </para>
	/// <para>
	/// What this is NOT: a guarantee. It compares against the version the environment RECORDED, which is
	/// simply whatever the last descriptor with a different timestamp said — not necessarily the highest
	/// ever installed, and not evidence that the recorded version is the one serving.
	/// </para>
	/// </remarks>
	private bool ShouldRefuseInstall(out string message) {
		message = null;
		if (!_bundledPackageCatalog.TryGetVersion(
				BundledPackages.ProcessBuilderPackageName,
				out PackageVersion shippedVersion,
				out string diagnosis)) {
			_logger.WriteWarning(
				$"{diagnosis} Installing anyway: without a version to compare, a downgrade cannot be ruled in "
				+ "or out, and refusing would block the install over clio's own defect.");
			return false;
		}
		if (!string.IsNullOrWhiteSpace(shippedVersion.Suffix)) {
			// REFUSED, not warned-and-installed, and the difference is the whole point. A bundled version must
			// be a plain four-part number: IsStrictlyNewer below compares numbers alone, which is only sound
			// while no suffix can appear on this side. Let one through and the guard stops being able to see a
			// rollback — shipping 1.0.1.0-rc onto an environment recording 9.9.9.9 would install, because the
			// suffix is all that the comparison ignores and the numbers say nothing is wrong. That is the exact
			// harm this guard exists to prevent, so a malformed distribution must not reach the comparison at
			// all rather than reach it blind.
			// Note this is NOT the unreadable-version path above: that one proceeds because "I could not check"
			// must not become "you may not proceed", and there is genuinely nothing to compare. Here the
			// distribution is readable and WRONG, clio's own artifact, and installing it is what causes damage
			// to somebody else's environment. Blocking is the safe direction; the message names clio, not the
			// target, because that is where the fix is.
			message =
				$"Refusing: this clio distribution declares {BundledPackages.ProcessBuilderPackageName} "
				+ $"{TextUtilities.SanitizeVersionForDisplay(shippedVersion)}, but a bundled package version must be a plain "
				+ "four-part number with no pre-release suffix — clio compares bundled versions numerically, "
				+ "and installing a suffixed one could move the target environment's recorded version backwards "
				+ "for everyone using it without being detected. Reinstall or update clio itself. If you "
				+ "produced this archive, re-run the rebundle with a four-part version.";
			return true;
		}
		PackageVersion installedVersion;
		try {
			// BOUNDED, unlike the install that follows it. This read is advisory - every failure below
			// proceeds - so it must never be able to outlast the thing it precedes. IApplicationClient
			// defaults to Timeout.Infinite, so a target that accepts the connection and then says nothing
			// would otherwise block here forever, with nothing attempted and nothing printed. On the MCP path
			// that is worse than a slow command: the tool holds the per-tenant configuration-build
			// reservation across this call, and a hang never reaches the finally that releases it, so every
			// later install-process-builder AND compile-creatio on that tenant is refused for the life of the
			// server process. A read allowed to fail must not be allowed to hang.
			Task<PackageVersion> read = Task.Run(() =>
				_requiredPackageChecker.GetInstalledVersion(BundledPackages.ProcessBuilderPackageName));
			if (!read.Wait(InstalledVersionProbeTimeoutMs)) {
				_logger.WriteWarning(
					"Timed out reading the version currently installed in the environment. Installing anyway; "
					+ "if the environment is genuinely unreachable the install itself will say so.");
				return false;
			}
			installedVersion = read.Result;
		} catch (Exception e) {
			_logger.WriteWarning(
				"Could not read the version currently installed in the environment "
				+ $"({Truncate(e.GetReadableMessageException())}). Installing anyway; if the environment is "
				+ "genuinely unreachable the install itself will say so.");
			return false;
		}
		if (installedVersion is null || !IsStrictlyNewer(installedVersion, shippedVersion)) {
			return false;
		}
		message =
			$"Refusing: the environment carries {BundledPackages.ProcessBuilderPackageName} "
			+ $"{TextUtilities.SanitizeVersionForDisplay(installedVersion)}, and this clio ships {TextUtilities.SanitizeVersionForDisplay(shippedVersion)} — installing would move "
			+ "that environment's recorded version BACKWARDS for everyone using it, and nothing downstream "
			+ "would report it: the gate would see a present package and the convergence check compares the "
			+ "recorded version, which the rollback has just rewritten. Update clio instead, or pass --force "
			+ "if the rollback is what you want.";
		return true;
	}

	/// <summary>
	/// Determines whether <paramref name="installed"/> is genuinely newer than <paramref name="shipped"/>,
	/// comparing the four-part numbers and nothing else.
	/// </summary>
	/// <remarks>
	/// NOT <c>PackageVersion</c>'s own comparison, and deliberately narrower than it. <c>CompareSuffix</c>
	/// ranks an EMPTY suffix BELOW a non-empty one, so it holds <c>1.1.0.0 &lt; 1.1.0.0-rc</c> — the inverse
	/// of SemVer. This guard needs no answer to that question at all, because
	/// <see cref="IBundledPackageCatalog.TryGetVersion"/> refuses a suffixed SHIPPED version outright: with
	/// one side guaranteed suffix-free, "is the environment ahead of what we carry" is settled by the numbers.
	/// <para>
	/// A suffix on the INSTALLED side is therefore ignored rather than ordered, and the effect is the one we
	/// want: installing <c>1.1.0.0</c> over an environment recording <c>1.1.0.0-rc</c> is permitted, because
	/// the release supersedes its own pre-release. This case is reachable through the supported path —
	/// <c>clio set-pkg-version</c> accepts and writes the <c>X.Y.Z.W-suffix</c> form deliberately, and
	/// whatever lands in <c>SysPackage.Version</c> is parsed straight back through <c>PackageVersion</c>.
	/// </para>
	/// <para>
	/// So <c>PackageVersion</c>'s operator is left alone rather than corrected: it is repo-wide and backs the
	/// <c>[RequiresPackage]</c> gate for cliogate and NuGet version selection, where the same change would
	/// alter which environments are refused and which packages are chosen. Correcting it there is its own
	/// decision with its own blast radius, and nothing here needs it.
	/// </para>
	/// </remarks>
	private static bool IsStrictlyNewer(PackageVersion installed, PackageVersion shipped) =>
		installed.Version.CompareTo(shipped.Version) > 0;

	/// <summary>
	/// Clamps EXCEPTION text that came from the environment or the transport before it is quoted back.
	/// </summary>
	/// <remarks>
	/// A DataService failure with no <c>errorInfo</c> puts the ENTIRE raw response body into the exception
	/// message (<c>SelectQueryHelper</c>), which can carry a server stack trace and internal paths. On the
	/// MCP path that would be redacted; on the CLI it would go straight to the console.
	/// <para>
	/// Versions do NOT come through here — they go through
	/// <see cref="TextUtilities.SanitizeVersionForDisplay"/>, which rebuilds them from permitted characters
	/// rather than merely shortening them. A length cap is the right defence for a stack trace and the wrong
	/// one for a value that reaches an agent's context, where the payload fits well inside any cap.
	/// </para>
	/// </remarks>
	private static string Truncate(string text) {
		if (string.IsNullOrEmpty(text)) {
			return text;
		}
		string collapsed = text.Replace("\r", " ").Replace("\n", " ");
		return collapsed.Length <= QuotedTextLimit
			? collapsed
			: collapsed[..QuotedTextLimit] + "…";
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
	/// carries one because waiting IS its job, whereas here the wait is incidental to an install that is
	/// long by nature — the target runs a full configuration build, and how long that takes belongs to the
	/// target, not to clio. No figure is quoted anywhere on purpose; see the remark on the readiness budget.
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
			if (!_bundledPackageCatalog.ArchiveExists(BundledPackages.ProcessBuilderPackageName)) {
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
			// The only things that can stop an explicitly requested install, both about moving the environment
			// backwards: it WOULD move backwards, or this distribution is stamped so that a rollback could not
			// be detected at all. Checked before anything is touched, and skipped entirely under --force.
			if (!options.Force && ShouldRefuseInstall(out string refusal)) {
				_logger.WriteError(refusal);
				return 1;
			}
			// Otherwise no short-circuit: an explicitly requested install always installs. It is invoked as
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
