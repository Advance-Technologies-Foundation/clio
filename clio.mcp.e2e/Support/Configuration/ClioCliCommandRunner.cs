using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class ClioCliCommandRunner {
	private const int CliogateReadinessAttempts = 12;
	private static readonly TimeSpan CliogateReadinessDelay = TimeSpan.FromSeconds(5);

	private const int CliogateInstallAttempts = 6;
	private static readonly TimeSpan CliogateInstallDelay = TimeSpan.FromSeconds(10);

	private const int CliogateHttpReadinessAttempts = 12;
	private static readonly TimeSpan CliogateHttpReadinessDelay = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Read-only, idempotent cliogate route used as the HTTP-handler readiness probe.
	/// It returns the cliogate assembly version (no <c>CheckCanManageSolution</c>, no DB writes),
	/// so it is safe to call repeatedly and returns 404 only while the REST module is not yet serving.
	/// </summary>
	private const string CliogateProbeRoute = "rest/CreatioApiGateway/GetApiVersion";

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

		foreach ((string key, string? value) in settings.ProcessEnvironmentVariables) {
			startInfo.Environment[key] = value ?? string.Empty;
		}

		using Process process = new() { StartInfo = startInfo };
		process.Start();
		Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
		Task<string> stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
		try {
			await process.WaitForExitAsync(cancellationToken);
		} catch (OperationCanceledException) {
			if (!process.HasExited) {
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync(CancellationToken.None);
			}
			throw;
		}

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
		ClioCliCommandResult? lastInstallResult = null;
		for (int attempt = 0; attempt < CliogateInstallAttempts; attempt++) {
			lastInstallResult = await RunAsync(
				settings,
				["install-gate", "-e", environmentName],
				cancellationToken: cancellationToken);
			if (lastInstallResult.ExitCode == 0) {
				await WaitForCliogateReadinessAsync(settings, environmentName, cancellationToken);
				return;
			}

			// Check if cliogate is already available before retrying
			ClioCliCommandResult pkgCheckResult = await RunAsync(
				settings,
				["list-packages", "-e", environmentName, "--Json", "true"],
				cancellationToken: cancellationToken);
			if (pkgCheckResult.ExitCode == 0 &&
				TryReadSuccessFlag(pkgCheckResult.StandardOutput, out bool alreadyReady) &&
				alreadyReady) {
				await WaitForCliogateReadinessAsync(settings, environmentName, cancellationToken);
				return;
			}

			if (attempt < CliogateInstallAttempts - 1) {
				await Task.Delay(CliogateInstallDelay, cancellationToken);
			}
		}

		lastInstallResult.Should().NotBeNull(
			because: "cliogate installation should capture the last install result for diagnostics");
		lastInstallResult!.ExitCode.Should().Be(0,
			because:
			$"cliogate must be installed before MCP tests that require it. " +
			$"install stderr: {lastInstallResult.StandardError}. install stdout: {lastInstallResult.StandardOutput}");
	}

	private static async Task WaitForCliogateReadinessAsync(
		McpE2ESettings settings,
		string environmentName,
		CancellationToken cancellationToken) {
		ClioCliCommandResult? lastResult = null;
		for (int attempt = 0; attempt < CliogateReadinessAttempts; attempt++) {
			lastResult = await RunAsync(
				settings,
				["list-packages", "-e", environmentName, "--Json", "true"],
				cancellationToken: cancellationToken);
			if (lastResult.ExitCode == 0 &&
				TryReadSuccessFlag(lastResult.StandardOutput, out bool success) &&
				success) {
				await WaitForCliogateHttpHandlersAsync(environmentName, cancellationToken);
				return;
			}

			if (attempt < CliogateReadinessAttempts - 1) {
				await Task.Delay(CliogateReadinessDelay, cancellationToken);
			}
		}

		lastResult.Should().NotBeNull(
			because: "cliogate readiness polling should capture the last command result for diagnostics");
		lastResult!.ExitCode.Should().Be(0,
			because:
			$"cliogate should become ready before destructive MCP tests proceed. stdout: {lastResult.StandardOutput}. stderr: {lastResult.StandardError}");
		TryReadSuccessFlag(lastResult.StandardOutput, out bool finalSuccess).Should().BeTrue(
			because:
			$"cliogate readiness polling should end only after list-packages reports success. stdout: {lastResult.StandardOutput}. stderr: {lastResult.StandardError}");
		finalSuccess.Should().BeTrue(
			because:
			$"cliogate should become ready before destructive MCP tests proceed. stdout: {lastResult.StandardOutput}. stderr: {lastResult.StandardError}");
	}

	/// <summary>
	/// After <c>list-packages</c> (DataService) reports ready, polls a read-only cliogate HTTP route
	/// until it stops returning 404. The DataService layer can come up before the
	/// <c>/rest/CreatioApiGateway/*</c> handlers do, so this closes the readiness race that causes
	/// MCP tools calling cliogate routes to fail with (404) Not Found.
	/// </summary>
	private static async Task WaitForCliogateHttpHandlersAsync(
		string environmentName,
		CancellationToken cancellationToken) {
		EnvironmentSettings environment = RegisteredClioEnvironmentSettingsResolver.Resolve(environmentName);
		string baseUri = environment.IsNetCore
			? environment.Uri
			: $"{environment.Uri.TrimEnd('/')}/0";

		using HttpClientHandler handler = new() {
			ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
		};
		using HttpClient httpClient = new(handler) {
			Timeout = TimeSpan.FromSeconds(30)
		};
		ICliogateHttpReadinessProbe probe = new CliogateHttpReadinessProbe(
			httpClient,
			CliogateHttpReadinessAttempts,
			CliogateHttpReadinessDelay);
		await probe.WaitUntilServingAsync(baseUri, CliogateProbeRoute, cancellationToken);
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
