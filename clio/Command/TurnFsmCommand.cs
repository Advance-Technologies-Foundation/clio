using System;
using System.Threading;
using CommandLine;

namespace Clio.Command;

using Clio.Common;

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
	public TurnFsmCommand(SetFsmConfigCommand setFsmConfigCommand,
		LoadPackagesToFileSystemCommand loadPackagesToFileSystemCommand,
		LoadPackagesToDbCommand loadPackagesToDbCommand, IApplicationClient applicationClient,
		EnvironmentSettings environmentSettings, RestartCommand restartCommand) {
		_setFsmConfigCommand = setFsmConfigCommand;
		_loadPackagesToFileSystemCommand = loadPackagesToFileSystemCommand;
		_loadPackagesToDbCommand = loadPackagesToDbCommand;
		_applicationClient = applicationClient;
		_environmentSettings = environmentSettings;
		_restartCommand = restartCommand;
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
			Console.WriteLine("Invalid value for IsFsm. Expected: 'on' or 'off'.");
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
					if (!TryLoginWithRetry(_applicationClient, timeout: TimeSpan.FromSeconds(90), delay: TimeSpan.FromSeconds(3))) {
						Console.WriteLine("Application is not available after restart. Try again later or increase restart time.");
						return 1;
					}
				}
				return _loadPackagesToFileSystemCommand.Execute(options);
			}
		}
		else {
			if (_loadPackagesToDbCommand.Execute(options) == 0) {
				return _setFsmConfigCommand.Execute(options);
			}
		}
		return 1;
	}

	private static bool TryLoginWithRetry(IApplicationClient applicationClient, TimeSpan timeout, TimeSpan delay) {
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
					Console.WriteLine("Waiting for application to start after restart...");
					printedWaitingMessage = true;
				}
				Thread.Sleep(delay);
			}
		}
		if (lastException != null) {
			Console.WriteLine(lastException.Message);
		}
		return false;
	}

	#endregion

}
