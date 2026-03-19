using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Clio.Command.McpServer;
using System.Runtime.CompilerServices;

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
		string? repositoryRoot = ResolveRepositoryRoot();
		if (!string.IsNullOrWhiteSpace(repositoryRoot)) {
			string repositoryOutputDirectory = Path.Combine(repositoryRoot, "clio", "bin", "Debug", "net8.0");
			string repositoryExecutablePath = Path.Combine(
				repositoryOutputDirectory,
				OperatingSystem.IsWindows() ? "clio.exe" : "clio.dll");
			if (File.Exists(repositoryExecutablePath)) {
				return repositoryExecutablePath;
			}

			string repositoryAssemblyPath = Path.Combine(repositoryOutputDirectory, "clio.dll");
			if (File.Exists(repositoryAssemblyPath)) {
				return repositoryAssemblyPath;
			}
		}

		string assemblyPath = typeof(McpServerCommand).Assembly.Location;
		if (!string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath)) {
			return assemblyPath;
		}

		throw new InvalidOperationException(
			"Unable to resolve a fresh clio process path from the repository output or the loaded assembly.");
	}

	private static string? ResolveRepositoryRoot() {
		string[] candidateDirectories = [
			Path.GetDirectoryName(GetCurrentSourceFilePath()) ?? string.Empty,
			Environment.CurrentDirectory,
			TestContext.CurrentContext.TestDirectory
		];

		foreach (string candidateDirectory in candidateDirectories.Where(directory => !string.IsNullOrWhiteSpace(directory))) {
			for (DirectoryInfo? current = new(candidateDirectory); current is not null; current = current.Parent) {
				if (File.Exists(Path.Combine(current.FullName, "Directory.Packages.props"))
					&& File.Exists(Path.Combine(current.FullName, "clio.sln"))) {
					return current.FullName;
				}
			}
		}

		return null;
	}

	private static string GetCurrentSourceFilePath([CallerFilePath] string sourceFilePath = "") {
		return sourceFilePath;
	}
}
