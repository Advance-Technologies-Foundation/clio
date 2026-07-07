using System;
using System.IO;
using Clio.Common;
using Clio.Package;
using Clio.WebApplication;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Command-line options for installing or updating the bundled cliogate package.
/// </summary>
[Verb("install-gate", Aliases = ["gate", "update-gate", "installgate"],
	HelpText = "Install clio api gateway to application")]
public class InstallGateOptions : EnvironmentNameOptions { }

/// <summary>
/// Installs the bundled cliogate package into a Creatio environment.
/// </summary>
public class InstallGateCommand : Command<InstallGateOptions> {

	#region Fields: Private

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IPackageInstaller _packageInstaller;
	private readonly IApplication _application;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="InstallGateCommand"/> class.
	/// </summary>
	/// <param name="environmentSettings">Resolved target environment settings.</param>
	/// <param name="packageInstaller">Package installer used to install the bundled cliogate package.</param>
	/// <param name="application">Application service used to restart Creatio after installation.</param>
	/// <param name="workingDirectoriesProvider">Provider used to locate bundled clio assets.</param>
	/// <param name="logger">Logger used for command output.</param>
	public InstallGateCommand(
		EnvironmentSettings environmentSettings,
		IPackageInstaller packageInstaller,
		IApplication application,
		IWorkingDirectoriesProvider workingDirectoriesProvider,
		ILogger logger) {
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		packageInstaller.CheckArgumentNull(nameof(packageInstaller));
		application.CheckArgumentNull(nameof(application));
		workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
		logger.CheckArgumentNull(nameof(logger));
		_environmentSettings = environmentSettings;
		_packageInstaller = packageInstaller;
		_application = application;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	private EnvironmentSettings CreateInstallEnvironmentSettings() {
		EnvironmentSettings installEnvironmentSettings = new();
		installEnvironmentSettings.Merge(_environmentSettings);
		installEnvironmentSettings.DeveloperModeEnabled = false;
		return installEnvironmentSettings;
	}

	private string GetPackagePath() {
		string packageName = _environmentSettings.IsNetCore ? "cliogate_netcore" : "cliogate";
		return Path.Combine(_workingDirectoriesProvider.ExecutingDirectory, "cliogate", $"{packageName}.gz");
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Executes the install-gate command.
	/// </summary>
	/// <param name="options">The parsed install-gate command options.</param>
	/// <returns>Returns 0 when cliogate is installed successfully; otherwise, returns 1.</returns>
	public override int Execute(InstallGateOptions options) {
		try {
			bool success = _packageInstaller.Install(
				GetPackagePath(),
				CreateInstallEnvironmentSettings(),
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true);
			if (success) {
				_logger.WriteLine("Done");
				_application.Restart();
			} else {
				_logger.WriteError("Error");
			}
			return success ? 0 : 1;
		} catch (Exception e) {
			// Log the readable message FIRST (it now carries the WebException status / HTTP code via
			// GetReadableMessageException) so a failed install surfaces *why* — e.g. an auth 401 vs a
			// connect/timeout during the package upload — instead of a bare stack with no message.
			_logger.WriteError(e.GetReadableMessageException());
			_logger.WriteError(e.StackTrace);
			return 1;
		}
	}

	#endregion

}
