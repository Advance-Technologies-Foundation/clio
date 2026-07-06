using System.Xml.Linq;
using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class SandboxEnvironmentResolver {
	public static SandboxEnvironmentContext Resolve(McpE2ESettings settings) {
		string environmentName = settings.Sandbox.EnvironmentName!;
		EnvironmentSettings registeredEnvironment = RegisteredClioEnvironmentSettingsResolver.Resolve(environmentName);
		string environmentPath = ResolveEnvironmentPath(settings, environmentName);
		Directory.Exists(environmentPath).Should().BeTrue(
			because: "the registered clio environment path must exist on disk for end-to-end sandbox verification");

		string connectionStringsPath = FindConnectionStringsConfig(environmentPath, environmentName);

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

	private const string ConnectionStringsConfigFileName = "ConnectionStrings.config";

	/// <summary>
	/// Locates <c>ConnectionStrings.config</c> inside a deployed Creatio web-app root.
	/// </summary>
	/// <remarks>
	/// The file lives at a fixed, well-known location for both runtimes:
	/// <list type="bullet">
	/// <item><description>.NET Core: <c>&lt;root&gt;/ConnectionStrings.config</c></description></item>
	/// <item><description>.NET Framework: <c>&lt;root&gt;/Terrasoft.WebApp/ConnectionStrings.config</c></description></item>
	/// </list>
	/// Those candidates are probed directly. A recursive scan of the whole deployed root is
	/// deliberately avoided: a real Creatio deploy holds tens of thousands of files (Pkg,
	/// Resources, node_modules, bin, ...), so a <see cref="SearchOption.AllDirectories"/>
	/// enumeration takes many minutes and effectively hangs the e2e suite (it had no timeout).
	/// </remarks>
	private static string FindConnectionStringsConfig(string environmentPath, string environmentName) {
		string[] knownRelativePaths = [
			ConnectionStringsConfigFileName,
			Path.Combine("Terrasoft.WebApp", ConnectionStringsConfigFileName)
		];

		foreach (string relativePath in knownRelativePaths) {
			string candidate = Path.Combine(environmentPath, relativePath);
			if (File.Exists(candidate)) {
				return candidate;
			}
		}

		throw new InvalidOperationException(
			$"{ConnectionStringsConfigFileName} was not found under '{environmentPath}' for environment '{environmentName}'. " +
			$"Probed: {string.Join(", ", knownRelativePaths)}. " +
			"Verify McpE2E__Sandbox__EnvironmentPath points at the deployed Creatio web-app root.");
	}

	private static string ResolveEnvironmentPath(McpE2ESettings settings, string environmentName) {
		if (!string.IsNullOrWhiteSpace(settings.Sandbox.EnvironmentPath)) {
			return Path.GetFullPath(settings.Sandbox.EnvironmentPath);
		}

		string resolved = ClioEnvironmentCommandResolver.ResolveEnvironmentPath(settings, environmentName);
		resolved.Should().NotBeNullOrWhiteSpace(
			because: $"clio envs must surface EnvironmentPath for '{environmentName}', or set McpE2E__Sandbox__EnvironmentPath in CI");
		return Path.GetFullPath(resolved);
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
