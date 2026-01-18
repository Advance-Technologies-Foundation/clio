using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.IIS;
using Clio.Common.SystemServices;
using Clio.UserEnvironment;
using CommandLine;
using ConsoleTables;

namespace Clio.Command;

[Verb("hosts", Aliases = ["list-hosts"], HelpText = "List all Creatio hosts and their status")]
public class HostsOptions : BaseCommandOptions{ }

public class HostsCommand(
	ISettingsRepository settingsRepository, 
	ISystemServiceManager serviceManager, 
	IIISSiteDetector iisSiteDetector,
	ILogger logger)
	: Command<HostsOptions>{
	
	#region Class: Nested

	private class HostInfo{
		#region Properties: Public

		public string Environment { get; set; }
		public string ServiceName { get; set; }
		public string Status { get; set; }
		public int? PID { get; set; }
		public string EnvironmentPath { get; set; }

		#endregion
	}

	#endregion

	#region Methods: Private

	private void DisplayHostsTable(List<HostInfo> hosts) {
		ConsoleTable table = new("Environment", "Service Name", "Status", "PID", "Environment Path");
		foreach (HostInfo host in hosts) {
			table.AddRow(
				host.Environment,
				host.ServiceName,
				host.Status,
				host.PID?.ToString() ?? "-",
				TruncatePath(host.EnvironmentPath, 50)
			);
		}

		logger.WriteLine("=== Creatio Hosts ===");
		//logger.PrintTable(table);
		logger.WriteInfo(table.ToMinimalString());
		logger.WriteInfo($"\nTotal: {hosts.Count} host(s)");
	}

	private List<EnvironmentSettings> GetAllEnvironmentsWithPath() {
		try {
			// Use reflection to access the internal _settings.Environments
			FieldInfo settingsField = settingsRepository.GetType()
														 .GetField("_settings",
															 BindingFlags.NonPublic | BindingFlags.Instance);

			if (settingsField == null) {
				return [];
			}

			object settings = settingsField.GetValue(settingsRepository);
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
			return [];
		}
	}

	private async Task<List<HostInfo>> GetAllHosts() {
		List<EnvironmentSettings> environments = GetAllEnvironmentsWithPath();

		if (environments.Count == 0) {
			logger.WriteInfo("No environments with paths found.");
			return [];
		}

		logger.WriteInfo($"Scanning {environments.Count} environment(s) in parallel...");

		// Create tasks for all environments to scan in parallel
		Task<HostInfo>[] scanTasks = environments.Select(ScanEnvironmentAsync).ToArray();

		// Wait for all scans to complete
		HostInfo[] hosts = await Task.WhenAll(scanTasks);

		logger.WriteInfo($"\nScan complete. Found {hosts.Length} host(s).\n");
		return hosts.ToList();
	}

	private async Task<HostInfo> ScanEnvironmentAsync(EnvironmentSettings env) {
		string envName = GetEnvironmentName(env);
		logger.WriteInfo($"Checking {envName}...");
		
		string serviceName = $"creatio-{GetServiceName(envName, env.EnvironmentPath)}";
		string status;
		int? pid = null;

		// On Windows, check for IIS sites
		if (OperationSystem.Current.IsWindows) {
			List<IISSiteInfo> iisSites = await iisSiteDetector.GetSitesByPath(env.EnvironmentPath);
			
			if (iisSites.Any()) {
				// Found IIS site(s) for this environment
				IISSiteInfo primarySite = iisSites.First();
				
				if (primarySite.IsRunning) {
					status = "Running (IIS)";
					// Get w3wp.exe PID for the site
					logger.WriteInfo($"  → {envName}: Found running IIS site: {primarySite.SiteName}, getting PID...");
					pid = await iisSiteDetector.GetSiteProcessId(primarySite.SiteName);
					if (pid.HasValue) {
						logger.WriteInfo($"  → {envName}: PID: {pid.Value}");
					} else {
						logger.WriteWarning($"  → {envName}: Could not determine PID for site {primarySite.SiteName}");
					}
				} else {
					status = "Stopped (IIS)";
					logger.WriteInfo($"  → {envName}: Found IIS site: {primarySite.SiteName} (Stopped)");
				}
				
				// Use actual IIS site name as service name
				serviceName = primarySite.SiteName;
			} else {
				// No IIS site found, check for background process
				logger.WriteInfo($"  → {envName}: No IIS site found, checking for background process...");
				(int pid, string processName)? processInfo = GetBackgroundProcess(env.EnvironmentPath);
				
				if (processInfo != null) {
					status = "Running (Process)";
					pid = processInfo.Value.pid;
					logger.WriteInfo($"  → {envName}: Found process: {processInfo.Value.processName} (PID: {pid})");
				} else {
					status = "Stopped";
					logger.WriteInfo($"  → {envName}: No running process found");
				}
			}
		} else {
			// On macOS/Linux, check for systemd/launchd service
			logger.WriteInfo($"  → {envName}: Checking service: {serviceName}...");
			bool serviceRunning = await serviceManager.IsServiceRunning(serviceName);
			
			if (serviceRunning) {
				status = "Running (Service)";
				logger.WriteInfo($"  → {envName}: Service is running");
			} else {
				// Check for background process
				logger.WriteInfo($"  → {envName}: Service not running, checking for background process...");
				(int pid, string processName)? processInfo = GetBackgroundProcess(env.EnvironmentPath);
				
				if (processInfo != null) {
					status = "Running (Process)";
					pid = processInfo.Value.pid;
					logger.WriteInfo($"  → {envName}: Found process: {processInfo.Value.processName} (PID: {pid})");
				} else {
					status = "Stopped";
					logger.WriteInfo($"  → {envName}: No running process found");
				}
			}
		}

		return new HostInfo {
			Environment = envName,
			ServiceName = serviceName,
			Status = status,
			PID = pid,
			EnvironmentPath = env.EnvironmentPath
		};
	}

	private (int pid, string processName)? GetBackgroundProcess(string targetPath) {
		try {
			if (string.IsNullOrWhiteSpace(targetPath)) {
				return null;
			}

			Process[] processes = Process.GetProcesses();

			foreach (Process process in processes) {
				try {
					string processName = process.ProcessName.ToLower();
					if (processName.Contains("dotnet") || processName.Contains("creatio") ||
						processName.Contains("terrasoft") || processName.Contains("webhost")) {
						ProcessModuleCollection modules = process.Modules;
						foreach (ProcessModule module in modules) {
							if (module.FileName.Contains(targetPath, StringComparison.OrdinalIgnoreCase)) {
								return (process.Id, process.ProcessName);
							}
						}
					}
				}
				catch {
					// Ignore access denied errors
				}
			}

			return null;
		}
		catch {
			return null;
		}
	}

	private string GetEnvironmentName(EnvironmentSettings env) {
		try {
			FieldInfo settingsField = settingsRepository.GetType()
														 .GetField("_settings",
															 BindingFlags.NonPublic | BindingFlags.Instance);

			if (settingsField != null) {
				object settings = settingsField.GetValue(settingsRepository);
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
			// Fallback
		}

		if (!string.IsNullOrWhiteSpace(env.EnvironmentPath)) {
			return Path.GetFileName(env.EnvironmentPath.TrimEnd(Path.DirectorySeparatorChar));
		}

		return "unknown";
	}

	private string GetServiceName(string envName, string envPath) {
		if (!string.IsNullOrWhiteSpace(envName) && envName != "unknown") {
			return envName;
		}

		if (!string.IsNullOrWhiteSpace(envPath)) {
			return Path.GetFileName(envPath.TrimEnd(Path.DirectorySeparatorChar));
		}

		return "default";
	}

	private string TruncatePath(string path, int maxLength) {
		if (string.IsNullOrEmpty(path) || path.Length <= maxLength) {
			return path;
		}

		// Show beginning and end of path
		int prefixLength = maxLength / 2 - 2;
		int suffixLength = maxLength / 2 - 2;

		return path.Substring(0, prefixLength) + "..." + path.Substring(path.Length - suffixLength);
	}

	#endregion

	#region Methods: Public

	public override int Execute(HostsOptions options) {
		try {
			List<HostInfo> hosts = GetAllHosts().Result;

			if (hosts.Count == 0) {
				logger.WriteInfo("No Creatio hosts found.");
				logger.WriteInfo("Use 'clio reg-web-app' to register environments with EnvironmentPath.");
				return 0;
			}

			DisplayHostsTable(hosts);
			return 0;
		}
		catch (Exception ex) {
			logger.WriteError($"Failed to list hosts: {ex.Message}");
			return 1;
		}
	}

	#endregion
}
