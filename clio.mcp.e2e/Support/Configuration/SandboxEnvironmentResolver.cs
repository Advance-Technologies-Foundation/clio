using System.Xml.Linq;
using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class SandboxEnvironmentResolver {
	public static SandboxEnvironmentContext Resolve(McpE2ESettings settings) {
		string environmentName = settings.Sandbox.EnvironmentName!;
		EnvironmentSettings registeredEnvironment = RegisteredClioEnvironmentSettingsResolver.Resolve(environmentName);
		string environmentPath = Path.GetFullPath(
			ClioEnvironmentCommandResolver.ResolveEnvironmentPath(settings, environmentName));
		Directory.Exists(environmentPath).Should().BeTrue(
			because: "the registered clio environment path must exist on disk for end-to-end sandbox verification");

		string connectionStringsPath = Directory
			.GetFiles(environmentPath, "ConnectionStrings.config", SearchOption.AllDirectories)
			.FirstOrDefault()
			?? throw new InvalidOperationException(
				$"ConnectionStrings.config was not found under '{environmentPath}' for environment '{environmentName}'.");

		XDocument document = XDocument.Load(connectionStringsPath);
		string redisConnectionString = GetRequiredConnectionString(document, "redis", connectionStringsPath);
		string databaseConnectionString = GetRequiredConnectionString(document, "db", connectionStringsPath);

		return new SandboxEnvironmentContext(
			environmentName,
			registeredEnvironment.Uri,
			registeredEnvironment.Login,
			registeredEnvironment.Password,
			registeredEnvironment.IsNetCore,
			environmentPath,
			connectionStringsPath,
			redisConnectionString,
			databaseConnectionString);
	}

	private static string GetRequiredConnectionString(XDocument document, string name, string connectionStringsPath) {
		string? connectionString = document.Root?
			.Elements("add")
			.FirstOrDefault(element => string.Equals((string?)element.Attribute("name"), name, StringComparison.OrdinalIgnoreCase))?
			.Attribute("connectionString")?
			.Value;

		connectionString.Should().NotBeNullOrWhiteSpace(
			because: $"'{name}' must exist in {connectionStringsPath} for sandbox end-to-end tests");

		return connectionString!;
	}
}
