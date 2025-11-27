using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command
{
	[Verb("start", Aliases = new string[] { "start-server", "start-creatio", "sc" }, HelpText = "Start local Creatio application")]
	public class StartOptions : EnvironmentNameOptions
	{
	}

	public class StartCommand : Command<StartOptions>
	{
		private readonly ISettingsRepository _settingsRepository;
		private readonly IDotnetExecutor _dotnetExecutor;
		private readonly ILogger _logger;
		private readonly IFileSystem _fileSystem;

		public StartCommand(ISettingsRepository settingsRepository, IDotnetExecutor dotnetExecutor,
			ILogger logger, IFileSystem fileSystem) {
			_settingsRepository = settingsRepository;
			_dotnetExecutor = dotnetExecutor;
			_logger = logger;
			_fileSystem = fileSystem;
		}

		public override int Execute(StartOptions options) {
			try {
				// If no environment specified, try to get the default or show available environments
				if (string.IsNullOrWhiteSpace(options.Environment)) {
					var defaultEnv = _settingsRepository.FindEnvironment(null);
					if (defaultEnv == null || string.IsNullOrWhiteSpace(defaultEnv.EnvironmentPath)) {
						_logger.WriteError("No default environment configured with EnvironmentPath.");
						_logger.WriteInfo("\nAvailable environments with EnvironmentPath:");
						ShowAvailableEnvironments();
						_logger.WriteInfo("\nUse: clio start -e <environment_name>");
						return 1;
					}
				}

				EnvironmentSettings env = GetEnvironmentSettings(options);

				if (string.IsNullOrWhiteSpace(env.EnvironmentPath)) {
					_logger.WriteError("EnvironmentPath is not configured for this environment.");
					_logger.WriteInfo("\nAvailable environments with EnvironmentPath:");
					ShowAvailableEnvironments();
					_logger.WriteInfo("\nUse: clio reg-web-app <env> --ep <path>");
					return 1;
				}

				if (!_fileSystem.ExistsDirectory(env.EnvironmentPath)) {
					_logger.WriteError($"Environment path does not exist: {env.EnvironmentPath}");
					return 1;
				}

				string dllPath = Path.Combine(env.EnvironmentPath, "Terrasoft.WebHost.dll");
				if (!_fileSystem.ExistsFile(dllPath)) {
					_logger.WriteError($"Terrasoft.WebHost.dll not found at: {dllPath}");
					return 1;
				}

				string envName = options.Environment ?? "default";
				_logger.WriteInfo($"Starting Creatio application '{envName}' in a new terminal window...");
				_logger.WriteInfo($"Path: {env.EnvironmentPath}");
				
				StartInNewTerminal(env.EnvironmentPath, envName);
				
				_logger.WriteInfo("✓ Creatio application started successfully!");
				_logger.WriteInfo("Check the new terminal window for application logs.");

				return 0;
			}
			catch (Exception ex) {
				_logger.WriteError($"Failed to start application: {ex.Message}");
				return 1;
			}
		}

		private EnvironmentSettings GetEnvironmentSettings(StartOptions options) {
			// Get environment by name (uses default if not specified)
			// Don't use GetEnvironment(options) as Fill() doesn't preserve EnvironmentPath
			return _settingsRepository.GetEnvironment(options.Environment);
		}

		private void StartInNewTerminal(string workingDirectory, string envName) {
			ProcessStartInfo startInfo;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				// Windows: Use cmd.exe to start a new window
				startInfo = new ProcessStartInfo {
					FileName = "cmd.exe",
					Arguments = $"/k \"cd /d \"{workingDirectory}\" && dotnet Terrasoft.WebHost.dll\"",
					UseShellExecute = true,
					CreateNoWindow = false
				};
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				// macOS: Use osascript to open Terminal.app with the command
				string command = $"cd '{workingDirectory}' && echo 'Starting Creatio [{envName}]...' && dotnet Terrasoft.WebHost.dll";
				string script = $"tell application \\\"Terminal\\\" to do script \\\"{command}\\\"";
				startInfo = new ProcessStartInfo {
					FileName = "osascript",
					Arguments = $"-e \"{script}\"",
					UseShellExecute = false,
					CreateNoWindow = true
				};
			}
			else {
				// Linux: Try common terminal emulators
				string terminal = GetLinuxTerminal();
				startInfo = new ProcessStartInfo {
					FileName = terminal,
					Arguments = $"--working-directory=\"{workingDirectory}\" -e \"bash -c 'echo Starting Creatio [{envName}]...; dotnet Terrasoft.WebHost.dll; exec bash'\"",
					UseShellExecute = false,
					CreateNoWindow = false
				};
			}

			Process.Start(startInfo);
		}

		private string GetLinuxTerminal() {
			// Try to find available terminal emulator on Linux
			string[] terminals = { "gnome-terminal", "konsole", "xfce4-terminal", "xterm" };
			
			foreach (string terminal in terminals) {
				try {
					var process = Process.Start(new ProcessStartInfo {
						FileName = "which",
						Arguments = terminal,
						RedirectStandardOutput = true,
						UseShellExecute = false
					});
					
					if (process != null) {
						process.WaitForExit();
						if (process.ExitCode == 0) {
							return terminal;
						}
					}
				}
				catch {
					// Continue to next terminal
				}
			}
			
			return "xterm"; // Fallback
		}

		private void ShowAvailableEnvironments() {
			try {
				// Use reflection to access the internal _settings.Environments
				var settingsField = _settingsRepository.GetType()
					.GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance);
				
				if (settingsField == null) return;
				
				var settings = settingsField.GetValue(_settingsRepository);
				var environmentsProperty = settings.GetType()
					.GetProperty("Environments", BindingFlags.Public | BindingFlags.Instance);
				
				if (environmentsProperty == null) return;
				
				var environments = environmentsProperty.GetValue(settings) 
					as Dictionary<string, EnvironmentSettings>;
				
				if (environments == null || !environments.Any()) {
					_logger.WriteInfo("  No environments configured.");
					return;
				}

				var envsWithPath = environments
					.Where(e => !string.IsNullOrWhiteSpace(e.Value.EnvironmentPath))
					.ToList();

				if (!envsWithPath.Any()) {
					_logger.WriteInfo("  No environments have EnvironmentPath configured.");
					return;
				}

				foreach (var env in envsWithPath) {
					_logger.WriteInfo($"  - {env.Key}: {env.Value.EnvironmentPath}");
				}
			}
			catch {
				// If reflection fails, just skip showing the list
			}
		}
	}
}
