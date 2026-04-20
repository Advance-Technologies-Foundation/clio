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
using Clio.Common.SystemServices;
using Clio.UserEnvironment;
using CommandLine;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Clio.Command;

[Verb("stop", Aliases = ["stop-creatio"], HelpText = "Stop Creatio application(s) and remove services")]
public class StopOptions : EnvironmentNameOptions{
	#region Properties: Public

	[Option("all", Required = false, HelpText = "Stop all Creatio services/processes")]
	public bool All { get; set; }

	#endregion
}

public class StopCommand : Command<StopOptions>{
	#region Fields: Private

	private readonly IIISAppPoolManager _iisAppPoolManager;
	private readonly IIISSiteDetector _iisSiteDetector;
	private readonly ILogger _logger;
	private readonly IProcessExecutor _processExecutor;
	private readonly ISystemServiceManager _serviceManager;
	private readonly ISettingsRepository _settingsRepository;

	#endregion

	#region Constructors: Public

	public StopCommand(ISettingsRepository settingsRepository, ISystemServiceManager serviceManager,
		ILogger logger, IIISAppPoolManager iisAppPoolManager, IIISSiteDetector iisSiteDetector,
		IProcessExecutor processExecutor) {
		_settingsRepository = settingsRepository;
		_serviceManager = serviceManager;
		_logger = logger;
		_iisAppPoolManager = iisAppPoolManager;
		_iisSiteDetector = iisSiteDetector;
		_processExecutor = processExecutor;
	}

	#endregion

	#region Methods: Private

	public event EventHandler<ProgressNotificationValue> StatusChanged;

	private void OnStatusChanged(ProgressNotificationValue e) {
		StatusChanged?.Invoke(this, e);
	}

	private bool ConfirmStop(List<EnvironmentSettings> environments) {
		_logger.WriteWarning($"\nThis will stop {environments.Count} Creatio service(s)/process(es):");
		foreach (EnvironmentSettings env in environments) {
			_logger.WriteInfo($"  - {GetEnvironmentName(env)} ({env.EnvironmentPath})");
		}

		_logger.WriteWarning("\nContinue? [y/N]: ");
		ConsoleKeyInfo answer = Console.ReadKey();
		_logger.WriteLine();

		return answer.KeyChar is 'y' or 'Y';
	}

	private async Task<int> ExecuteAsync(StopOptions options) {
		try {
			List<EnvironmentSettings> environmentsToStop = GetEnvironmentsToStop(options);
			int totalSteps = (environmentsToStop.Count * 3 ) + 1;
			uint stepsCompleted = 0;
			ProgressNotificationValue progressStep1 = new() {
				Progress = ++stepsCompleted,
				Total = totalSteps,
				Message = "Obtained environment(s) to stop",
			};
			OnStatusChanged(progressStep1);
			
			if (environmentsToStop.Count == 0) {
				_logger.WriteError("No environments found to stop.");
				return 1;
			}

			// Show confirmation unless --silent flag is set
			if (!options.IsSilent && !ConfirmStop(environmentsToStop)) {
				_logger.WriteInfo("Operation cancelled by user.");
				return 0;
			}

			int successCount = 0;
			int failureCount = 0;
			
			foreach (EnvironmentSettings env in environmentsToStop) {
				string envName = GetEnvironmentName(env);
				_logger.WriteInfo($"\nStopping environment: {envName}");

				bool stopped = false;
				// Try to stop IIS app pool first (Windows only)
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && await StopIISAppPool(env, envName)) {
					stopped = true;
				}
				
				ProgressNotificationValue progressStep2 = new() {
					Progress = ++stepsCompleted,
					Total = totalSteps,
					Message = stopped ? "Stopped IIS app pool" : "IIS app pool not running",
				};
				OnStatusChanged(progressStep2);

				// Try to stop OS service
				if (await StopOSService(env, envName)) {
					stopped = true;
				}

				ProgressNotificationValue progressStep3 = new() {
					Progress = ++stepsCompleted,
					Total = totalSteps,
					Message = stopped ? "Stopped OS service" : "OS service not running",
				};
				OnStatusChanged(progressStep3);
				
				// Try to stop background process
				if (await StopBackgroundProcess(env, envName)) {
					stopped = true;
				}
				ProgressNotificationValue progressStep4 = new() {
					Progress = ++stepsCompleted,
					Total = totalSteps,
					Message = stopped ? "Background Process stopped" : "Background Process not running",
				};
				OnStatusChanged(progressStep4);

				if (stopped) {
					successCount++;
					_logger.WriteInfo($"✓ Successfully stopped '{envName}'");
				}
				else {
					failureCount++;
					_logger.WriteWarning($"No running service or process found for '{envName}'");
				}
			}

			_logger.WriteInfo("\n=== Summary ===");
			_logger.WriteInfo($"Stopped: {successCount}");
			if (failureCount > 0) {
				_logger.WriteWarning($"Not found/Failed: {failureCount}");
			}

			return failureCount > 0 ? 1 : 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Failed to stop application(s): {ex.Message}");
			return 1;
		}
	}

	private List<EnvironmentSettings> GetAllEnvironmentsWithPath() {
		try {
			// Use reflection to access the internal _settings.Environments
			FieldInfo settingsField = _settingsRepository.GetType()
														 .GetField("_settings",
															 BindingFlags.NonPublic | BindingFlags.Instance);

			if (settingsField == null) {
				return [];
			}

			object settings = settingsField.GetValue(_settingsRepository);
			PropertyInfo environmentsProperty = settings.GetType()
														.GetProperty("Environments",
															BindingFlags.Public | BindingFlags.Instance);

			if (environmentsProperty == null) {
				return new List<EnvironmentSettings>();
			}

			Dictionary<string, EnvironmentSettings> environments = environmentsProperty.GetValue(settings)
				as Dictionary<string, EnvironmentSettings>;

			if (environments == null) {
				return new List<EnvironmentSettings>();
			}

			return environments.Values
							   .Where(e => !string.IsNullOrWhiteSpace(e.EnvironmentPath))
							   .ToList();
		}
		catch {
			return new List<EnvironmentSettings>();
		}
	}

	private string GetEnvironmentName(EnvironmentSettings env) {
		// Try to find environment name from settings repository
		try {
			FieldInfo settingsField = _settingsRepository.GetType()
														 .GetField("_settings",
															 BindingFlags.NonPublic | BindingFlags.Instance);

			if (settingsField != null) {
				object settings = settingsField.GetValue(_settingsRepository);
				PropertyInfo environmentsProperty = settings.GetType()
															.GetProperty("Environments",
																BindingFlags.Public | BindingFlags.Instance);

				if (environmentsProperty != null) {
					Dictionary<string, EnvironmentSettings> environments = environmentsProperty.GetValue(settings)
						as Dictionary<string, EnvironmentSettings>;

					if (environments != null) {
						KeyValuePair<string, EnvironmentSettings> entry
							= environments.FirstOrDefault(kvp => kvp.Value == env);
						if (!string.IsNullOrEmpty(entry.Key)) {
							return entry.Key;
						}
					}
				}
			}
		}
		catch {
			// Fallback to path-based name
		}

		// Fallback: use last directory segment of EnvironmentPath
		if (!string.IsNullOrWhiteSpace(env.EnvironmentPath)) {
			return Path.GetFileName(env.EnvironmentPath.TrimEnd(Path.DirectorySeparatorChar));
		}

		return "unknown";
	}

	private List<EnvironmentSettings> GetEnvironmentsToStop(StopOptions options) {
		List<EnvironmentSettings> environments = new();

		if (options.All) {
			// Get all environments with EnvironmentPath configured
			environments = GetAllEnvironmentsWithPath();
		}
		else {
			// Get specific environment
			string envName = options.Environment;
			if (string.IsNullOrWhiteSpace(envName)) {
				EnvironmentSettings defaultEnv = _settingsRepository.FindEnvironment();
				if (defaultEnv == null || string.IsNullOrWhiteSpace(defaultEnv.EnvironmentPath)) {
					throw new Exception(
						"No default environment configured with EnvironmentPath. Use -e to specify environment or --all to stop all.");
				}

				environments.Add(defaultEnv);
			}
			else {
				EnvironmentSettings env = _settingsRepository.GetEnvironment(envName);
				if (string.IsNullOrWhiteSpace(env.EnvironmentPath)) {
					throw new Exception($"Environment '{envName}' does not have EnvironmentPath configured.");
				}

				environments.Add(env);
			}
		}

		return environments;
	}

	private string GetServiceName(string envName, string envPath) {
		// Service name should match what was created during deployment
		// Priority: environment name, then last directory segment
		if (!string.IsNullOrWhiteSpace(envName) && envName != "unknown") {
			return envName;
		}

		if (!string.IsNullOrWhiteSpace(envPath)) {
			return Path.GetFileName(envPath.TrimEnd(Path.DirectorySeparatorChar));
		}

		return "default";
	}

	private async Task<bool> KillProcessesByPath(string targetPath) {
		try {
#pragma warning disable CLIO004 // Process enumeration cannot be abstracted via IProcessExecutor
			Process[] allProcesses = Process.GetProcesses();
			List<Process> dotnetProcesses = allProcesses.Where(p => {
				try {
					return p.ProcessName.ToLower().Contains("dotnet");
				}
				catch {
					return false;
				}
			}).ToList();
#pragma warning restore CLIO004

			bool killedAny = false;

#pragma warning disable CLIO004 // Process member access on already-obtained Process instances cannot be abstracted
			foreach (Process dotnetProcess in dotnetProcesses) {
				try {
					ProcessExecutionResult lsofResult = await _processExecutor.ExecuteAndCaptureAsync(
						new ProcessExecutionOptions("lsof", $"-p {dotnetProcess.Id}"));
					string output = lsofResult.StandardOutput;

					if (output.Contains(targetPath)) {
						_logger.WriteInfo(
							$"Killing process {dotnetProcess.ProcessName} (PID: {dotnetProcess.Id}) running from {targetPath}");
						dotnetProcess.Kill();
						await Task.Delay(500);
						killedAny = true;
					}
				}
				catch (Exception ex) {
					_logger.WriteWarning($"Could not check/kill process {dotnetProcess.Id}: {ex.Message}");
				}
			}
#pragma warning restore CLIO004

			return killedAny;
		}
		catch (Exception ex) {
			_logger.WriteWarning($"Error finding processes by path: {ex.Message}");
			return false;
		}
	}

	private async Task<bool> StopBackgroundProcess(EnvironmentSettings env, string envName) {
		try {
			if (string.IsNullOrWhiteSpace(env.EnvironmentPath)) {
				return false;
			}

			string targetPath = env.EnvironmentPath;
			bool processFound = false;

			// On macOS/Linux, use ps command to find processes with the target path
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				processFound = await KillProcessesByPath(targetPath);
			}
			else {
#pragma warning disable CLIO004 // Process enumeration and kill cannot be abstracted via IProcessExecutor
				Process[] processes = Process.GetProcesses();

				foreach (Process process in processes) {
					try {
						string processName = process.ProcessName.ToLower();
						if (processName.Contains("dotnet") || processName.Contains("creatio") ||
							processName.Contains("terrasoft") || processName.Contains("webhost")) {
							ProcessModuleCollection modules = process.Modules;
							foreach (ProcessModule module in modules) {
								if (module.FileName.Contains(targetPath, StringComparison.OrdinalIgnoreCase)) {
									_logger.WriteInfo($"Killing process {processName} (PID: {process.Id})");
									process.Kill();
									await Task.Delay(1000);
									processFound = true;
									break;
								}
							}
						}
					}
					catch {
					}
				}
#pragma warning restore CLIO004
			}

			if (processFound) {
				_logger.WriteInfo("✓ Background process stopped");
			}

			return processFound;
		}
		catch (Exception ex) {
			_logger.WriteWarning($"Error stopping background process: {ex.Message}");
			return false;
		}
	}

	private async Task<bool> StopIISAppPool(EnvironmentSettings env, string envName) {
		try {
			if (string.IsNullOrWhiteSpace(env.EnvironmentPath)) {
				return false;
			}

			List<IISSiteInfo> iisSites = await _iisSiteDetector.GetSitesByPath(env.EnvironmentPath);
			if (iisSites.Count == 0) {
				return false;
			}

			string appPoolName = iisSites[0].AppPoolName;
			if (string.IsNullOrWhiteSpace(appPoolName) || appPoolName == "Unknown") {
				return false;
			}

			_logger.WriteInfo($"Stopping IIS application pool: {appPoolName}");
			
			bool stopped = await _iisAppPoolManager.StopAppPool(appPoolName);
			if (stopped) {
				_logger.WriteInfo($"✓ IIS application pool '{appPoolName}' stopped successfully");
				return true;
			}

			_logger.WriteWarning($"Failed to stop IIS application pool '{appPoolName}'");
			return false;
		}
		catch (Exception ex) {
			_logger.WriteWarning($"Error stopping IIS application pool: {ex.Message}");
			return false;
		}
	}

	private async Task<bool> StopOSService(EnvironmentSettings env, string envName) {
		try {
			// Service name pattern: creatio-{siteName}
			// We need to derive siteName from environment path or name
			string serviceName = $"creatio-{GetServiceName(envName, env.EnvironmentPath)}";

			// Check if service exists and is running
			bool isRunning = await _serviceManager.IsServiceRunning(serviceName);
			if (!isRunning) {
				return false;
			}

			_logger.WriteInfo($"Stopping OS service: {serviceName}");

			// Stop the service
			bool stopped = await _serviceManager.StopService(serviceName);
			if (!stopped) {
				_logger.WriteWarning($"Failed to stop service '{serviceName}'");
				return false;
			}

			// Disable auto-start
			await _serviceManager.DisableService(serviceName);

			// Delete service configuration
			bool deleted = await _serviceManager.DeleteService(serviceName);
			if (deleted) {
				_logger.WriteInfo($"✓ Service '{serviceName}' stopped and removed");
			}
			else {
				_logger.WriteWarning($"Service '{serviceName}' stopped but configuration not removed");
			}

			return true;
		}
		catch (Exception ex) {
			_logger.WriteWarning($"Error stopping OS service: {ex.Message}");
			return false;
		}
	}

	#endregion

	#region Methods: Public

	public override int Execute(StopOptions options) {
		return ExecuteAsync(options).GetAwaiter().GetResult();
	}

	#endregion
}
