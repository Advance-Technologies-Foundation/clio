using System.Diagnostics;
using FluentAssertions;
using Clio.Mcp.E2E.Support.Mcp;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class ClioEnvironmentCommandResolver {
	public static string ResolveEnvironmentPath(McpE2ESettings settings, string environmentName) {
		return ResolveEnvironmentValue(settings, environmentName, "EnvironmentPath");
	}

	public static string ResolveEnvironmentUri(McpE2ESettings settings, string environmentName) {
		return ResolveEnvironmentValue(settings, environmentName, "Uri");
	}

	private static string ResolveEnvironmentValue(McpE2ESettings settings, string environmentName,
		string fieldName) {
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
			because: $"clio envs {environmentName} must succeed so the E2E suite can resolve registered environment metadata. stderr: {stderr}");

		return ResolveEnvironmentValue(stdout, fieldName);
	}

	internal static string ResolveEnvironmentValue(string stdout, string fieldName) {
		string? value = stdout
			.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Split(':', 2))
			.Where(parts => parts.Length == 2)
			.Where(parts => string.Equals(parts[0].Trim(), fieldName, StringComparison.OrdinalIgnoreCase))
			.Select(parts => parts[1].Trim())
			.FirstOrDefault();

		value.Should().NotBeNullOrWhiteSpace(
			because: $"clio envs must surface {fieldName} for the configured E2E sandbox environment");

		return value!;
	}
}
