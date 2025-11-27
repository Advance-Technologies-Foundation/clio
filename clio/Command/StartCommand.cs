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
		[Option('w', "terminal", Required = false, HelpText = "Start Creatio in a new terminal window (default: background service)")]
		public bool Terminal { get; set; }
	}

	public class StartCommand : Command<StartOptions>
	{
		private readonly ISettingsRepository _settingsRepository;
		private readonly IDotnetExecutor _dotnetExecutor;
		private readonly ILogger _logger;
		private readonly IFileSystem _fileSystem;
		private readonly ICreatioHostService _creatioHostService;

		public StartCommand(ISettingsRepository settingsRepository, IDotnetExecutor dotnetExecutor,
			ILogger logger, IFileSystem fileSystem, ICreatioHostService creatioHostService) {
			_settingsRepository = settingsRepository;
			_dotnetExecutor = dotnetExecutor;
			_logger = logger;
			_fileSystem = fileSystem;
			_creatioHostService = creatioHostService;
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
			
			if (options.Terminal) {
				_logger.WriteInfo($"Starting Creatio application '{envName}' in a new terminal window...");
				_logger.WriteInfo($"Path: {env.EnvironmentPath}");
				_creatioHostService.StartInNewTerminal(env.EnvironmentPath, envName);
				_logger.WriteInfo("✓ Creatio application started successfully!");
				_logger.WriteInfo("Check the new terminal window for application logs.");
			} else {
				_logger.WriteInfo($"Starting Creatio application '{envName}' as a background service...");
				_logger.WriteInfo($"Path: {env.EnvironmentPath}");
				int? processId = _creatioHostService.StartInBackground(env.EnvironmentPath);
				if (processId.HasValue) {
					_logger.WriteInfo($"✓ Creatio application started successfully as background service (PID: {processId.Value})!");
					_logger.WriteInfo("Use 'clio start -w' to start with terminal window for logs.");
				} else {
					_logger.WriteInfo("✓ Creatio application started successfully as background service!");
				}
			}				return 0;
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
