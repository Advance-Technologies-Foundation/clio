using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options for <c>install-process-builder</c>. Carries no <c>[RequiresPackage]</c> and no <c>[FeatureToggle]</c>
/// — see <see cref="InstallBundledPackageOptions"/> for why.
/// </summary>
[Verb("install-process-builder", Aliases = ["update-process-builder", "installprocessbuilder"],
	HelpText = "Install or update the bundled process-builder package in Creatio")]
public class InstallProcessBuilderOptions : InstallBundledPackageOptions {

}

/// <summary>
/// Installs the bundled process-builder package, making <c>ProcessDesignService</c> reachable on the target.
/// </summary>
public class InstallProcessBuilderCommand : InstallBundledPackageCommand<InstallProcessBuilderOptions> {

	/// <inheritdoc />
	public InstallProcessBuilderCommand(
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
	protected override string PackageName => BundledPackages.ProcessBuilderPackageName;

	/// <inheritdoc />
	protected override ServiceUrlBuilder.KnownRoute PingRoute => ServiceUrlBuilder.KnownRoute.ProcessBuilderPing;

}
