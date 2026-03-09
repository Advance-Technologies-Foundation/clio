using System.Diagnostics;
using FluentAssertions;
using Clio.Mcp.E2E.Support.Mcp;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class ClioEnvironmentCommandResolver {
	public static string ResolveEnvironmentPath(McpE2ESettings settings, string environmentName) {
		ClioProcessDescriptor command = ClioExecutableResolver.Resolve(settings, "envs", environmentName, "--format", "raw");
		ProcessStartInfo startInfo = new() {
			FileName = command.Command,
			WorkingDirectory = command.WorkingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		foreach (string argument in command.Arguments) {
			startInfo.ArgumentList.Add(argument);
		}

		using Process process = new() { StartInfo = startInfo };
		process.Start();
		string stdout = process.StandardOutput.ReadToEnd();
		string stderr = process.StandardError.ReadToEnd();
		process.WaitForExit();

		process.ExitCode.Should().Be(0,
			because: $"clio envs {environmentName} must succeed so the E2E suite can resolve the registered environment path. stderr: {stderr}");

		string? environmentPath = stdout
			.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Split(':', 2))
			.Where(parts => parts.Length == 2)
			.Where(parts => string.Equals(parts[0].Trim(), "EnvironmentPath", StringComparison.OrdinalIgnoreCase))
			.Select(parts => parts[1].Trim())
			.FirstOrDefault();

		environmentPath.Should().NotBeNullOrWhiteSpace(
			because: "clio envs must surface EnvironmentPath for a sandbox environment that can be inspected end to end");

		return environmentPath!;
	}
}
