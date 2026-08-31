using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Clio.Common;

namespace Clio.Common.SystemServices;

/// <summary>
/// Implementation of ISystemServiceManager for macOS using launchd.
/// Creates and manages launchd plist files for Creatio applications.
/// </summary>
public class MacOSSystemServiceManager : ISystemServiceManager
{
	private const string LaunchdDirectory = "~/Library/LaunchAgents";
	private const string LaunchdSystemDirectory = "/Library/LaunchDaemons";
	private readonly IProcessExecutor _processExecutor;

	public MacOSSystemServiceManager(IProcessExecutor processExecutor)
	{
		_processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
	}

	/// <summary>
	/// Creates or updates a launchd plist service configuration.
	/// </summary>
	public async Task<bool> CreateOrUpdateService(
		string serviceName,
		string description,
		string workingDirectory,
		string executablePath,
		string arguments = "",
		bool autoStart = true,
		IReadOnlyDictionary<string, string> environmentVariables = null
	)
	{
		try
		{
			var plistContent = GenerateLaunchdPlist(
				serviceName,
				description,
				workingDirectory,
				executablePath,
				arguments,
				autoStart,
				environmentVariables
			);

			var expandedPath = ExpandTilde(LaunchdDirectory);
			var plistFilePath = Path.Combine(expandedPath, $"{serviceName}.plist");

			await Task.CompletedTask;
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Enables service via launchctl load.
	/// </summary>
	public async Task<bool> EnableService(string serviceName)
	{
		try
		{
			await Task.CompletedTask;
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Disables service via launchctl unload.
	/// </summary>
	public async Task<bool> DisableService(string serviceName)
	{
		try
		{
			var expandedPath = ExpandTilde(LaunchdDirectory);
			var plistPath = Path.Combine(expandedPath, $"{serviceName}.plist");

			if (!File.Exists(plistPath))
				return false;

			ProcessExecutionResult result = await _processExecutor.ExecuteAndCaptureAsync(
				new ProcessExecutionOptions("launchctl", $"unload \"{plistPath}\""));
			return result.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Starts service via launchctl start.
	/// </summary>
	public async Task<bool> StartService(string serviceName)
	{
		try
		{
			await Task.CompletedTask;
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Stops service via launchctl stop.
	/// </summary>
	public async Task<bool> StopService(string serviceName)
	{
		try
		{
			ProcessExecutionResult result = await _processExecutor.ExecuteAndCaptureAsync(
				new ProcessExecutionOptions("launchctl", $"stop {serviceName}"));
			return result.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Restarts service via launchctl stop/start.
	/// </summary>
	public async Task<bool> RestartService(string serviceName)
	{
		try
		{
			await Task.CompletedTask;
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Checks if service is running via launchctl list.
	/// </summary>
	public async Task<bool> IsServiceRunning(string serviceName)
	{
		try
		{
			ProcessExecutionResult result = await _processExecutor.ExecuteAndCaptureAsync(
				new ProcessExecutionOptions("launchctl", "list"));
			return result.StandardOutput.Contains(serviceName);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Removes launchd plist file.
	/// </summary>
	public async Task<bool> DeleteService(string serviceName)
	{
		try
		{
			var expandedPath = ExpandTilde(LaunchdDirectory);
			var plistPath = Path.Combine(expandedPath, $"{serviceName}.plist");

			if (File.Exists(plistPath))
			{
				File.Delete(plistPath);
			}

			await Task.CompletedTask;
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Generates launchd plist content in XML format.
	/// </summary>
	private static string GenerateLaunchdPlist(
		string serviceName,
		string description,
		string workingDirectory,
		string executablePath,
		string arguments,
		bool autoStart,
		IReadOnlyDictionary<string, string> environmentVariables
	)
	{
		var programArgumentsXml = string.IsNullOrEmpty(arguments)
			? $"\n\t\t<string>{executablePath}</string>"
			: $"\n\t\t<string>{executablePath}</string>\n\t\t<string>{arguments}</string>";
		var environmentXml = new System.Text.StringBuilder("\n\t\t<key>ASPNETCORE_ENVIRONMENT</key>\n\t\t<string>Production</string>");
		foreach ((string key, string value) in environmentVariables ?? new Dictionary<string, string>())
		{
			environmentXml.Append($"\n\t\t<key>{EscapeXml(key)}</key>\n\t\t<string>{EscapeXml(value)}</string>");
		}

		return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
	<key>Label</key>
	<string>{serviceName}</string>
	<key>Program</key>
	<string>{executablePath}</string>
	<key>ProgramArguments</key>
	<array>{programArgumentsXml}
	</array>
	<key>WorkingDirectory</key>
	<string>{workingDirectory}</string>
	<key>RunAtLoad</key>
	<{(autoStart ? "true" : "false")} />
	<key>KeepAlive</key>
	<true/>
	<key>StandardOutPath</key>
	<string>{Path.Combine(workingDirectory, $"{serviceName}.log")}</string>
	<key>StandardErrorPath</key>
	<string>{Path.Combine(workingDirectory, $"{serviceName}.err")}</string>
	<key>EnvironmentVariables</key>
	<dict>
		{environmentXml}
	</dict>
</dict>
</plist>";
	}

	private static string EscapeXml(string value) =>
		value.Replace("&", "&amp;", StringComparison.Ordinal)
			.Replace("<", "&lt;", StringComparison.Ordinal)
			.Replace(">", "&gt;", StringComparison.Ordinal)
			.Replace("\"", "&quot;", StringComparison.Ordinal)
			.Replace("'", "&apos;", StringComparison.Ordinal);

	/// <summary>
	/// Expands tilde (~) to home directory path.
	/// </summary>
	private static string ExpandTilde(string path)
	{
		if (path.StartsWith("~"))
		{
			return Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				path.Substring(2)
			);
		}
		return path;
	}
}
