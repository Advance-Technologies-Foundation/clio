using System;
using System.Diagnostics;
using System.IO;
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

	public CreatioHostService(ILogger logger)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <summary>
	/// Starts the Creatio host process in the background.
	/// The process runs detached and unmanaged - user can stop it via 'clio stop' or manual termination.
	/// Output is suppressed to avoid cluttering the console.
	/// </summary>
	public int? StartInBackground(string workingDirectory)
	{
		try
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = "dotnet",
				Arguments = "Terrasoft.WebHost.dll",
				WorkingDirectory = workingDirectory,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};

			Process process = Process.Start(startInfo);
			
			if (process != null)
			{
				// Consume output streams to prevent process blocking
				// but don't log them to console to avoid clutter
				process.OutputDataReceived += (sender, e) => { /* Suppress output */ };
				process.ErrorDataReceived += (sender, e) => { /* Suppress errors */ };
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
				
				_logger.WriteInfo($"Started Creatio host process (PID: {process.Id})");
				_logger.WriteInfo($"To view logs: check application log files in the Creatio directory");
				return process.Id;
			}
			else
			{
				_logger.WriteWarning("Failed to start host process - process returned null");
				return null;
			}
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
		ProcessStartInfo startInfo;

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			// Windows: Use cmd.exe to start a new window
			startInfo = new ProcessStartInfo
			{
				FileName = "cmd.exe",
				Arguments = $"/k \"cd /d \"{workingDirectory}\" && dotnet Terrasoft.WebHost.dll\"",
				UseShellExecute = true,
				CreateNoWindow = false
			};
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			// macOS: Use osascript to open Terminal.app with the command
			string command = $"cd '{workingDirectory}' && echo 'Starting Creatio [{envName}]...' && dotnet Terrasoft.WebHost.dll";
			string script = $"tell application \\\"Terminal\\\" to do script \\\"{command}\\\"";
			startInfo = new ProcessStartInfo
			{
				FileName = "osascript",
				Arguments = $"-e \"{script}\"",
				UseShellExecute = false,
				CreateNoWindow = true
			};
		}
		else
		{
			// Linux: Try common terminal emulators
			string terminal = GetLinuxTerminal();
			startInfo = new ProcessStartInfo
			{
				FileName = terminal,
				Arguments = $"--working-directory=\"{workingDirectory}\" -e \"bash -c 'echo Starting Creatio [{envName}]...; dotnet Terrasoft.WebHost.dll; exec bash'\"",
				UseShellExecute = false,
				CreateNoWindow = false
			};
		}

		Process.Start(startInfo);
	}

	private string GetLinuxTerminal()
	{
		// Try to find available terminal emulator on Linux
		string[] terminals = { "gnome-terminal", "konsole", "xfce4-terminal", "xterm" };
		
		foreach (string terminal in terminals)
		{
			try
			{
				var process = Process.Start(new ProcessStartInfo
				{
					FileName = "which",
					Arguments = terminal,
					RedirectStandardOutput = true,
					UseShellExecute = false
				});
				
				if (process != null)
				{
					process.WaitForExit();
					if (process.ExitCode == 0)
					{
						return terminal;
					}
				}
			}
			catch
			{
				// Continue to next terminal
			}
		}
		
		return "xterm"; // Fallback
	}
}
