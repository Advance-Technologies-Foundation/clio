using Clio.Command.McpServer;
using Clio.Mcp.E2E.Support.Configuration;

namespace Clio.Mcp.E2E.Support.Mcp;

internal static class ClioExecutableResolver {
	private static readonly string UserDotnetPath =
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet");

	public static ClioProcessDescriptor Resolve(McpE2ESettings settings) {
		return Resolve(settings, "mcp-server");
	}

	public static ClioProcessDescriptor Resolve(McpE2ESettings settings, params string[] commandArguments) {
		string? configuredPath = settings.ClioProcessPath;
		if (!string.IsNullOrWhiteSpace(configuredPath)) {
			return CreateFromConfiguredPath(configuredPath, commandArguments);
		}

		string assemblyPath = typeof(McpServerCommand).Assembly.Location;
		string workingDirectory = Path.GetDirectoryName(assemblyPath)
			?? throw new InvalidOperationException("Unable to determine the clio assembly directory.");

		return new ClioProcessDescriptor(
			Command: ResolveDotnetHostPath(),
			Arguments: [assemblyPath, .. commandArguments],
			WorkingDirectory: workingDirectory);
	}

	private static ClioProcessDescriptor CreateFromConfiguredPath(string configuredPath, IReadOnlyList<string> commandArguments) {
		string fullPath = Path.GetFullPath(configuredPath);
		string workingDirectory = Path.GetDirectoryName(fullPath)
			?? throw new InvalidOperationException($"Unable to determine working directory for '{fullPath}'.");

		if (fullPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) {
			return new ClioProcessDescriptor(
				Command: ResolveDotnetHostPath(),
				Arguments: [fullPath, .. commandArguments],
				WorkingDirectory: workingDirectory);
		}

		return new ClioProcessDescriptor(
			Command: fullPath,
			Arguments: [.. commandArguments],
			WorkingDirectory: workingDirectory);
	}

	private static string ResolveDotnetHostPath() {
		string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
		if (!string.IsNullOrWhiteSpace(dotnetRoot)) {
			string dotnetFromRoot = Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
			if (File.Exists(dotnetFromRoot)) {
				return dotnetFromRoot;
			}
		}

		if (File.Exists(UserDotnetPath)) {
			return UserDotnetPath;
		}

		return "dotnet";
	}
}
