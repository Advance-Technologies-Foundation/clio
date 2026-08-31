using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Clio.Common;

/// <summary>
/// Service for starting Creatio host processes.
/// Provides methods for starting Creatio in background or in a new terminal window.
/// </summary>
public interface ICreatioHostService
{
	/// <summary>
	/// Starts Creatio host in the background (no terminal window).
	/// Returns the process ID if successful.
	/// </summary>
	/// <param name="workingDirectory">Directory containing the Creatio host.</param>
	/// <param name="environmentVariables">Optional environment variables for the child host.</param>
	int? StartInBackground(string workingDirectory,
		IReadOnlyDictionary<string, string> environmentVariables = null);

	/// <summary>
	/// Persists the environment values needed to start the Creatio host after the current process exits.
	/// </summary>
	/// <param name="workingDirectory">Directory containing the Creatio host.</param>
	/// <param name="environmentVariables">Environment values to restore on a later start.</param>
	void PersistEnvironmentVariables(string workingDirectory,
		IReadOnlyDictionary<string, string> environmentVariables);

	/// <summary>
	/// Starts Creatio host in a new terminal window.
	/// </summary>
	void StartInNewTerminal(string workingDirectory, string envName);
}

/// <summary>
/// Starts the Creatio host with a constrained inherited environment and restores
/// deployment-time certificate values for later starts.
/// </summary>
public class CreatioHostService : ICreatioHostService
{
	private static readonly string[] HostEnvironmentAllowlist = [
		"PATH",
		"HOME",
		"USERPROFILE",
		"TMP",
		"TEMP",
		"TMPDIR",
		"DOTNET_ROOT",
		"DOTNET_ROOT(x86)",
		"DOTNET_CLI_HOME",
		"SystemRoot",
		"WINDIR",
		"PATHEXT"
	];

	private readonly ILogger _logger;
	private readonly IProcessExecutor _processExecutor;
	private readonly ICreatioHostEnvironmentStore _environmentStore;

	/// <summary>
	/// Initializes a new instance of the <see cref="CreatioHostService"/> class.
	/// </summary>
	/// <param name="logger">Logger used for host lifecycle messages.</param>
	/// <param name="processExecutor">Process launcher used to start the host.</param>
	/// <param name="environmentStore">Persistent store for sensitive host environment values.</param>
	public CreatioHostService(
		ILogger logger,
		IProcessExecutor processExecutor,
		ICreatioHostEnvironmentStore environmentStore)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
		_environmentStore = environmentStore ?? throw new ArgumentNullException(nameof(environmentStore));
	}

	/// <summary>
	/// Starts the Creatio host process in the background.
	/// The process runs detached and unmanaged - user can stop it via 'clio stop' or manual termination.
	/// </summary>
	public int? StartInBackground(string workingDirectory,
		IReadOnlyDictionary<string, string> environmentVariables = null)
	{
		try
		{
			environmentVariables ??= _environmentStore.Load(workingDirectory);
			ProcessExecutionOptions options = new("dotnet", "Terrasoft.WebHost.dll") {
				WorkingDirectory = workingDirectory,
				EnvironmentVariables = environmentVariables,
				ClearInheritedEnvironment = true,
				InheritedEnvironmentVariableAllowlist = HostEnvironmentAllowlist
			};
			ProcessLaunchResult result = _processExecutor.FireAndForgetAsync(options).GetAwaiter().GetResult();
			if (result.Started && result.ProcessId.HasValue)
			{
				_logger.WriteInfo($"Started Creatio host process (PID: {result.ProcessId.Value})");
				_logger.WriteInfo($"To view logs: check application log files in the Creatio directory");
				return result.ProcessId;
			}
			_logger.WriteWarning($"Failed to start host process: {result.ErrorMessage ?? "process returned null"}");
			return null;
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Failed to start host process: {ex.Message}");
			throw;
		}
	}

	/// <inheritdoc />
	public void PersistEnvironmentVariables(string workingDirectory,
		IReadOnlyDictionary<string, string> environmentVariables)
	{
		_environmentStore.Save(workingDirectory, environmentVariables);
	}

	/// <summary>
	/// Starts the Creatio host process in a new terminal window.
	/// </summary>
	public void StartInNewTerminal(string workingDirectory, string envName)
	{
		IReadOnlyDictionary<string, string> environmentVariables = _environmentStore.Load(workingDirectory);
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			string windowsArgs = $"/c start \"Creatio [{envName}]\" cmd.exe /k \"cd /d \"{workingDirectory}\" && dotnet Terrasoft.WebHost.dll\"";
			StartTerminalProcess("cmd.exe", windowsArgs, workingDirectory, environmentVariables);
			return;
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			string command = $"cd '{workingDirectory}' && echo 'Starting Creatio [{envName}]...' && dotnet Terrasoft.WebHost.dll";
			string script = $"tell application \\\"Terminal\\\" to do script \\\"{command}\\\"";
			StartTerminalProcess("osascript", $"-e \"{script}\"", workingDirectory, environmentVariables);
			return;
		}
		string terminal = GetLinuxTerminal();
		string linuxArgs = $"--working-directory=\"{workingDirectory}\" -e \"bash -c 'echo Starting Creatio [{envName}]...; dotnet Terrasoft.WebHost.dll; exec bash'\"";
		StartTerminalProcess(terminal, linuxArgs, workingDirectory, environmentVariables);
	}

	private void StartTerminalProcess(
		string program,
		string arguments,
		string workingDirectory,
		IReadOnlyDictionary<string, string> environmentVariables)
	{
		ProcessExecutionOptions options = new(program, arguments) {
			WorkingDirectory = workingDirectory,
			EnvironmentVariables = environmentVariables,
			ClearInheritedEnvironment = true,
			InheritedEnvironmentVariableAllowlist = HostEnvironmentAllowlist
		};
		_processExecutor.FireAndForgetAsync(options).GetAwaiter().GetResult();
	}

	private string GetLinuxTerminal()
	{
		string[] terminals = { "gnome-terminal", "konsole", "xfce4-terminal", "xterm" };
		foreach (string terminal in terminals)
		{
			try
			{
				string output = _processExecutor.Execute("which", terminal, waitForExit: true);
				if (!string.IsNullOrWhiteSpace(output) && output.Contains('/'))
				{
					return terminal;
				}
			}
			catch
			{
				// Continue to next terminal
			}
		}
		return "xterm";
	}
}
