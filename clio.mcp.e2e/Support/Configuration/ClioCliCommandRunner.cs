using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class ClioCliCommandRunner {
	public static async Task<ClioCliCommandResult> RunAsync(
		McpE2ESettings settings,
		IReadOnlyList<string> arguments,
		string? workingDirectory = null,
		CancellationToken cancellationToken = default) {
		ClioProcessDescriptor command = ClioExecutableResolver.Resolve(settings, arguments.ToArray());
		ProcessStartInfo startInfo = new() {
			FileName = command.Command,
			WorkingDirectory = workingDirectory ?? command.WorkingDirectory,
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
		Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);

		return new ClioCliCommandResult(
			process.ExitCode,
			await stdoutTask,
			await stderrTask,
			startInfo.WorkingDirectory,
			string.Join(" ", startInfo.ArgumentList));
	}

	public static async Task RunAndAssertSuccessAsync(
		McpE2ESettings settings,
		IReadOnlyList<string> arguments,
		string? workingDirectory = null,
		CancellationToken cancellationToken = default) {
		ClioCliCommandResult result = await RunAsync(settings, arguments, workingDirectory, cancellationToken);
		result.ExitCode.Should().Be(0,
			because: $"the arrange step must succeed before MCP behavior can be asserted. command: clio {result.Arguments}. stderr: {result.StandardError}");
	}

	public static async Task EnsureCliogateInstalledAsync(
		McpE2ESettings settings,
		string environmentName,
		CancellationToken cancellationToken = default) {
		ClioCliCommandResult installResult = await RunAsync(
			settings,
			["install-gate", "-e", environmentName],
			cancellationToken: cancellationToken);
		if (installResult.ExitCode == 0) {
			return;
		}

		ClioCliCommandResult getPkgListResult = await RunAsync(
			settings,
			["get-pkg-list", "-e", environmentName, "--Json", "true"],
			cancellationToken: cancellationToken);
		bool cliogateAlreadyAvailable =
			getPkgListResult.ExitCode == 0 &&
			TryReadSuccessFlag(getPkgListResult.StandardOutput, out bool success) &&
			success;
		cliogateAlreadyAvailable.Should().BeTrue(
			because:
			$"the arrange step must either install cliogate successfully or confirm that get-pkg-list already works. install stderr: {installResult.StandardError}. install stdout: {installResult.StandardOutput}. verification stdout: {getPkgListResult.StandardOutput}. verification stderr: {getPkgListResult.StandardError}");
	}

	private static bool TryReadSuccessFlag(string output, out bool success) {
		success = false;
		if (string.IsNullOrWhiteSpace(output)) {
			return false;
		}

		try {
			using JsonDocument document = JsonDocument.Parse(output);
			if (!document.RootElement.TryGetProperty("success", out JsonElement successElement) ||
				(successElement.ValueKind != JsonValueKind.True && successElement.ValueKind != JsonValueKind.False)) {
				return false;
			}

			success = successElement.GetBoolean();
			return true;
		}
		catch (JsonException) {
			return false;
		}
	}
}

internal sealed record ClioCliCommandResult(
	int ExitCode,
	string StandardOutput,
	string StandardError,
	string WorkingDirectory,
	string Arguments);
