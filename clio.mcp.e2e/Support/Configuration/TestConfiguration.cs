using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class TestConfiguration {
	public static McpE2ESettings Load() {
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.SetBasePath(TestContext.CurrentContext.TestDirectory)
			.AddJsonFile("appsettings.json", optional: true)
			.AddEnvironmentVariables()
			.Build();

		McpE2ESettings settings = new();
		configuration.GetSection("McpE2E").Bind(settings);
		return settings;
	}

	public static void EnsureSandboxIsConfigured(McpE2ESettings settings) {
		settings.Sandbox.EnvironmentName.Should().NotBeNullOrWhiteSpace(
			because: "destructive MCP tests need an explicit sandbox environment name");
		settings.Sandbox.SeedKeyPrefix.Should().NotBeNullOrWhiteSpace(
			because: "seeded Redis keys must use a predictable prefix for cleanup and diagnostics");
	}

	public static string ResolveFreshClioProcessPath() {
		return Path.GetFullPath(Path.Combine(
			TestContext.CurrentContext.TestDirectory,
			"..",
			"..",
			"..",
			"..",
			"clio",
			"bin",
			"Debug",
			"net8.0",
			"clio.dll"));
	}
}
