using System.Reflection;
using Clio;
using FluentAssertions;
using Newtonsoft.Json;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class RegisteredClioEnvironmentSettingsResolver {
	public static EnvironmentSettings Resolve(string environmentName) {
		string settingsFilePath = GetClioSettingsFilePath();
		File.Exists(settingsFilePath).Should().BeTrue(
			because: "registered clio environment credentials must come from the real clio appsettings file for MCP end-to-end tests");

		string settingsJson = File.ReadAllText(settingsFilePath);
		Settings settings = JsonConvert.DeserializeObject<Settings>(settingsJson)
			?? throw new InvalidOperationException($"Failed to deserialize clio settings from '{settingsFilePath}'.");

		settings.Environments.Should().ContainKey(environmentName,
			because: "the configured sandbox environment must exist in the registered clio settings");

		EnvironmentSettings environment = settings.Environments[environmentName];
		environment.Uri.Should().NotBeNullOrWhiteSpace(
			because: "credentials-based MCP tests need the registered environment URL");
		environment.Login.Should().NotBeNullOrWhiteSpace(
			because: "credentials-based MCP tests need the registered environment login");
		environment.Password.Should().NotBeNullOrWhiteSpace(
			because: "credentials-based MCP tests need the registered environment password");

		return environment;
	}

	private static string GetClioSettingsFilePath() {
		Assembly assembly = typeof(Program).Assembly;
		string? company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
		string? product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
		string baseDirectory = Environment.GetEnvironmentVariable(
			OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME")
			?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		baseDirectory.Should().NotBeNullOrWhiteSpace(
			because: "the current machine must expose a base directory for the registered clio settings");
		company.Should().NotBeNullOrWhiteSpace(
			because: "the clio assembly must expose an AssemblyCompanyAttribute for settings-file discovery");
		product.Should().NotBeNullOrWhiteSpace(
			because: "the clio assembly must expose an AssemblyProductAttribute for settings-file discovery");

		return Path.Combine(baseDirectory, company!, product!, "appsettings.json");
	}
}
