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
/// <b>No restart.</b> The configuration build that compiles the package also loads the result, so the
/// service answers without one. cliogate needs a restart precisely because it ships a prebuilt assembly,
/// which is only loaded at application start.
/// </description></item>
/// <item><description>
/// <b>The outcome is verified, not the install call.</b> Because the assembly is produced by the target
/// rather than shipped, "installed" and "working" are genuinely different states: were the compile-marker
/// schema ever lost from the archive, the package would install, never compile, and the name-based
/// <c>[RequiresPackage]</c> gate would still report it present while every
/// <c>/rest/ProcessDesignService/*</c> call failed. So this command calls <c>ListUserTasks</c> afterwards
/// and fails when the service does not answer. The Application Hub can recover that class of failure
/// through its own <c>RestoreFromBackup</c> stage; this path has no such button, which is exactly why the
/// check belongs here.
/// </description></item>
/// <item><description>
/// <b>The bundled artifact's presence is checked first</b>, so a distribution that failed to carry it
/// says so plainly instead of surfacing as a generic failure from deep inside the installer, which has no
/// existence pre-check of its own.
/// </description></item>
/// </list>
/// An already-compatible installation short-circuits, so re-running the command does no work — and in
/// particular does not make the environment recompile the package for nothing.
/// </remarks>
public class InstallProcessBuilderCommand : Command<InstallProcessBuilderOptions> {

	#region Fields: Private

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IPackageInstaller _packageInstaller;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IFileSystem _fileSystem;
	private readonly IRequiredPackageChecker _requiredPackageChecker;
	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
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
	/// <param name="requiredPackageChecker">
	/// Checker used to skip the install when the target environment already carries a compatible version.
	/// </param>
	/// <param name="applicationClient">Client used to prove the service answers after installation.</param>
	/// <param name="serviceUrlBuilder">Builder for the <c>ProcessDesignService</c> route.</param>
	/// <param name="logger">Logger used for command output.</param>
	public InstallProcessBuilderCommand(
		EnvironmentSettings environmentSettings,
		IPackageInstaller packageInstaller,
		IWorkingDirectoriesProvider workingDirectoriesProvider,
		IFileSystem fileSystem,
		IRequiredPackageChecker requiredPackageChecker,
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		packageInstaller.CheckArgumentNull(nameof(packageInstaller));
		workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		requiredPackageChecker.CheckArgumentNull(nameof(requiredPackageChecker));
		applicationClient.CheckArgumentNull(nameof(applicationClient));
		serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
		logger.CheckArgumentNull(nameof(logger));
		_environmentSettings = environmentSettings;
		_packageInstaller = packageInstaller;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_fileSystem = fileSystem;
		_requiredPackageChecker = requiredPackageChecker;
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
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

	/// <summary>
	/// Proves that <c>ProcessDesignService</c> actually answers on the target environment.
	/// </summary>
	/// <returns><c>true</c> only when the service returned a successful <c>ListUserTasks</c> envelope.</returns>
	/// <remarks>
	/// Fails CLOSED, unlike <see cref="IsAlreadyInstalledAndCompatible"/> — this check IS the point of the
	/// command's contract. A successful install proves the package was accepted, not that the target
	/// compiled it: no assembly ships in the archive, so the only proof that the code exists and is loaded
	/// is the service responding.
	/// <para>
	/// The response is parsed rather than pattern-matched, because the interesting failure is an HTML error
	/// page from IIS when the route does not resolve — that fails <see cref="JsonDocument.Parse"/> and is
	/// correctly reported as "not answering", whereas a substring search over it could accidentally match.
	/// A single attempt is deliberate: the compile finishes before the install call returns (the platform
	/// logs it synchronously), and a 404 from an unbound route is not a transient condition that retrying
	/// would fix.
	/// </para>
	/// </remarks>
	private bool DoesServiceAnswer() {
		try {
			string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ListUserTasks);
			// ProcessDesignService uses BodyStyle=Wrapped: a parameterless operation accepts an empty object.
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
			if (!success) {
				_logger.WriteError(
					$"Failed to install the bundled {BundledPackages.ProcessBuilderPackageName} package.");
				return 1;
			}
			// The install only proves the archive was accepted. The assembly is compiled BY THE TARGET, so
			// the service answering is the only proof the code exists and is loaded.
			if (!DoesServiceAnswer()) {
				_logger.WriteError(
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
