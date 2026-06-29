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

		// Suppress clio's background self-update for EVERY e2e-spawned clio process from a single
		// seam. Both spawn paths (ClioCliCommandRunner.RunAsync and McpServerSession) forward
		// settings.ProcessEnvironmentVariables to the child, and clio's ShouldSkipUpdateCheck honors
		// CLIO_NO_UPDATE_CHECK. This removes the "Updating clio ... in background" confound from
		// install-gate and keeps the suite from racing an in-flight self-update. An explicit value
		// from configuration is preserved.
		if (!settings.ProcessEnvironmentVariables.ContainsKey("CLIO_NO_UPDATE_CHECK")) {
			settings.ProcessEnvironmentVariables["CLIO_NO_UPDATE_CHECK"] = "true";
		}

		return settings;
	}

	public static void EnsureSandboxIsConfigured(McpE2ESettings settings) {
		settings.Sandbox.EnvironmentName.Should().NotBeNullOrWhiteSpace(
			because: "destructive MCP tests need an explicit sandbox environment name");
		settings.Sandbox.SeedKeyPrefix.Should().NotBeNullOrWhiteSpace(
			because: "seeded Redis keys must use a predictable prefix for cleanup and diagnostics");
	}

	public static string ResolveFreshClioProcessPath() {
		string? runtimeRepositoryRoot = ResolveRepositoryRootFromTestOutput();
		if (!string.IsNullOrWhiteSpace(runtimeRepositoryRoot)) {
			string? runtimeRepositoryPath = ResolveRepositoryProcessPath(runtimeRepositoryRoot);
			if (!string.IsNullOrWhiteSpace(runtimeRepositoryPath)) {
				return runtimeRepositoryPath;
			}
		}

		string? sourceRepositoryRoot = ResolveRepositoryRootFromSourceFile();
		if (!string.IsNullOrWhiteSpace(sourceRepositoryRoot)) {
			string? sourceRepositoryPath = ResolveRepositoryProcessPath(sourceRepositoryRoot);
			if (!string.IsNullOrWhiteSpace(sourceRepositoryPath)) {
				return sourceRepositoryPath;
			}
		}

		string? repositoryRoot = ResolveRepositoryRoot();
		if (!string.IsNullOrWhiteSpace(repositoryRoot)) {
			string? repositoryProcessPath = ResolveRepositoryProcessPath(repositoryRoot);
			if (!string.IsNullOrWhiteSpace(repositoryProcessPath)) {
				return repositoryProcessPath;
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
				if (IsRepositoryRoot(current.FullName)) {
					return current.FullName;
				}
			}
		}

		return null;
	}

	private static string? ResolveRepositoryRootFromTestOutput() {
		string assemblyLocation = typeof(TestConfiguration).Assembly.Location;
		if (string.IsNullOrWhiteSpace(assemblyLocation)) {
			return null;
		}

		DirectoryInfo? current = new(Path.GetDirectoryName(assemblyLocation) ?? string.Empty);
		for (int step = 0; step < 4 && current is not null; step++) {
			current = current.Parent;
		}

		if (current is null) {
			return null;
		}

		return IsRepositoryRoot(current.FullName) ? current.FullName : null;
	}

	private static string? ResolveRepositoryRootFromSourceFile() {
		string sourceFilePath = GetCurrentSourceFilePath();
		if (string.IsNullOrWhiteSpace(sourceFilePath)) {
			return null;
		}

		DirectoryInfo? sourceDirectory = new(Path.GetDirectoryName(sourceFilePath) ?? string.Empty);
		DirectoryInfo? repositoryRoot = sourceDirectory.Parent?.Parent?.Parent;
		if (repositoryRoot is null) {
			return null;
		}

		return IsRepositoryRoot(repositoryRoot.FullName) ? repositoryRoot.FullName : null;
	}

	private static string? ResolveRepositoryProcessPath(string repositoryRoot) {
		(string configuration, string targetFramework) = ResolveCurrentBuildOutputLayout();
		string repositoryOutputDirectory = Path.Combine(repositoryRoot, "clio", "bin", configuration, targetFramework);
		string repositoryExecutablePath = Path.Combine(
			repositoryOutputDirectory,
			OperatingSystem.IsWindows() ? "clio.exe" : "clio.dll");
		if (File.Exists(repositoryExecutablePath)) {
			return repositoryExecutablePath;
		}

		string repositoryAssemblyPath = Path.Combine(repositoryOutputDirectory, "clio.dll");
		return File.Exists(repositoryAssemblyPath) ? repositoryAssemblyPath : null;
	}

	/// <summary>
	/// Derives the build configuration and target framework moniker from the running test
	/// assembly directory (for example <c>.../clio.mcp.e2e/bin/Debug/net8.0</c>) so the sibling
	/// clio output directory is resolved for the framework actually under test. Hardcoding a single
	/// moniker (previously <c>net10.0</c>) broke the multi-targeted suite when CI runs net8.0.
	/// </summary>
	private static (string Configuration, string TargetFramework) ResolveCurrentBuildOutputLayout() {
		string assemblyDirectory = Path.GetDirectoryName(typeof(TestConfiguration).Assembly.Location) ?? string.Empty;
		DirectoryInfo? targetFrameworkDirectory = string.IsNullOrWhiteSpace(assemblyDirectory)
			? null
			: new DirectoryInfo(assemblyDirectory);

		string targetFramework = targetFrameworkDirectory?.Name is { Length: > 0 } tfm ? tfm : "net8.0";
		string configuration = targetFrameworkDirectory?.Parent?.Name is { Length: > 0 } cfg ? cfg : "Debug";
		return (configuration, targetFramework);
	}

	private static bool IsRepositoryRoot(string directoryPath) {
		return File.Exists(Path.Combine(directoryPath, "Directory.Packages.props"))
			&& (File.Exists(Path.Combine(directoryPath, "clio.sln"))
				|| File.Exists(Path.Combine(directoryPath, "clio.slnx")));
	}

	private static string GetCurrentSourceFilePath([CallerFilePath] string sourceFilePath = "") {
		return sourceFilePath;
	}
}
