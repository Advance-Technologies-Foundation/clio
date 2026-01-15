using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Clio.Common.IIS;

/// <summary>
///     Windows implementation of IIS site detector using appcmd.exe.
/// </summary>
public class WindowsIISSiteDetector : IIISSiteDetector{
	#region Constants: Private

	private const string AppCmdPath = @"C:\Windows\System32\inetsrv\appcmd.exe";

	#endregion

	private static bool _debugMode;

	#region Constructors: Public

	public WindowsIISSiteDetector() {
		// Enable debug mode if environment variable is set
		string debugEnv = Environment.GetEnvironmentVariable("CLIO_DEBUG_IIS");
		_debugMode = !string.IsNullOrWhiteSpace(debugEnv) &&
					 debugEnv.Equals("true", StringComparison.OrdinalIgnoreCase);
	}

	#endregion

	#region Methods: Private

	private static void DebugLog(string message) {
		if (_debugMode) {
			Console.WriteLine($"[DEBUG IIS] {message}");
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

			using Process process = new() {
				StartInfo = new ProcessStartInfo {
					FileName = AppCmdPath,
					Arguments = arguments,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				}
			};

			process.Start();
			string output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();

			return output;
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
			using Process process = new() {
				StartInfo = new ProcessStartInfo {
					FileName = "wmic",
					Arguments = $"process where ProcessId={processId} get CommandLine /format:list",
					RedirectStandardOutput = true,
					UseShellExecute = false,
					CreateNoWindow = true
				}
			};

			process.Start();
			string output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();

			// Parse output (format: CommandLine=<value>)
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

			// Get app pool name for the site
			string appXml = ExecuteAppCmd($"list app \"{siteName}/\" /xml");
			if (string.IsNullOrWhiteSpace(appXml)) {
				DebugLog("[PID] No app found with trailing slash, trying without...");

				// Try without trailing slash for nested apps
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

			// Method 1: Try using appcmd to list worker processes
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

			// Method 2: Use PowerShell to get w3wp process by app pool name
			DebugLog("[PID] Method 2: Trying PowerShell WMI...");
			try {
				string psCommand
					= $@"Get-WmiObject Win32_Process -Filter ""Name='w3wp.exe'"" | Where-Object {{$_.CommandLine -like '*{appPoolName}*'}} | Select-Object -First 1 -ExpandProperty ProcessId";
				DebugLog($"[PID] Method 2 PS command: {psCommand}");

				using Process process = new() {
					StartInfo = new ProcessStartInfo {
						FileName = "powershell.exe",
						Arguments = $"-NoProfile -NonInteractive -Command \"{psCommand}\"",
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						UseShellExecute = false,
						CreateNoWindow = true
					}
				};

				process.Start();
				string output = process.StandardOutput.ReadToEnd();
				string errorOutput = process.StandardError.ReadToEnd();
				process.WaitForExit();

				DebugLog($"[PID] Method 2 output: '{output.Trim()}'");
				if (!string.IsNullOrWhiteSpace(errorOutput)) {
					DebugLog($"[PID] Method 2 error: {errorOutput}");
				}

				if (!string.IsNullOrWhiteSpace(output) && int.TryParse(output.Trim(), out int pid)) {
					DebugLog($"[PID] Method 2 SUCCESS: PID = {pid}");
					return pid;
				}
			}
			catch (Exception ex) {
				DebugLog($"[PID] Method 2 failed: {ex.Message}");
			}

			// Method 3: Manual process enumeration with command line check
			DebugLog("[PID] Method 3: Trying manual enumeration...");
			try {
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

			// Get all sites
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

				// Get physical path for the site
				string physicalPath = ExecuteAppCmd($"list vdir \"{siteName}/\" /text:physicalPath").Trim();

				// Check if this site matches the environment path
				string normalizedPhysicalPath = NormalizePath(physicalPath);

				// Match if paths are equal, or if physical path is within environment path
				// (for cases like environmentPath = "C:\App" and physicalPath = "C:\App\Terrasoft.WebApp")
				bool isMatch = normalizedPhysicalPath == normalizedEnvPath ||
							   normalizedPhysicalPath.StartsWith(normalizedEnvPath + Path.DirectorySeparatorChar) ||
							   normalizedEnvPath.StartsWith(normalizedPhysicalPath + Path.DirectorySeparatorChar);

				if (!isMatch) {
					continue;
				}

				// Get app pool name for this site
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

				// Get app pool state
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

			// Also check for nested applications
			string appsXml = ExecuteAppCmd("list app /xml");
			if (!string.IsNullOrWhiteSpace(appsXml)) {
				XElement appsRoot = XElement.Parse(appsXml);
				foreach (XElement appElement in appsRoot.Elements("APP")) {
					string appName = appElement.Attribute("APP.NAME")?.Value ?? string.Empty;

					// Skip root applications (already handled above)
					if (string.IsNullOrEmpty(appName) || !appName.Contains("/") || appName.EndsWith("/")) {
						continue;
					}

					// Get physical path for this application
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

					// Skip if we already have this site
					if (sites.Any(s => s.SiteName == appName)) {
						continue;
					}

					// Get site state
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

					// Get app pool info
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

			// Check site state
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

			// Get app pool name
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

			// Check app pool state
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

			// Both site and app pool must be started
			return siteState == "Started" && appPoolState == "Started";
		});
	}

	#endregion
}
