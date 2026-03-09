using Clio.Command.McpServer;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Clio.Mcp.E2E.Support.Configuration;

internal sealed class TemporaryClioSettingsOverride : IDisposable {
	private readonly string _appSettingsPath;
	private readonly string _originalContent;

	private TemporaryClioSettingsOverride(string appSettingsPath, string originalContent) {
		_appSettingsPath = appSettingsPath;
		_originalContent = originalContent;
	}

	public static TemporaryClioSettingsOverride SetWorkspacesRoot(string workspacesRoot) {
		string appSettingsPath = GetClioAppSettingsPath();
		string originalContent = File.ReadAllText(appSettingsPath);
		JObject settings = JObject.Parse(originalContent);
		settings["workspaces-root"] = workspacesRoot;
		File.WriteAllText(appSettingsPath, settings.ToString(Newtonsoft.Json.Formatting.Indented));
		return new TemporaryClioSettingsOverride(appSettingsPath, originalContent);
	}

	private static string GetClioAppSettingsPath() {
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

	public void Dispose() {
		File.WriteAllText(_appSettingsPath, _originalContent);
	}
}
