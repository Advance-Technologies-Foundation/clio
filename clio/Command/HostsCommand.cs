using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.SystemServices;
using Clio.UserEnvironment;
using CommandLine;
using ConsoleTables;

namespace Clio.Command;

[Verb("hosts", Aliases = ["list-hosts"], HelpText = "List all Creatio hosts and their status")]
public class HostsOptions : BaseCommandOptions{ }

public class HostsCommand(ISettingsRepository settingsRepository, ISystemServiceManager serviceManager, ILogger logger)
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
		logger.PrintTable(table);
		//logger.WriteInfo(table.ToMinimalString());
		//logger.WriteInfo($"\nTotal: {hosts.Count} host(s)");
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
		List<HostInfo> hosts = new();
		List<EnvironmentSettings> environments = GetAllEnvironmentsWithPath();

		foreach (EnvironmentSettings env in environments) {
			string envName = GetEnvironmentName(env);
			string serviceName = $"creatio-{GetServiceName(envName, env.EnvironmentPath)}";

			// Check for OS service
			bool serviceRunning = await serviceManager.IsServiceRunning(serviceName);
			string serviceStatus = serviceRunning ? "Service Running" : "Service Stopped";

			// Check for background process
			(int pid, string processName)? processInfo = GetBackgroundProcess(env.EnvironmentPath);

			string status;
			int? pid = null;

			if (serviceRunning) {
				status = "Running (Service)";
			}
			else if (processInfo != null) {
				status = "Running (Process)";
				pid = processInfo.Value.pid;
			}
			else {
				status = "Stopped";
			}

			hosts.Add(new HostInfo {
				Environment = envName,
				ServiceName = serviceName,
				Status = status,
				PID = pid,
				EnvironmentPath = env.EnvironmentPath
			});
		}

		return hosts;
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
