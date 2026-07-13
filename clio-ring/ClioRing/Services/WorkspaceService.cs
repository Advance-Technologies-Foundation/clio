using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ClioRing.Models;

namespace ClioRing.Services;

public class WorkspaceService
{
	private const string AppSettingsFileName = "app-settings.json";
	private const string TasksFolder = "tasks";
	private const string IconsFolder = "icons";
	private const string NetFrameworkScript = "open-test-solution-framework.cmd";
	private const string MainNetFrameworkScript = "open-main-solution-framework.cmd";
	private const string NetCoreScript = "open-test-solution-netcore.cmd";
	private const string MainNetCoreScript = "open-main-solution-netcore.cmd";

	public AppSettings LoadSettings()
	{
		try
		{
			var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppSettingsFileName);
			if (!File.Exists(settingsPath))
			{
				throw new FileNotFoundException($"Configuration file not found: {settingsPath}");
			}

			var json = File.ReadAllText(settingsPath);

			var settings = JsonSerializer.Deserialize(json, RingJsonContext.Default.AppSettings);

			if (settings == null)
			{
				throw new InvalidOperationException("Failed to deserialize app settings");
			}

			return settings;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to load settings: {ex.Message}", ex);
		}
	}

	public List<Workspace> DiscoverWorkspaces(string workspaceFolder)
	{
		if (!Directory.Exists(workspaceFolder))
		{
			throw new DirectoryNotFoundException($"Workspace folder not found: {workspaceFolder}");
		}

		var workspaces = new List<Workspace>();
		var directories = Directory.GetDirectories(workspaceFolder);

		foreach (var directory in directories)
		{
			var directoryInfo = new DirectoryInfo(directory);
			var tasksPath = Path.Combine(directory, TasksFolder);

			if (!Directory.Exists(tasksPath))
			{
				continue; // Skip if no tasks folder exists
			}

			string netFrameworkScriptPath = File.Exists(Path.Combine(tasksPath, MainNetFrameworkScript))
			? Path.Combine(tasksPath, MainNetFrameworkScript)
			: Path.Combine(tasksPath, NetFrameworkScript);

			string netCoreScriptPath =  File.Exists(Path.Combine(tasksPath, MainNetFrameworkScript))
			? Path.Combine(tasksPath, MainNetCoreScript)
			: Path.Combine(tasksPath, NetCoreScript);

			// Find icon if exists
			string? iconPath = null;
			var iconsPath = Path.Combine(directory, IconsFolder);
			if (Directory.Exists(iconsPath))
			{
				var iconFiles = Directory.GetFiles(iconsPath, "*.*")
					.Where(f =>
						//f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
						f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
						f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
						f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
						f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) //||
						//f.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
						)
					.ToList();

				if (iconFiles.Any())
				{
					iconPath = iconFiles.First();
				}
			}

			Workspace workspace = new () {
				Name = directoryInfo.Name,
				Path = directory,
				IconPath = iconPath,
				HasNetFrameworkScript = File.Exists(netFrameworkScriptPath),
				HasNetCoreScript = File.Exists(netCoreScriptPath),
				IsGitRepository = IsGitRepository(directory),
				CurrentBranch = TryGetCurrentBranch(directory)
			};

			workspaces.Add(workspace);
		}

		return workspaces;
	}

	public void ExecuteScript(string workspacePath, bool isNetCore) {

		string netFrameworkScriptPath = File.Exists(Path.Combine(workspacePath, TasksFolder, MainNetFrameworkScript))
			? Path.Combine(workspacePath, TasksFolder, MainNetFrameworkScript)
			: Path.Combine(workspacePath, TasksFolder, NetFrameworkScript);

		string netCoreScriptPath =  File.Exists(Path.Combine(workspacePath, TasksFolder, MainNetFrameworkScript))
			? Path.Combine(workspacePath, TasksFolder, MainNetCoreScript)
			: Path.Combine(workspacePath, TasksFolder, NetCoreScript);


		string scriptPath = isNetCore ? netCoreScriptPath : netFrameworkScriptPath;

		// var scriptName = isNetCore ? NetCoreScript : NetFrameworkScript;
		// var scriptPath = Path.Combine(workspacePath, TasksFolder, scriptName);

		if (!File.Exists(scriptPath)) {
			throw new FileNotFoundException($"Script not found: {scriptPath}");
		}

		try
		{
			var processStartInfo = new ProcessStartInfo
			{
				FileName = scriptPath,
				WorkingDirectory = Path.GetDirectoryName(scriptPath),
				UseShellExecute = true
			};

			Process.Start(processStartInfo);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to execute script: {ex.Message}", ex);
		}
	}

	public void GitPull(string workspacePath)
	{
		if (!IsGitRepository(workspacePath))
		{
			throw new InvalidOperationException($"Not a git repository: {workspacePath}");
		}

		try
		{
			var processStartInfo = new ProcessStartInfo
			{
				FileName = "git", // NOSONAR: user-installed git is intentionally resolved via PATH
				Arguments = $"-C \"{workspacePath}\" pull",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			using var process = new Process { StartInfo = processStartInfo };
			process.Start();

			Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
			Task<string> errorTask = process.StandardError.ReadToEndAsync();

			process.WaitForExit();
			Task.WaitAll(outputTask, errorTask);

			string output = outputTask.Result.Trim();
			string error = errorTask.Result.Trim();

			if (process.ExitCode != 0)
			{
				string details = string.IsNullOrWhiteSpace(error) ? output : error;
				throw new InvalidOperationException($"git pull failed: {details}");
			}
		}
		catch (Exception ex) when (ex is not InvalidOperationException)
		{
			throw new InvalidOperationException($"Failed to run git pull: {ex.Message}", ex);
		}
	}

	public void OpenInVsCode(string workspacePath)
	{
		if (!Directory.Exists(workspacePath))
		{
			throw new DirectoryNotFoundException($"Workspace folder not found: {workspacePath}");
		}

		try
		{
			string? codeExecutable = ResolveVsCodeExecutable();
			ProcessStartInfo processStartInfo = codeExecutable is not null
				? new ProcessStartInfo
				{
					FileName = codeExecutable,
					Arguments = $"\"{workspacePath}\"",
					UseShellExecute = false,
					CreateNoWindow = true
				}
				: new ProcessStartInfo
				{
					FileName = "cmd.exe", // NOSONAR: Windows system command host resolves from the protected system path
					Arguments = $"/c code \"{workspacePath}\"",
					UseShellExecute = false,
					CreateNoWindow = true
				};

			Process? process = Process.Start(processStartInfo);
			if (process is null)
			{
				throw new InvalidOperationException("Failed to start VS Code process.");
			}
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException(
				"Failed to open workspace in VS Code. Ensure VS Code is installed and the 'code' command is available.",
				ex);
		}
	}

	private static bool IsGitRepository(string workspacePath)
	{
		string gitPath = Path.Combine(workspacePath, ".git");
		return Directory.Exists(gitPath) || File.Exists(gitPath);
	}

	private static string? TryGetCurrentBranch(string workspacePath)
	{
		try
		{
			string? gitDirectory = ResolveGitDirectory(workspacePath);
			if (string.IsNullOrWhiteSpace(gitDirectory))
			{
				return null;
			}

			string headPath = Path.Combine(gitDirectory, "HEAD");
			if (!File.Exists(headPath))
			{
				return null;
			}

			string head = File.ReadAllText(headPath).Trim();
			const string headRefPrefix = "ref:";
			if (head.StartsWith(headRefPrefix, StringComparison.OrdinalIgnoreCase))
			{
				string reference = head[headRefPrefix.Length..].Trim();
				const string headsPrefix = "refs/heads/";
				if (reference.StartsWith(headsPrefix, StringComparison.OrdinalIgnoreCase))
				{
					return reference[headsPrefix.Length..];
				}

				return reference;
			}

			return head.Length > 7 ? head[..7] : head;
		}
		catch
		{
			return null;
		}
	}

	private static string? ResolveGitDirectory(string workspacePath)
	{
		string gitPath = Path.Combine(workspacePath, ".git");
		if (Directory.Exists(gitPath))
		{
			return gitPath;
		}

		if (!File.Exists(gitPath))
		{
			return null;
		}

		string pointer = File.ReadAllText(gitPath).Trim();
		const string gitDirPrefix = "gitdir:";
		if (!pointer.StartsWith(gitDirPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		string gitDirectory = pointer[gitDirPrefix.Length..].Trim();
		if (Path.IsPathRooted(gitDirectory))
		{
			return gitDirectory;
		}

		return Path.GetFullPath(Path.Combine(workspacePath, gitDirectory));
	}

	private static string? ResolveVsCodeExecutable()
	{
		var candidates = new[]
		{
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Programs",
				"Microsoft VS Code",
				"Code.exe"),
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
				"Microsoft VS Code",
				"Code.exe"),
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
				"Microsoft VS Code",
				"Code.exe")
		};

		return candidates.FirstOrDefault(File.Exists);
	}
}
