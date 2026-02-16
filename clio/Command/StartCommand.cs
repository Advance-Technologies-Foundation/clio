using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.IIS;
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
		private readonly IIISAppPoolManager _iisAppPoolManager;
		private readonly IIISSiteDetector _iisSiteDetector;
		private readonly IApplicationClient _applicationClient;

		public StartCommand(ISettingsRepository settingsRepository, IDotnetExecutor dotnetExecutor,
			ILogger logger, IFileSystem fileSystem, ICreatioHostService creatioHostService,
			IIISAppPoolManager iisAppPoolManager, IIISSiteDetector iisSiteDetector,
			IApplicationClient applicationClient) {
			_settingsRepository = settingsRepository;
			_dotnetExecutor = dotnetExecutor;
			_logger = logger;
			_fileSystem = fileSystem;
			_creatioHostService = creatioHostService;
			_iisAppPoolManager = iisAppPoolManager;
			_iisSiteDetector = iisSiteDetector;
			_applicationClient = applicationClient;
		}

		public override int Execute(StartOptions options) {
			return ExecuteAsync(options).GetAwaiter().GetResult();
		}

		private async Task<int> ExecuteAsync(StartOptions options) {
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

				string envName = options.Environment ?? "default";

			// Check if this is an IIS deployment
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				var iisSites = await _iisSiteDetector.GetSitesByPath(env.EnvironmentPath);
				if (iisSites.Count > 0)
				{
					return await StartIISSite(envName, iisSites[0]);
				}
			}

				// Fall back to .NET Core deployment
				string dllPath = Path.Combine(env.EnvironmentPath, "Terrasoft.WebHost.dll");
				if (!_fileSystem.ExistsFile(dllPath)) {
					_logger.WriteError($"Terrasoft.WebHost.dll not found at: {dllPath}");
					_logger.WriteInfo("This environment does not appear to be a .NET deployment.");
					return 1;
				}
			
		if (options.Terminal) {
			_logger.WriteInfo($"Starting Creatio application '{envName}' in a new terminal window...");
			_logger.WriteInfo($"Path: {env.EnvironmentPath}");
			_creatioHostService.StartInNewTerminal(env.EnvironmentPath, envName);
			_logger.WriteInfo("✓ Creatio application started successfully!");
			_logger.WriteInfo("Check the new terminal window for application logs.");
			return await PingSite(envName);
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
			return await PingSite(envName);
		}
			}
			catch (Exception ex) {
				_logger.WriteError($"Failed to start application: {ex.Message}");
				return 1;
			}
		}

		private async Task<int> StartIISAppPool(string envName, string appPoolName)
		{
			_logger.WriteInfo($"Starting IIS application pool '{appPoolName}' for environment '{envName}'...");

			try
			{
				bool isRunning = await _iisAppPoolManager.IsAppPoolRunning(appPoolName);
				if (isRunning)
				{
					_logger.WriteInfo($"Application pool '{appPoolName}' is already running.");
					return 0;
				}

				bool started = await _iisAppPoolManager.StartAppPool(appPoolName);
				if (started)
				{
					_logger.WriteInfo($"✓ IIS application pool '{appPoolName}' started successfully!");
					return 0;
				}
				else
				{
					_logger.WriteError($"Failed to start IIS application pool '{appPoolName}'.");
					_logger.WriteInfo("You may need to run this command with Administrator privileges.");
					return 1;
				}
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Error starting IIS application pool: {ex.Message}");
				return 1;
			}
		}

		private async Task<int> StartIISSite(string envName, IISSiteInfo siteInfo)
		{
			_logger.WriteInfo($"Starting IIS site '{siteInfo.SiteName}' and application pool '{siteInfo.AppPoolName}' for environment '{envName}'...");

			try
			{
				// Check if site and app pool are already running
				bool siteRunning = await _iisAppPoolManager.IsSiteRunning(siteInfo.SiteName);
				bool appPoolRunning = await _iisAppPoolManager.IsAppPoolRunning(siteInfo.AppPoolName);

				if (siteRunning && appPoolRunning)
				{
					_logger.WriteInfo($"IIS site '{siteInfo.SiteName}' and application pool '{siteInfo.AppPoolName}' are already running.");
					return 0;
				}

				// Start the app pool first (required before starting the site)
				if (!appPoolRunning)
				{
					_logger.WriteInfo($"Starting application pool '{siteInfo.AppPoolName}'...");
					bool appPoolStarted = await _iisAppPoolManager.StartAppPool(siteInfo.AppPoolName);
					if (!appPoolStarted)
					{
						_logger.WriteError($"Failed to start IIS application pool '{siteInfo.AppPoolName}'.");
						_logger.WriteInfo("You may need to run this command with Administrator privileges.");
						return 1;
					}
					_logger.WriteInfo($"✓ Application pool '{siteInfo.AppPoolName}' started successfully!");
				}

				// Start the site
				if (!siteRunning)
				{
					_logger.WriteInfo($"Starting IIS site '{siteInfo.SiteName}'...");
					bool siteStarted = await _iisAppPoolManager.StartSite(siteInfo.SiteName);
					if (!siteStarted)
					{
						_logger.WriteError($"Failed to start IIS site '{siteInfo.SiteName}'.");
						_logger.WriteInfo("You may need to run this command with Administrator privileges.");
						return 1;
					}
					_logger.WriteInfo($"✓ IIS site '{siteInfo.SiteName}' started successfully!");
				}

			_logger.WriteInfo($"✓ Creatio application '{envName}' is now running!");
			
			// Ping the site to verify it's accessible
			return await PingSite(envName);
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Error starting IIS site and application pool: {ex.Message}");
			return 1;
		}
	}

	private async Task<int> PingSite(string envName)
	{
		try
		{
			EnvironmentSettings env = _settingsRepository.GetEnvironment(envName);
			if (env == null || string.IsNullOrWhiteSpace(env.Uri))
			{
				_logger.WriteInfo("Skipping ping: environment URI not configured.");
				return 0;
			}

			_logger.WriteInfo($"Pinging {env.Uri}/ping to verify accessibility...");
			
			// Wait a moment for IIS to fully start
			await Task.Delay(2000);
			
			string pingUrl = $"{env.Uri}/ping";
			_applicationClient.ExecuteGetRequest(pingUrl, 30000, 3, 2);
			
			_logger.WriteInfo($"✓ Site is accessible and responding!");
			return 0;
		}
		catch (Exception ex)
		{
			_logger.WriteWarning($"Site started but ping failed: {ex.Message}");
			_logger.WriteInfo("The site may still be starting up. Try accessing it manually.");
			return 0; // Don't fail the command if ping fails
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
