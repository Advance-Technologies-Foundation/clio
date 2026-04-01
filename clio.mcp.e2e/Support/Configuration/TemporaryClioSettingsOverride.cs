using Clio.Command.McpServer;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Clio.Mcp.E2E.Support.Configuration;

internal sealed class TemporaryClioSettingsOverride : IDisposable {
	private readonly string _appSettingsPath;
	private readonly string? _originalContent;
	private readonly bool _fileExisted;

	private TemporaryClioSettingsOverride(string appSettingsPath, string? originalContent, bool fileExisted) {
		_appSettingsPath = appSettingsPath;
		_originalContent = originalContent;
		_fileExisted = fileExisted;
	}

	public string AppSettingsPath => _appSettingsPath;

	public static TemporaryClioSettingsOverride SetWorkspacesRoot(
		string workspacesRoot,
		string? clioProcessPath = null,
		IReadOnlyDictionary<string, string?>? processEnvironmentVariables = null) {
		string appSettingsPath = GetClioAppSettingsPath(clioProcessPath, processEnvironmentVariables);
		string originalContent = File.ReadAllText(appSettingsPath);
		JObject settings = JObject.Parse(originalContent);
		settings["workspaces-root"] = workspacesRoot;
		File.WriteAllText(appSettingsPath, settings.ToString(Newtonsoft.Json.Formatting.Indented));
		return new TemporaryClioSettingsOverride(appSettingsPath, originalContent, true);
	}

	public static TemporaryClioSettingsOverride ReplaceContent(
		string newContent,
		string? clioProcessPath = null,
		IReadOnlyDictionary<string, string?>? processEnvironmentVariables = null) {
		string appSettingsPath = GetClioAppSettingsPath(clioProcessPath, processEnvironmentVariables);
		bool fileExisted = File.Exists(appSettingsPath);
		string? originalContent = fileExisted ? File.ReadAllText(appSettingsPath) : null;
		Directory.CreateDirectory(Path.GetDirectoryName(appSettingsPath)!);
		File.WriteAllText(appSettingsPath, newContent);
		return new TemporaryClioSettingsOverride(appSettingsPath, originalContent, fileExisted);
	}

	public static TemporaryClioSettingsOverride SetWrongActiveEnvironmentKey(
		string? clioProcessPath = null,
		IReadOnlyDictionary<string, string?>? processEnvironmentVariables = null) {
		return ReplaceContent("""
			{
			  "ActiveEnvironmentKey": "wrong-dev",
			  "Environments": {
			    "dev": {
			      "Uri": "http://localhost",
			      "Login": "Supervisor",
			      "Password": "Supervisor"
			    }
			  }
			}
			""",
			clioProcessPath,
			processEnvironmentVariables);
	}

	private static string GetClioAppSettingsPath(
		string? clioProcessPath = null,
		IReadOnlyDictionary<string, string?>? processEnvironmentVariables = null) {
		if (!string.IsNullOrWhiteSpace(clioProcessPath)) {
			return ResolveSettingsPathFromClioProcess(clioProcessPath, processEnvironmentVariables);
		}
		Assembly clioAssembly = typeof(McpServerCommand).Assembly;
		string companyName = clioAssembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company
			?? throw new InvalidOperationException("Unable to resolve the clio assembly company name.");
		string productName = clioAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
			?? throw new InvalidOperationException("Unable to resolve the clio assembly product name.");
		string userPath = Environment.GetEnvironmentVariable(
			RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "LOCALAPPDATA" : "HOME")
			?? throw new InvalidOperationException("Unable to resolve the user settings root for the current platform.");
		return Path.Combine(userPath, companyName, productName, "appsettings.json");
	}

	private static string ResolveSettingsPathFromClioProcess(
		string clioProcessPath,
		IReadOnlyDictionary<string, string?>? processEnvironmentVariables) {
		ProcessStartInfo startInfo;
		string fullPath = Path.GetFullPath(clioProcessPath);
		if (fullPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) {
			startInfo = new ProcessStartInfo("dotnet") {
				ArgumentList = { fullPath, "info", "--settings-file" }
			};
		}
		else {
			startInfo = new ProcessStartInfo(fullPath) {
				ArgumentList = { "info", "--settings-file" }
			};
		}
		startInfo.RedirectStandardOutput = true;
		startInfo.RedirectStandardError = true;
		startInfo.UseShellExecute = false;
		foreach (KeyValuePair<string, string?> pair in processEnvironmentVariables ?? new Dictionary<string, string?>()) {
			if (pair.Value is null) {
				startInfo.Environment.Remove(pair.Key);
			}
			else {
				startInfo.Environment[pair.Key] = pair.Value;
			}
		}
		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("Unable to start clio to resolve the settings file path.");
		string standardOutput = process.StandardOutput.ReadToEnd();
		string standardError = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0) {
			throw new InvalidOperationException(
				$"Unable to resolve the settings file path from clio. Stdout: {standardOutput} Stderr: {standardError}");
		}
		string pathLine = standardOutput
			.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Trim())
			.LastOrDefault(line => line.StartsWith("/", StringComparison.Ordinal) || line.Contains("appsettings.json", StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidOperationException($"Unable to extract the settings file path from clio output: {standardOutput}");
		int separatorIndex = pathLine.IndexOf("- ", StringComparison.Ordinal);
		return separatorIndex >= 0
			? pathLine[(separatorIndex + 2)..].Trim()
			: pathLine;
	}

	public void Dispose() {
		if (_fileExisted) {
			File.WriteAllText(_appSettingsPath, _originalContent!);
			return;
		}
		if (File.Exists(_appSettingsPath)) {
			File.Delete(_appSettingsPath);
		}
	}
}
