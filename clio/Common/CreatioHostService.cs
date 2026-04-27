using System;
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
	int? StartInBackground(string workingDirectory);

	/// <summary>
	/// Starts Creatio host in a new terminal window.
	/// </summary>
	void StartInNewTerminal(string workingDirectory, string envName);
}

public class CreatioHostService : ICreatioHostService
{
	private readonly ILogger _logger;
	private readonly IProcessExecutor _processExecutor;

	public CreatioHostService(ILogger logger, IProcessExecutor processExecutor)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
	}

	/// <summary>
	/// Starts the Creatio host process in the background.
	/// The process runs detached and unmanaged - user can stop it via 'clio stop' or manual termination.
	/// </summary>
	public int? StartInBackground(string workingDirectory)
	{
		try
		{
			ProcessExecutionOptions options = new("dotnet", "Terrasoft.WebHost.dll") {
				WorkingDirectory = workingDirectory
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

	/// <summary>
	/// Starts the Creatio host process in a new terminal window.
	/// </summary>
	public void StartInNewTerminal(string workingDirectory, string envName)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			string windowsArgs = $"/c start \"Creatio [{envName}]\" cmd.exe /k \"cd /d \"{workingDirectory}\" && dotnet Terrasoft.WebHost.dll\"";
			_processExecutor.Execute("cmd.exe", windowsArgs, waitForExit: false);
			return;
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			string command = $"cd '{workingDirectory}' && echo 'Starting Creatio [{envName}]...' && dotnet Terrasoft.WebHost.dll";
			string script = $"tell application \\\"Terminal\\\" to do script \\\"{command}\\\"";
			_processExecutor.Execute("osascript", $"-e \"{script}\"", waitForExit: false);
			return;
		}
		string terminal = GetLinuxTerminal();
		string linuxArgs = $"--working-directory=\"{workingDirectory}\" -e \"bash -c 'echo Starting Creatio [{envName}]...; dotnet Terrasoft.WebHost.dll; exec bash'\"";
		_processExecutor.Execute(terminal, linuxArgs, waitForExit: false);
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
