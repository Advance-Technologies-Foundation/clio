using System;
using CommandLine;

namespace Clio.Command;

using Clio.Common;
using Clio.Package;

[Verb("turn-fsm", Aliases = ["tfsm", "fsm"], HelpText = "Turn file system mode on or off for an environment")]
public class TurnFsmCommandOptions : SetFsmConfigOptions
{ }

/// <summary>
/// Command to turn file system mode on or off for a Creatio environment.
/// When turning FSM on, it configures the environment and loads packages to file system.
/// When turning FSM off, it loads packages to database and then configures the environment.
/// </summary>
public class TurnFsmCommand : Command<TurnFsmCommandOptions>
{

	#region Fields: Private

	private readonly SetFsmConfigCommand _setFsmConfigCommand;
	private readonly LoadPackagesToFileSystemCommand _loadPackagesToFileSystemCommand;
	private readonly LoadPackagesToDbCommand _loadPackagesToDbCommand;
	private readonly IApplicationClient _applicationClient;
	private readonly EnvironmentSettings _environmentSettings;
	private readonly RestartCommand _restartCommand;
	private readonly ILogger _logger;
	private readonly IRetryDelay _retryDelay;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="TurnFsmCommand"/> class.
	/// </summary>
	/// <param name="setFsmConfigCommand">Command to set file system mode configuration.</param>
	/// <param name="loadPackagesToFileSystemCommand">Command to load packages to file system.</param>
	/// <param name="loadPackagesToDbCommand">Command to load packages to database.</param>
	/// <param name="applicationClient"></param>
	/// <param name="environmentSettings">Environment settings configuration.</param>
	/// <param name="restartCommand"></param>
	/// <param name="logger">Command output logger.</param>
	/// <param name="retryDelay">Delay used between application login attempts.</param>
	public TurnFsmCommand(SetFsmConfigCommand setFsmConfigCommand,
		LoadPackagesToFileSystemCommand loadPackagesToFileSystemCommand,
		LoadPackagesToDbCommand loadPackagesToDbCommand, IApplicationClient applicationClient,
		EnvironmentSettings environmentSettings, RestartCommand restartCommand, ILogger logger,
		IRetryDelay retryDelay) {
		_setFsmConfigCommand = setFsmConfigCommand;
		_loadPackagesToFileSystemCommand = loadPackagesToFileSystemCommand;
		_loadPackagesToDbCommand = loadPackagesToDbCommand;
		_applicationClient = applicationClient;
		_environmentSettings = environmentSettings;
		_restartCommand = restartCommand;
		_logger = logger;
		_retryDelay = retryDelay;
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Executes the file system mode toggle command.
	/// </summary>
	/// <param name="options">Command options containing FSM configuration.</param>
	/// <returns>0 if successful, 1 if failed.</returns>
	public override int Execute(TurnFsmCommandOptions options) {
		string fsmValue = options.IsFsm?.Trim() ?? string.Empty;
		bool isOn = string.Equals(fsmValue, "on", StringComparison.OrdinalIgnoreCase);
		bool isOff = string.Equals(fsmValue, "off", StringComparison.OrdinalIgnoreCase);
		if (!isOn && !isOff) {
			_logger.WriteError("Invalid value for IsFsm. Expected: 'on' or 'off'.");
			return 1;
		}

		if (isOn) {
			if (_setFsmConfigCommand.Execute(options) == 0) {
				options.IsNetCore = _environmentSettings.IsNetCore;
				if (options.IsNetCore == true) {
					RestartOptions opt = new () {
						Environment = options.Environment,
						Uri = options.Uri,
						Login = options.Login,
						Password = options.Password,
						IsNetCore = options.IsNetCore
					};
					//RestartCommand restartCommand = new (_applicationClient, _environmentSettings);
					_restartCommand.Execute(opt);
					if (!TryLoginWithRetry(_applicationClient, _logger, timeout: TimeSpan.FromSeconds(90), delay: TimeSpan.FromSeconds(3))) {
						_logger.WriteError("Application is not available after restart. Try again later or increase restart time.");
						return 1;
					}
				}
				return ExecuteFileSystemExport(options);
			}
		}
		else {
			return ExecuteDatabaseImportThenDisable(options);
		}
		return 1;
	}

	/// <summary>
	/// Exports the packages after file system mode was switched on. The configuration has already been
	/// written at this point, so a failure is reported with that fact spelled out instead of leaving the
	/// caller to read exit code 1 as "nothing changed on the environment".
	/// </summary>
	/// <param name="options">Command options containing FSM configuration.</param>
	/// <returns>0 when the export completed; otherwise 1.</returns>
	private int ExecuteFileSystemExport(TurnFsmCommandOptions options) {
		FileDesignModeLoadResult loadResult = _loadPackagesToFileSystemCommand.Load(options);
		if (loadResult == FileDesignModeLoadResult.Completed) {
			return 0;
		}
		if (loadResult == FileDesignModeLoadResult.FileDesignModeDisabled) {
			// The Web.config flag was written but the running application still answers "disabled".
			// On .NET Framework the IIS app pool recycles asynchronously on that file change, so the
			// probe can be answered by a worker that has not picked the new flag up yet.
			_logger.WriteWarning(
				"File system mode was written to the configuration, but the environment still reports file " +
				"design mode as disabled, so no packages were exported. Wait for the web application to " +
				"restart and run 'clio pkg-to-file-system' to finish the export.");
		} else {
			_logger.WriteWarning(
				"File system mode is already switched on in the configuration, but the packages were not " +
				"exported. Run 'clio pkg-to-file-system' to finish the export.");
		}
		return 1;
	}

	/// <summary>
	/// Imports the packages before file system mode is switched off. An environment that already reports
	/// file design mode as disabled is the goal state of this direction and never had anything to import,
	/// so the configuration is still written; a load the platform refused, or an unreadable file design
	/// mode state, aborts before the configuration is touched.
	/// </summary>
	/// <param name="options">Command options containing FSM configuration.</param>
	/// <returns>The exit code of the configuration step, or 1 when the import failed.</returns>
	private int ExecuteDatabaseImportThenDisable(TurnFsmCommandOptions options) {
		FileDesignModeLoadResult loadResult = _loadPackagesToDbCommand.Load(options);
		switch (loadResult) {
			case FileDesignModeLoadResult.Completed:
				return _setFsmConfigCommand.Execute(options);
			case FileDesignModeLoadResult.FileDesignModeDisabled:
				_logger.WriteWarning(
					"The environment already has file design mode disabled, so there was nothing to import. " +
					"Applying the file system mode configuration.");
				return _setFsmConfigCommand.Execute(options);
			default:
				_logger.WriteError(
					"Packages were not imported into the database, so the file system mode configuration was " +
					"left unchanged. Fix the reported error and run 'clio turn-fsm off' again, or run " +
					"'clio set-fsm-config off' to write the configuration without importing.");
				return 1;
		}
	}

	private bool TryLoginWithRetry(IApplicationClient applicationClient, ILogger logger, TimeSpan timeout, TimeSpan delay) {
		DateTime start = DateTime.UtcNow;
		Exception lastException = null;
		bool printedWaitingMessage = false;
		while (DateTime.UtcNow - start < timeout) {
			try {
				applicationClient.Login();
				return true;
			}
			catch (Exception ex) {
				lastException = ex;
				if (!printedWaitingMessage) {
					logger.WriteLine("Waiting for application to start after restart...");
					printedWaitingMessage = true;
				}
				_retryDelay.Wait(delay);
			}
		}
		if (lastException != null) {
			logger.WriteError(lastException.Message);
		}
		return false;
	}

	#endregion

}
