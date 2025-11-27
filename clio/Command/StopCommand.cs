using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.SystemServices;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command
{
	[Verb("stop", Aliases = new[] { "stop-creatio" }, HelpText = "Stop Creatio application(s) and remove services")]
	public class StopOptions : EnvironmentNameOptions
	{
		[Option("all", Required = false, HelpText = "Stop all Creatio services/processes")]
		public bool All { get; set; }

		[Option("quiet", Required = false, HelpText = "Stop without confirmation prompt")]
		public bool Quiet { get; set; }
	}

	public class StopCommand : Command<StopOptions>
	{
		private readonly ISettingsRepository _settingsRepository;
		private readonly ISystemServiceManager _serviceManager;
		private readonly ILogger _logger;
		private readonly IFileSystem _fileSystem;

		public StopCommand(ISettingsRepository settingsRepository, ISystemServiceManager serviceManager,
			ILogger logger, IFileSystem fileSystem)
		{
			_settingsRepository = settingsRepository;
			_serviceManager = serviceManager;
			_logger = logger;
			_fileSystem = fileSystem;
	}

	public override int Execute(StopOptions options)
	{
		return ExecuteAsync(options).GetAwaiter().GetResult();
	}

	private async Task<int> ExecuteAsync(StopOptions options)
	{
		try
		{
			List<EnvironmentSettings> environmentsToStop = GetEnvironmentsToStop(options);				if (environmentsToStop.Count == 0)
				{
					_logger.WriteError("No environments found to stop.");
					return 1;
				}

				// Show confirmation unless --quiet flag is set
				if (!options.Quiet && !ConfirmStop(environmentsToStop))
				{
					_logger.WriteInfo("Operation cancelled by user.");
					return 0;
				}

				int successCount = 0;
				int failureCount = 0;

				foreach (var env in environmentsToStop)
				{
					string envName = GetEnvironmentName(env);
					_logger.WriteInfo($"\nStopping environment: {envName}");

					bool stopped = false;

					// Try to stop OS service first
					if (await StopOSService(env, envName))
					{
						stopped = true;
					}

					// Try to stop background process
					if (await StopBackgroundProcess(env, envName))
					{
						stopped = true;
					}

					if (stopped)
					{
						successCount++;
						_logger.WriteInfo($"✓ Successfully stopped '{envName}'");
					}
					else
					{
						failureCount++;
						_logger.WriteWarning($"No running service or process found for '{envName}'");
					}
				}

				_logger.WriteInfo($"\n=== Summary ===");
				_logger.WriteInfo($"Stopped: {successCount}");
				if (failureCount > 0)
				{
					_logger.WriteWarning($"Not found/Failed: {failureCount}");
				}

				return failureCount > 0 ? 1 : 0;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Failed to stop application(s): {ex.Message}");
				return 1;
			}
		}

		private List<EnvironmentSettings> GetEnvironmentsToStop(StopOptions options)
		{
			var environments = new List<EnvironmentSettings>();

			if (options.All)
			{
				// Get all environments with EnvironmentPath configured
				environments = GetAllEnvironmentsWithPath();
			}
			else
			{
				// Get specific environment
				string envName = options.Environment;
				if (string.IsNullOrWhiteSpace(envName))
				{
					var defaultEnv = _settingsRepository.FindEnvironment(null);
					if (defaultEnv == null || string.IsNullOrWhiteSpace(defaultEnv.EnvironmentPath))
					{
						throw new Exception("No default environment configured with EnvironmentPath. Use -e to specify environment or --all to stop all.");
					}
					environments.Add(defaultEnv);
				}
				else
				{
					var env = _settingsRepository.GetEnvironment(envName);
					if (string.IsNullOrWhiteSpace(env.EnvironmentPath))
					{
						throw new Exception($"Environment '{envName}' does not have EnvironmentPath configured.");
					}
					environments.Add(env);
				}
			}

			return environments;
		}

		private List<EnvironmentSettings> GetAllEnvironmentsWithPath()
		{
			try
			{
				// Use reflection to access the internal _settings.Environments
				var settingsField = _settingsRepository.GetType()
					.GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance);

				if (settingsField == null)
					return new List<EnvironmentSettings>();

				var settings = settingsField.GetValue(_settingsRepository);
				var environmentsProperty = settings.GetType()
					.GetProperty("Environments", BindingFlags.Public | BindingFlags.Instance);

				if (environmentsProperty == null)
					return new List<EnvironmentSettings>();

				var environments = environmentsProperty.GetValue(settings)
					as Dictionary<string, EnvironmentSettings>;

				if (environments == null)
					return new List<EnvironmentSettings>();

				return environments.Values
					.Where(e => !string.IsNullOrWhiteSpace(e.EnvironmentPath))
					.ToList();
			}
			catch
			{
				return new List<EnvironmentSettings>();
			}
		}

		private bool ConfirmStop(List<EnvironmentSettings> environments)
		{
			_logger.WriteWarning($"\nThis will stop {environments.Count} Creatio service(s)/process(es):");
			foreach (var env in environments)
			{
				_logger.WriteInfo($"  - {GetEnvironmentName(env)} ({env.EnvironmentPath})");
			}
			_logger.WriteWarning("\nContinue? [y/N]: ");

			var answer = Console.ReadKey();
			Console.WriteLine();

			return answer.KeyChar == 'y' || answer.KeyChar == 'Y';
		}

		private async Task<bool> StopOSService(EnvironmentSettings env, string envName)
		{
			try
			{
				// Service name pattern: creatio-{siteName}
				// We need to derive siteName from environment path or name
				string serviceName = $"creatio-{GetServiceName(envName, env.EnvironmentPath)}";

				// Check if service exists and is running
				bool isRunning = await _serviceManager.IsServiceRunning(serviceName);
				if (!isRunning)
				{
					return false;
				}

				_logger.WriteInfo($"Stopping OS service: {serviceName}");

				// Stop the service
				bool stopped = await _serviceManager.StopService(serviceName);
				if (!stopped)
				{
					_logger.WriteWarning($"Failed to stop service '{serviceName}'");
					return false;
				}

				// Disable auto-start
				await _serviceManager.DisableService(serviceName);

				// Delete service configuration
				bool deleted = await _serviceManager.DeleteService(serviceName);
				if (deleted)
				{
					_logger.WriteInfo($"✓ Service '{serviceName}' stopped and removed");
				}
				else
				{
					_logger.WriteWarning($"Service '{serviceName}' stopped but configuration not removed");
				}

				return true;
			}
			catch (Exception ex)
			{
				_logger.WriteWarning($"Error stopping OS service: {ex.Message}");
				return false;
			}
		}

	private async Task<bool> StopBackgroundProcess(EnvironmentSettings env, string envName)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(env.EnvironmentPath))
			{
				return false;
			}

			string targetPath = env.EnvironmentPath;
			bool processFound = false;

			// On macOS/Linux, use ps command to find processes with the target path
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				processFound = await KillProcessesByPath(targetPath);
			}
			else
			{
				// On Windows, use Process.Modules
				Process[] processes = Process.GetProcesses();

				foreach (var process in processes)
				{
					try
					{
						string processName = process.ProcessName.ToLower();
						if (processName.Contains("dotnet") || processName.Contains("creatio") ||
							processName.Contains("terrasoft") || processName.Contains("webhost"))
						{
							// Try to get modules to see if any DLL is from target path
							var modules = process.Modules;
							foreach (ProcessModule module in modules)
							{
								if (module.FileName.Contains(targetPath, StringComparison.OrdinalIgnoreCase))
								{
									_logger.WriteInfo($"Killing process {processName} (PID: {process.Id})");
									process.Kill();
									await Task.Delay(1000); // Wait for process to terminate
									processFound = true;
									break;
								}
							}
						}
					}
					catch
					{
						// Ignore errors when checking specific processes (access denied, etc.)
					}
				}
			}

			if (processFound)
			{
				_logger.WriteInfo($"✓ Background process stopped");
			}

			return processFound;
		}
		catch (Exception ex)
		{
			_logger.WriteWarning($"Error stopping background process: {ex.Message}");
			return false;
		}
	}

	private async Task<bool> KillProcessesByPath(string targetPath)
	{
		try
		{
			// First, get all dotnet processes
			var allProcesses = Process.GetProcesses();
			var dotnetProcesses = allProcesses.Where(p => 
			{
				try
				{
					return p.ProcessName.ToLower().Contains("dotnet");
				}
				catch
				{
					return false;
				}
			}).ToList();

			bool killedAny = false;

			foreach (var dotnetProcess in dotnetProcesses)
			{
				try
				{
					// Use lsof to check if the process is running from the target path
					var lsofStartInfo = new ProcessStartInfo
					{
						FileName = "lsof",
						Arguments = $"-p {dotnetProcess.Id}",
						RedirectStandardOutput = true,
						UseShellExecute = false,
						CreateNoWindow = true
					};

					var lsofProcess = new Process { StartInfo = lsofStartInfo };
					lsofProcess.Start();

					var output = await lsofProcess.StandardOutput.ReadToEndAsync();
					await lsofProcess.WaitForExitAsync();

					// Check if the output contains the target path (working directory)
					if (output.Contains(targetPath))
					{
						_logger.WriteInfo($"Killing process {dotnetProcess.ProcessName} (PID: {dotnetProcess.Id}) running from {targetPath}");
						dotnetProcess.Kill();
						await Task.Delay(500);
						killedAny = true;
					}
				}
				catch (Exception ex)
				{
					// Process might have already exited or we don't have permission
					_logger.WriteWarning($"Could not check/kill process {dotnetProcess.Id}: {ex.Message}");
				}
			}

			return killedAny;
		}
		catch (Exception ex)
		{
			_logger.WriteWarning($"Error finding processes by path: {ex.Message}");
			return false;
		}
	}		private string GetEnvironmentName(EnvironmentSettings env)
		{
			// Try to find environment name from settings repository
			try
			{
				var settingsField = _settingsRepository.GetType()
					.GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance);

				if (settingsField != null)
				{
					var settings = settingsField.GetValue(_settingsRepository);
					var environmentsProperty = settings.GetType()
						.GetProperty("Environments", BindingFlags.Public | BindingFlags.Instance);

					if (environmentsProperty != null)
					{
						var environments = environmentsProperty.GetValue(settings)
							as Dictionary<string, EnvironmentSettings>;

						if (environments != null)
						{
							var entry = environments.FirstOrDefault(kvp => kvp.Value == env);
							if (!string.IsNullOrEmpty(entry.Key))
							{
								return entry.Key;
							}
						}
					}
				}
			}
			catch
			{
				// Fallback to path-based name
			}

			// Fallback: use last directory segment of EnvironmentPath
			if (!string.IsNullOrWhiteSpace(env.EnvironmentPath))
			{
				return Path.GetFileName(env.EnvironmentPath.TrimEnd(Path.DirectorySeparatorChar));
			}

			return "unknown";
		}

		private string GetServiceName(string envName, string envPath)
		{
			// Service name should match what was created during deployment
			// Priority: environment name, then last directory segment
			if (!string.IsNullOrWhiteSpace(envName) && envName != "unknown")
			{
				return envName;
			}

			if (!string.IsNullOrWhiteSpace(envPath))
			{
				return Path.GetFileName(envPath.TrimEnd(Path.DirectorySeparatorChar));
			}

			return "default";
		}
	}
}
