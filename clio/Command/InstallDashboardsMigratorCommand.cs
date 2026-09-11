using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options for <c>install-dashboards-migrator</c>. Carries no <c>[RequiresPackage]</c> and no
/// <c>[FeatureToggle]</c> — see <see cref="InstallBundledPackageOptions"/> for why.
/// </summary>
/// <remarks>
/// No <c>[RequiresCreatioVersion]</c> either, although the package declares <c>RequiredPlatformVersion</c>
/// 8.3.1: that attribute compares the CORE version (<c>coreVersion</c>, 10.x on an 8.3 stand), so a product
/// floor cannot be expressed with it. An older core refuses the configuration build, and the outcome check
/// reports that.
/// </remarks>
[Verb("install-dashboards-migrator", Aliases = ["update-dashboards-migrator"],
	HelpText = "Install or update the bundled dashboards-migrator package in Creatio")]
public class InstallDashboardsMigratorOptions : InstallBundledPackageOptions {

}

/// <summary>
/// Installs the bundled dashboards-migrator package (the "Dashboards migrator" app that converts 7.x
/// dashboards to Freedom UI), making <c>DashboardsMigratorService</c> reachable on the target.
/// </summary>
public class InstallDashboardsMigratorCommand : InstallBundledPackageCommand<InstallDashboardsMigratorOptions> {

	/// <inheritdoc />
	public InstallDashboardsMigratorCommand(
		EnvironmentSettings environmentSettings,
		IPackageInstaller packageInstaller,
		IBundledPackageCatalog bundledPackageCatalog,
		IPackageInstallOutcomeVerifier outcomeVerifier,
		IServerReadinessWaiter serverReadinessWaiter,
		IRequiredPackageChecker requiredPackageChecker,
		ILogger logger)
		: base(environmentSettings, packageInstaller, bundledPackageCatalog, outcomeVerifier,
			serverReadinessWaiter, requiredPackageChecker, logger) {
	}

	/// <inheritdoc />
	protected override string PackageName => BundledPackages.DashboardsMigratorPackageName;

	/// <inheritdoc />
	protected override ServiceUrlBuilder.KnownRoute PingRoute => ServiceUrlBuilder.KnownRoute.DashboardsMigratorPing;

}
