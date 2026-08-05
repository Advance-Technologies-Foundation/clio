using System;
using System.IO;
using Clio.Common;
using Clio.Package;
using Clio.WebApplication;
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
/// Modelled on <see cref="InstallGateCommand"/>, with three deliberate differences:
/// <list type="number">
/// <item><description>
/// There is no <c>IsNetCore</c> branch. One archive serves both hosts, because it carries both
/// <c>Files/Bin</c> (net472) and <c>Files/Bin/netstandard</c> (.NET) and the platform picks the matching
/// directory. cliogate needs two archives only because clio installs a differently NAMED package per
/// runtime, which is a naming choice rather than a framework requirement.
/// </description></item>
/// <item><description>
/// The bundled artifact's presence is checked before the install, so a distribution that failed to carry
/// it reports that plainly instead of surfacing as a generic install failure from deep inside the
/// installer.
/// </description></item>
/// <item><description>
/// An already-compatible installation short-circuits, so re-running the command does not restart a
/// healthy environment for nothing. The restart matters: <c>BasePackageInstaller</c> already restarts on
/// its own when the target is .NET Core, so an unconditional explicit restart makes two.
/// </description></item>
/// </list>
/// </remarks>
public class InstallProcessBuilderCommand : Command<InstallProcessBuilderOptions> {

	#region Fields: Private

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IPackageInstaller _packageInstaller;
	private readonly IApplication _application;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IFileSystem _fileSystem;
	private readonly IRequiredPackageChecker _requiredPackageChecker;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="InstallProcessBuilderCommand"/> class.
	/// </summary>
	/// <param name="environmentSettings">Resolved target environment settings.</param>
	/// <param name="packageInstaller">Package installer used to install the bundled archive.</param>
	/// <param name="application">Application service used to restart Creatio after installation.</param>
	/// <param name="workingDirectoriesProvider">Provider used to locate bundled clio assets.</param>
	/// <param name="fileSystem">File system used to verify the bundled archive is present.</param>
	/// <param name="requiredPackageChecker">
	/// Checker used to skip the install when the target environment already carries a compatible version.
	/// </param>
	/// <param name="logger">Logger used for command output.</param>
	public InstallProcessBuilderCommand(
		EnvironmentSettings environmentSettings,
		IPackageInstaller packageInstaller,
		IApplication application,
		IWorkingDirectoriesProvider workingDirectoriesProvider,
		IFileSystem fileSystem,
		IRequiredPackageChecker requiredPackageChecker,
		ILogger logger) {
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		packageInstaller.CheckArgumentNull(nameof(packageInstaller));
		application.CheckArgumentNull(nameof(application));
		workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		requiredPackageChecker.CheckArgumentNull(nameof(requiredPackageChecker));
		logger.CheckArgumentNull(nameof(logger));
		_environmentSettings = environmentSettings;
		_packageInstaller = packageInstaller;
		_application = application;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_fileSystem = fileSystem;
		_requiredPackageChecker = requiredPackageChecker;
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
	/// Determines whether the target environment already carries a compatible version of the package.
	/// </summary>
	/// <returns><c>true</c> only when the check succeeded AND reported a compatible version.</returns>
	/// <remarks>
	/// Fails OPEN on purpose. Reading the installed package list is a DataService call that can fail for
	/// reasons unrelated to the package — an unreachable host, expired credentials, or missing read rights
	/// on <c>SysPackage</c>. None of those should stop an explicitly requested install, so any failure
	/// falls through to installing rather than aborting.
	/// </remarks>
	private bool IsAlreadyInstalledAndCompatible() {
		try {
			return _requiredPackageChecker.IsCompatible(
				BundledPackages.ProcessBuilderPackageName, BundledPackages.ProcessBuilderVersion);
		} catch (Exception e) {
			_logger.WriteInfo(
				$"Could not determine the installed {BundledPackages.ProcessBuilderPackageName} version " +
				$"({e.Message}); proceeding with the installation.");
			return false;
		}
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Executes the install-process-builder command.
	/// </summary>
	/// <param name="options">The parsed install-process-builder command options.</param>
	/// <returns>
	/// Returns 0 when the package is installed successfully or a compatible version is already present;
	/// otherwise, returns 1.
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
			if (IsAlreadyInstalledAndCompatible()) {
				_logger.WriteInfo(
					$"{BundledPackages.ProcessBuilderPackageName} " +
					$"{BundledPackages.ProcessBuilderVersion} or higher is already installed. " +
					"Nothing to do.");
				return 0;
			}
			bool success = _packageInstaller.Install(
				packagePath,
				CreateInstallEnvironmentSettings(),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true);
			if (success) {
				_logger.WriteLine("Done");
				_application.Restart();
			} else {
				_logger.WriteError(
					$"Failed to install the bundled {BundledPackages.ProcessBuilderPackageName} package.");
			}
			return success ? 0 : 1;
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
