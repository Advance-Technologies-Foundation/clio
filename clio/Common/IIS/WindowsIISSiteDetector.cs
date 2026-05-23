using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Web.Administration;

namespace Clio.Common.IIS;

/// <summary>
///     Windows implementation of IIS site detector using appcmd.exe.
/// </summary>
public class WindowsIISSiteDetector : IIISSiteDetector{
	#region Constants: Private

	private const string AppCmdPath = @"C:\Windows\System32\inetsrv\appcmd.exe";

	#endregion

	private static bool _debugMode;
	private readonly IProcessExecutor _processExecutor;

	#region Constructors: Public

	public WindowsIISSiteDetector(IProcessExecutor processExecutor) {
		_processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
		string debugEnv = Environment.GetEnvironmentVariable("CLIO_DEBUG_IIS");
		_debugMode = !string.IsNullOrWhiteSpace(debugEnv) &&
					 debugEnv.Equals("true", StringComparison.OrdinalIgnoreCase);
	}

	#endregion

	#region Methods: Private

	private static void DebugLog(string message) {
		if (_debugMode) {
			ConsoleLogger.Instance.WriteDebug($"[DEBUG IIS] {message}");
		}
		Debug.WriteLine(message);
	}

	/// <summary>
	///     Executes appcmd.exe with the specified arguments and returns the output.
	/// </summary>
	private string ExecuteAppCmd(string arguments) {
		try {
			if (!File.Exists(AppCmdPath)) {
				return string.Empty;
			}
			return _processExecutor.Execute(AppCmdPath, arguments, waitForExit: true);
		}
		catch {
			return string.Empty;
		}
	}

	/// <summary>
	///     Gets the command line for a process using WMI.
	/// </summary>
	private string GetProcessCommandLine(int processId) {
		try {
			string output = _processExecutor.Execute("wmic",
				$"process where ProcessId={processId} get CommandLine /format:list", waitForExit: true);
			foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
				if (line.StartsWith("CommandLine=", StringComparison.OrdinalIgnoreCase)) {
					return line.Substring("CommandLine=".Length).Trim();
				}
			}
			return string.Empty;
		}
		catch {
			return string.Empty;
		}
	}

	/// <summary>
	///     Normalizes a path for comparison by removing trailing slashes and converting to lowercase.
	/// </summary>
	private string NormalizePath(string path) {
		if (string.IsNullOrWhiteSpace(path)) {
			return string.Empty;
		}

		return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
	}

	#endregion

	#region Methods: Public

	public async Task<int?> GetSiteProcessId(string siteName) {
		return await Task.Run<int?>(() => {
			if (string.IsNullOrWhiteSpace(siteName)) {
				DebugLog("[PID] Site name is null or empty");
				return null;
			}

			DebugLog($"[PID] Looking for PID for site: {siteName}");

			string appXml = ExecuteAppCmd($"list app \"{siteName}/\" /xml");
			if (string.IsNullOrWhiteSpace(appXml)) {
				DebugLog("[PID] No app found with trailing slash, trying without...");
				appXml = ExecuteAppCmd($"list app \"{siteName}\" /xml");
				if (string.IsNullOrWhiteSpace(appXml)) {
					DebugLog($"[PID] No app found for site: {siteName}");
					return null;
				}
			}

			string appPoolName = string.Empty;
			try {
				XElement appRoot = XElement.Parse(appXml);
				XElement appElement = appRoot.Element("APP");
				if (appElement != null) {
					appPoolName = appElement.Attribute("APPPOOL.NAME")?.Value;
					DebugLog($"[PID] Found app pool: {appPoolName}");
				}
			}
			catch (Exception ex) {
				DebugLog($"[PID] Failed to parse app XML: {ex.Message}");
				return null;
			}

			if (string.IsNullOrWhiteSpace(appPoolName)) {
				DebugLog("[PID] App pool name is empty");
				return null;
			}

			DebugLog("[PID] Method 1: Trying appcmd list wp...");
			try {
				string wpXml = ExecuteAppCmd($"list wp /apppool.name:\"{appPoolName}\" /xml");
				DebugLog($"[PID] Method 1 output: {wpXml?.Substring(0, Math.Min(200, wpXml?.Length ?? 0))}");

				if (!string.IsNullOrWhiteSpace(wpXml)) {
					XElement wpRoot = XElement.Parse(wpXml);
					XElement wpElement = wpRoot.Element("WP");
					if (wpElement != null) {
						string pidStr = wpElement.Attribute("WP.NAME")?.Value;
						DebugLog($"[PID] Method 1: Found WP.NAME = {pidStr}");
						if (!string.IsNullOrWhiteSpace(pidStr) && int.TryParse(pidStr, out int pid)) {
							DebugLog($"[PID] Method 1 SUCCESS: PID = {pid}");
							return pid;
						}
					}
					else {
						DebugLog("[PID] Method 1: No WP element found in XML");
					}
				}
				else {
					DebugLog("[PID] Method 1: Empty response from appcmd");
				}
			}
			catch (Exception ex) {
				DebugLog($"[PID] Method 1 failed: {ex.Message}");
			}

			DebugLog("[PID] Method 2: Trying PowerShell WMI...");
			try {
				string psCommand
					= $@"Get-WmiObject Win32_Process -Filter ""Name='w3wp.exe'"" | Where-Object {{$_.CommandLine -like '*{appPoolName}*'}} | Select-Object -First 1 -ExpandProperty ProcessId";
				DebugLog($"[PID] Method 2 PS command: {psCommand}");

				string output = _processExecutor.Execute("powershell.exe",
					$"-NoProfile -NonInteractive -Command \"{psCommand}\"", waitForExit: true);

				DebugLog($"[PID] Method 2 output: '{output.Trim()}'");

				if (!string.IsNullOrWhiteSpace(output) && int.TryParse(output.Trim(), out int pid)) {
					DebugLog($"[PID] Method 2 SUCCESS: PID = {pid}");
					return pid;
				}
			}
			catch (Exception ex) {
				DebugLog($"[PID] Method 2 failed: {ex.Message}");
			}

			DebugLog("[PID] Method 3: Trying manual enumeration...");
			try {
#pragma warning disable CLIO004
				Process[] processes = Process.GetProcessesByName("w3wp");
				DebugLog($"[PID] Method 3: Found {processes.Length} w3wp processes");

				foreach (Process proc in processes) {
					try {
						DebugLog($"[PID] Method 3: Checking process {proc.Id}...");
						string commandLine = GetProcessCommandLine(proc.Id);
						DebugLog(
							$"[PID] Method 3: Process {proc.Id} command line: {commandLine?.Substring(0, Math.Min(150, commandLine?.Length ?? 0))}");

						if (!string.IsNullOrWhiteSpace(commandLine) &&
							commandLine.Contains(appPoolName, StringComparison.OrdinalIgnoreCase)) {
							DebugLog($"[PID] Method 3 SUCCESS: PID = {proc.Id}");
							return proc.Id;
						}
					}
					catch (Exception ex) {
						DebugLog($"[PID] Method 3: Failed to check process {proc.Id}: {ex.Message}");
					}
					finally {
						proc.Dispose();
					}
				}
#pragma warning restore CLIO004
			}
			catch (Exception ex) {
				DebugLog($"[PID] Method 3 failed: {ex.Message}");
			}

			DebugLog($"[PID] All methods failed for site {siteName}, app pool {appPoolName}");
			return null;
		});
	}

	public async Task<List<IISSiteInfo>> GetSitesByPath(string environmentPath) {
		return await Task.Run(() => {
			List<IISSiteInfo> sites = new();

			if (string.IsNullOrWhiteSpace(environmentPath)) {
				return sites;
			}

			string normalizedEnvPath = NormalizePath(environmentPath);

			string sitesXml = ExecuteAppCmd("list sites /xml");
			if (string.IsNullOrWhiteSpace(sitesXml)) {
				return sites;
			}

			XElement sitesRoot = XElement.Parse(sitesXml);
			foreach (XElement siteElement in sitesRoot.Elements("SITE")) {
				string siteName = siteElement.Attribute("SITE.NAME")?.Value;
				string state = siteElement.Attribute("state")?.Value;

				if (string.IsNullOrWhiteSpace(siteName)) {
					continue;
				}

				string physicalPath = ExecuteAppCmd($"list vdir \"{siteName}/\" /text:physicalPath").Trim();

				string normalizedPhysicalPath = NormalizePath(physicalPath);

				bool isMatch = normalizedPhysicalPath == normalizedEnvPath ||
							   normalizedPhysicalPath.StartsWith(normalizedEnvPath + Path.DirectorySeparatorChar) ||
							   normalizedEnvPath.StartsWith(normalizedPhysicalPath + Path.DirectorySeparatorChar);

				if (!isMatch) {
					continue;
				}

				string appPoolXml = ExecuteAppCmd($"list app \"{siteName}/\" /xml");
				string appPoolName = string.Empty;
				string appPoolState = string.Empty;

				if (!string.IsNullOrWhiteSpace(appPoolXml)) {
					try {
						XElement appRoot = XElement.Parse(appPoolXml);
						XElement appElement = appRoot.Element("APP");
						if (appElement != null) {
							appPoolName = appElement.Attribute("APPPOOL.NAME")?.Value;
						}
					}
					catch {
						// Continue without app pool info
					}
				}

				if (!string.IsNullOrWhiteSpace(appPoolName)) {
					string appPoolInfoXml = ExecuteAppCmd($"list apppool \"{appPoolName}\" /xml");
					if (!string.IsNullOrWhiteSpace(appPoolInfoXml)) {
						try {
							XElement appPoolRoot = XElement.Parse(appPoolInfoXml);
							XElement appPoolElement = appPoolRoot.Element("APPPOOL");
							if (appPoolElement != null) {
								appPoolState = appPoolElement.Attribute("state")?.Value;
							}
						}
						catch {
							// Continue without app pool state
						}
					}
				}

				sites.Add(new IISSiteInfo {
					SiteName = siteName,
					PhysicalPath = physicalPath,
					State = state ?? "Unknown",
					AppPoolName = appPoolName ?? "Unknown",
					AppPoolState = appPoolState ?? "Unknown"
				});
			}

			string appsXml = ExecuteAppCmd("list app /xml");
			if (!string.IsNullOrWhiteSpace(appsXml)) {
				XElement appsRoot = XElement.Parse(appsXml);
				foreach (XElement appElement in appsRoot.Elements("APP")) {
					string appName = appElement.Attribute("APP.NAME")?.Value ?? string.Empty;

					if (string.IsNullOrEmpty(appName) || !appName.Contains("/") || appName.EndsWith("/")) {
						continue;
					}

					string[] parts = appName.Split('/', 2);
					string siteName = parts.Length > 0 ? parts[0] : string.Empty;
					string appPath = parts.Length > 1 ? "/" + parts[1] : string.Empty;

					string vdirPath = string.IsNullOrEmpty(appPath) ? $"{siteName}/" : $"{siteName}{appPath}/";
					string physicalPath = ExecuteAppCmd($"list vdir \"{vdirPath}\" /text:physicalPath").Trim();

					if (string.IsNullOrWhiteSpace(physicalPath)) {
						continue;
					}

					string normalizedPhysicalPath = NormalizePath(physicalPath);
					bool isMatch = normalizedPhysicalPath == normalizedEnvPath ||
								   normalizedPhysicalPath.StartsWith(normalizedEnvPath + Path.DirectorySeparatorChar) ||
								   normalizedEnvPath.StartsWith(normalizedPhysicalPath + Path.DirectorySeparatorChar);

					if (!isMatch) {
						continue;
					}

					if (sites.Any(s => s.SiteName == appName)) {
						continue;
					}

					string siteState = "Unknown";
					string siteXml = ExecuteAppCmd($"list site \"{siteName}\" /xml");
					if (!string.IsNullOrWhiteSpace(siteXml)) {
						try {
							XElement siteRoot = XElement.Parse(siteXml);
							XElement siteElement = siteRoot.Element("SITE");
							if (siteElement != null) {
								siteState = siteElement.Attribute("state")?.Value ?? "Unknown";
							}
						}
						catch {
							// Continue
						}
					}

					string appPoolName = appElement.Attribute("APPPOOL.NAME")?.Value ?? "Unknown";
					string appPoolState = "Unknown";

					if (appPoolName != "Unknown") {
						string appPoolInfoXml = ExecuteAppCmd($"list apppool \"{appPoolName}\" /xml");
						if (!string.IsNullOrWhiteSpace(appPoolInfoXml)) {
							try {
								XElement appPoolRoot = XElement.Parse(appPoolInfoXml);
								XElement appPoolElement = appPoolRoot.Element("APPPOOL");
								if (appPoolElement != null) {
									appPoolState = appPoolElement.Attribute("state")?.Value ?? "Unknown";
								}
							}
							catch {
								// Continue
							}
						}
					}

					sites.Add(new IISSiteInfo {
						SiteName = appName,
						PhysicalPath = physicalPath,
						State = siteState,
						AppPoolName = appPoolName,
						AppPoolState = appPoolState
					});
				}
			}

			return sites;
		});
	}

	public async Task<bool> IsSiteRunning(string siteName) {
		return await Task.Run(() => {
			if (string.IsNullOrWhiteSpace(siteName)) {
				return false;
			}

			string siteXml = ExecuteAppCmd($"list site \"{siteName}\" /xml");
			if (string.IsNullOrWhiteSpace(siteXml)) {
				return false;
			}

			string siteState = "Unknown";
			string appPoolName = string.Empty;

			try {
				XElement siteRoot = XElement.Parse(siteXml);
				XElement siteElement = siteRoot.Element("SITE");
				if (siteElement != null) {
					siteState = siteElement.Attribute("state")?.Value ?? "Unknown";
				}
			}
			catch {
				return false;
			}

			string appXml = ExecuteAppCmd($"list app \"{siteName}/\" /xml");
			if (!string.IsNullOrWhiteSpace(appXml)) {
				try {
					XElement appRoot = XElement.Parse(appXml);
					XElement appElement = appRoot.Element("APP");
					if (appElement != null) {
						appPoolName = appElement.Attribute("APPPOOL.NAME")?.Value;
					}
				}
				catch {
					// Continue without app pool
				}
			}

			string appPoolState = "Unknown";
			if (!string.IsNullOrWhiteSpace(appPoolName)) {
				string appPoolXml = ExecuteAppCmd($"list apppool \"{appPoolName}\" /xml");
				if (!string.IsNullOrWhiteSpace(appPoolXml)) {
					try {
						XElement appPoolRoot = XElement.Parse(appPoolXml);
						XElement appPoolElement = appPoolRoot.Element("APPPOOL");
						if (appPoolElement != null) {
							appPoolState = appPoolElement.Attribute("state")?.Value ?? "Unknown";
						}
					}
					catch {
						// Continue
					}
				}
			}

			return siteState == "Started" && appPoolState == "Started";
		});
	}

	/// <inheritdoc />
	public async Task<IReadOnlyCollection<int>> GetBoundPorts(int rangeStart, int rangeEnd)
	{
		return await Task.Run<IReadOnlyCollection<int>>(() =>
		{
			using ServerManager serverManager = new();
			return serverManager.Sites
				.SelectMany(site => site.Bindings)
				.Select(TryGetBindingPort)
				.Where(port => port.HasValue)
				.Select(port => port!.Value)
				.Where(port => port >= rangeStart && port <= rangeEnd)
				.Distinct()
				.OrderBy(port => port)
				.ToArray();
		});
	}

	#endregion

	private static int? TryGetBindingPort(Binding binding)
	{
		if (binding.EndPoint is not null)
		{
			return binding.EndPoint.Port;
		}

		string[] parts = (binding.BindingInformation ?? string.Empty).Split(':');
		if (parts.Length >= 2 && int.TryParse(parts[1], out int port))
		{
			return port;
		}

		return null;
	}
}
