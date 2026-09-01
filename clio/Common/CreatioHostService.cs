using System;
using System.Collections.Generic;
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
	private readonly IFileSystem _fileSystem;
	private readonly IFileSecurityHardening _fileSecurityHardening;

	/// <summary>
	/// Initializes a new instance of the <see cref="CreatioHostService"/> class.
	/// </summary>
	/// <param name="logger">Logger used for host lifecycle messages.</param>
	/// <param name="processExecutor">Process launcher used to start the host.</param>
	/// <param name="environmentStore">Persistent store for sensitive host environment values.</param>
	/// <param name="fileSystem">File-system abstraction used for the protected terminal launcher.</param>
	/// <param name="fileSecurityHardening">Helper that restricts the terminal launcher to the current user.</param>
	public CreatioHostService(
		ILogger logger,
		IProcessExecutor processExecutor,
		ICreatioHostEnvironmentStore environmentStore,
		IFileSystem fileSystem,
		IFileSecurityHardening fileSecurityHardening)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
		_environmentStore = environmentStore ?? throw new ArgumentNullException(nameof(environmentStore));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_fileSecurityHardening = fileSecurityHardening ?? throw new ArgumentNullException(nameof(fileSecurityHardening));
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
			string windowsArgs = "/c start \"Creatio\" cmd.exe /k dotnet Terrasoft.WebHost.dll";
			StartTerminalProcess("cmd.exe", windowsArgs, workingDirectory, environmentVariables);
			return;
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			string scriptPath = CreateMacOsTerminalLaunchScript(workingDirectory, envName, environmentVariables);
			try
			{
				string command = $"/bin/sh {EscapeShellSingleQuoted(scriptPath)}";
				string script = $"tell application \"Terminal\" to do script \"{command}\"";
				ProcessLaunchResult result = StartTerminalProcess(
					"osascript",
					$"-e \"{EscapeAppleScriptString(script)}\"",
					workingDirectory,
					environmentVariables);
				if (result is null || !result.Started)
				{
					throw new InvalidOperationException(
						$"Unable to start the macOS terminal launcher: {result?.ErrorMessage ?? "process did not start"}.");
				}
			}
			catch
			{
				_fileSystem.DeleteFileIfExists(scriptPath);
				throw;
			}
			return;
		}
		string terminal = GetLinuxTerminal();
		string linuxArgs = "-e \"bash -c 'echo Starting Creatio...; dotnet Terrasoft.WebHost.dll; exec bash'\"";
		StartTerminalProcess(terminal, linuxArgs, workingDirectory, environmentVariables);
	}

	private static string EscapeShellSingleQuoted(string value) =>
		$"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

	private static string EscapeAppleScriptString(string value) =>
		value.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("\"", "\\\"", StringComparison.Ordinal);

	private string CreateMacOsTerminalLaunchScript(
		string workingDirectory,
		string envName,
		IReadOnlyDictionary<string, string> environmentVariables)
	{
		string directory = Path.Combine(ClioRuntimePaths.Home, "host-environments");
		EnsureNotSymbolicLink(ClioRuntimePaths.Home, isDirectory: true);
		EnsureNotSymbolicLink(directory, isDirectory: true);
		_fileSystem.CreateDirectoryIfNotExists(directory);
		EnsureNotSymbolicLink(directory, isDirectory: true);
		_fileSecurityHardening.HardenDirectory(directory);
		string scriptPath = Path.Combine(directory, $"terminal-{Guid.NewGuid():N}.sh");
		try
		{
			string script = BuildTerminalLaunchScript(workingDirectory, envName, environmentVariables);
			EnsureNotSymbolicLink(scriptPath, isDirectory: false);
			_fileSystem.WriteOwnerOnlyTextToFile(scriptPath, script);
			_fileSecurityHardening.HardenFile(scriptPath);
			return scriptPath;
		}
		catch
		{
			_fileSystem.DeleteFileIfExists(scriptPath);
			throw;
		}
	}

	private void EnsureNotSymbolicLink(string path, bool isDirectory)
	{
		System.IO.Abstractions.IFileSystemInfo fileSystemInfo = isDirectory
			? _fileSystem.GetDirectoryInfo(path)
			: _fileSystem.GetFilesInfos(path);
		if (fileSystemInfo is not null
			&& (!string.IsNullOrEmpty(fileSystemInfo.LinkTarget)
				|| fileSystemInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)))
		{
			throw new IOException($"The terminal launcher path must not be a symbolic link: {path}.");
		}
	}

	internal static string BuildTerminalLaunchScript(
		string workingDirectory,
		string envName,
		IReadOnlyDictionary<string, string> environmentVariables)
	{
		List<string> lines = [
			"#!/bin/sh",
			"set -eu",
			"cleanup() { rm -f -- \"$0\"; }",
			"trap cleanup EXIT HUP INT TERM"
		];
		foreach ((string key, string value) in environmentVariables ?? new Dictionary<string, string>())
		{
			if (!IsValidShellEnvironmentVariableName(key))
			{
				throw new InvalidOperationException(
					$"The host environment variable '{key}' cannot be passed to a POSIX terminal launcher.");
			}

			lines.Add($"export {key}={EscapeShellSingleQuoted(value)}");
		}

		string displayName = string.IsNullOrWhiteSpace(envName) ? "environment" : envName;
		lines.Add($"cd -- {EscapeShellSingleQuoted(workingDirectory)}");
		lines.Add($"echo {EscapeShellSingleQuoted($"Starting Creatio [{displayName}]...")}");
		lines.Add("dotnet Terrasoft.WebHost.dll");
		return string.Join(Environment.NewLine, lines) + Environment.NewLine;
	}

	private static bool IsValidShellEnvironmentVariableName(string value)
	{
		if (string.IsNullOrEmpty(value)
			|| !(value[0] == '_' || value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
		{
			return false;
		}

		for (int index = 1; index < value.Length; index++)
		{
			char character = value[index];
			if (!(character == '_' || character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))
			{
				return false;
			}
		}

		return true;
	}

	private ProcessLaunchResult StartTerminalProcess(
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
		return _processExecutor.FireAndForgetAsync(options).GetAwaiter().GetResult();
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
