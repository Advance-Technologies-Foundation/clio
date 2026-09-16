using System;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options for <c>install-dashboards-migrator</c>.
/// </summary>
/// <remarks>
/// No <c>[RequiresCreatioVersion]</c>, although the app declares <c>RequiredPlatformVersion</c> 8.3.1: that
/// attribute compares the CORE version (10.x on an 8.3 stand), so a product floor cannot be expressed with
/// it. An older core refuses the configuration build, and the outcome check reports that.
/// </remarks>
[Verb("install-dashboards-migrator",
	Aliases = ["update-dashboards-migrator", "install-migrator", "update-migrator"],
	HelpText = "Install or update the dashboards-migrator app in Creatio from a package feed")]
public class InstallDashboardsMigratorOptions : EnvironmentNameOptions {

	/// <summary>
	/// Version to install. Omitted means the latest the feed offers, which is what an operator asking for
	/// "install the migrator" means.
	/// </summary>
	[Option("version", Required = false,
		HelpText = "App version to install; the latest version on the feed when omitted")]
	public string Version { get; set; }

	/// <summary>
	/// Feed to take the app from. Omitted means <see cref="DashboardsMigratorDistribution.DefaultFeedUrl"/>.
	/// </summary>
	[Option("source", Required = false,
		HelpText = "Package feed URL; the public feed when omitted")]
	public string SourceUrl { get; set; }

}

/// <summary>
/// Installs or updates the Dashboards Migrator app (7.x dashboards to Freedom UI) by fetching it from a
/// package feed, then proves the app's own code is serving.
/// </summary>
/// <remarks>
/// <para>
/// clio does not ship the package — see <see cref="DashboardsMigratorDistribution"/>. The fetch, the archive
/// assembly and the upload are <see cref="IInstallNugetPackage"/>'s, which is the same path
/// <c>install-nuget-pkg</c> takes; what this command adds is the part a generic install cannot have: the
/// wait for the platform's own post-install restart, and a verdict that rests on the app answering rather
/// than on the install call returning.
/// </para>
/// <para>
/// Installing a package whose assembly changed makes the platform restart itself, and that restart outlives
/// the install call — so the readiness wait sits between installing and judging the result.
/// </para>
/// </remarks>
public class InstallDashboardsMigratorCommand : Command<InstallDashboardsMigratorOptions> {

	#region Fields: Private

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IInstallNugetPackage _installNugetPackage;
	private readonly IServerReadinessWaiter _serverReadinessWaiter;
	private readonly IPackageInstallOutcomeVerifier _outcomeVerifier;
	private readonly IRequiredPackageChecker _requiredPackageChecker;
	private readonly IInstalledAppVersions _installedAppVersions;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public InstallDashboardsMigratorCommand(
		EnvironmentSettings environmentSettings,
		IInstallNugetPackage installNugetPackage,
		IServerReadinessWaiter serverReadinessWaiter,
		IPackageInstallOutcomeVerifier outcomeVerifier,
		IRequiredPackageChecker requiredPackageChecker,
		IInstalledAppVersions installedAppVersions,
		ILogger logger) {
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		installNugetPackage.CheckArgumentNull(nameof(installNugetPackage));
		serverReadinessWaiter.CheckArgumentNull(nameof(serverReadinessWaiter));
		outcomeVerifier.CheckArgumentNull(nameof(outcomeVerifier));
		requiredPackageChecker.CheckArgumentNull(nameof(requiredPackageChecker));
		installedAppVersions.CheckArgumentNull(nameof(installedAppVersions));
		logger.CheckArgumentNull(nameof(logger));
		_environmentSettings = environmentSettings;
		_installNugetPackage = installNugetPackage;
		_serverReadinessWaiter = serverReadinessWaiter;
		_outcomeVerifier = outcomeVerifier;
		_requiredPackageChecker = requiredPackageChecker;
		_installedAppVersions = installedAppVersions;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	// The probe route as `clio call-service --service-path` takes it, read from the same map the outcome
	// verifier probes through, so the route quoted to the operator is the one that was actually called.
	private static string PingServicePath =>
		ServiceUrlBuilder.KnownRoutes[PackagePingOutcomeVerifier.RouteOf(DashboardsMigratorDistribution.PackageName)]
			.TrimStart('/');

	private bool WaitForPlatformRestart() =>
		_serverReadinessWaiter.WaitForReady(new ServerReadinessOptions {
			Uri = _environmentSettings.Uri,
			IsNetCore = _environmentSettings.IsNetCore
		});

	// Recorded from the environment rather than from the requested version, so `clio info` names the version
	// that is actually serving — "latest" resolves to a number only the feed knew, and an install that landed
	// a different build than asked for must not be reported as the asked-for one.
	private void RecordInstalledVersion() {
		try {
			PackageVersion installed =
				_requiredPackageChecker.GetInstalledVersion(DashboardsMigratorDistribution.PackageName);
			if (installed is not null) {
				_installedAppVersions.Record(DashboardsMigratorDistribution.DisplayName, installed.ToString());
			}
		} catch (Exception exception) {
			// The install succeeded; failing to write a display record must not turn that into a failure.
			_logger.WriteWarning(
				$"The app is installed, but its version could not be recorded for 'clio info': {exception.Message}");
		}
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Executes the install-dashboards-migrator command.
	/// </summary>
	/// <param name="options">The parsed command options.</param>
	/// <returns>
	/// Returns 0 only when the app installed AND its Ping route answers afterwards; otherwise, returns 1.
	/// </returns>
	public override int Execute(InstallDashboardsMigratorOptions options) {
		string feedUrl = string.IsNullOrWhiteSpace(options.SourceUrl)
			? DashboardsMigratorDistribution.DefaultFeedUrl
			: options.SourceUrl;
		try {
			_logger.WriteInfo($"Installing {DashboardsMigratorDistribution.PackageName} "
				+ $"{(string.IsNullOrWhiteSpace(options.Version) ? "(latest)" : options.Version)} from {feedUrl}...");
			_installNugetPackage.Install(DashboardsMigratorDistribution.PackageName, options.Version, feedUrl);
			if (!WaitForPlatformRestart()) {
				_logger.WriteError(
					$"{DashboardsMigratorDistribution.PackageName} was installed, but the environment did not "
					+ "become ready within the timeout after the platform's post-install restart. Check the "
					+ "instance, then verify with 'clio call-service --service-path "
					+ $"{PingServicePath} -m POST -b {{}} -e <environment>'.");
				return 1;
			}
			if (!_outcomeVerifier.IsPackageOperational(
					DashboardsMigratorDistribution.PackageName, out string diagnosis)) {
				_logger.WriteError(diagnosis ??
					$"{DashboardsMigratorDistribution.PackageName} was installed, but its Ping route does not "
					+ "answer. Check the environment's configuration build log.");
				return 1;
			}
			RecordInstalledVersion();
			_logger.WriteLine("Done");
			return 0;
		} catch (Exception e) {
			// Readable message FIRST: it carries the WebException status / HTTP code, so a failed fetch or
			// upload surfaces why — an unreachable feed versus an auth failure on the environment.
			_logger.WriteError(e.GetReadableMessageException());
			_logger.WriteError(e.StackTrace);
			return 1;
		}
	}

	#endregion

}
