using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Clio.Common;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class ClioCliCommandRunner {
	private const int CliogateReadinessAttempts = 12;
	private static readonly TimeSpan CliogateReadinessDelay = TimeSpan.FromSeconds(5);

	private const int CliogateInstallAttempts = 6;
	private static readonly TimeSpan CliogateInstallDelay = TimeSpan.FromSeconds(10);

	// HTTP-handler readiness budget. This runs AFTER the DataService-level CliogateReadiness*
	// loop above, so the two budgets are ADDITIVE in the arrange phase: worst-case wait is
	// (CliogateReadinessAttempts * CliogateReadinessDelay) + this HTTP loop, the latter itself
	// capped by CliogateHttpReadinessOverallTimeout below. The attempts/delay deliberately mirror
	// the DataService values (12 × 5s) — keep them in sync if you tune the readiness window so the
	// two phases stay comparable rather than drifting apart.
	private const int CliogateHttpReadinessAttempts = 12;
	private static readonly TimeSpan CliogateHttpReadinessDelay = TimeSpan.FromSeconds(5);

	// A serving cliogate route answers in well under a second, so a short per-request timeout is
	// ample. Keeping it low matters: an accept-then-hang host during restart would otherwise burn
	// the full timeout on every attempt, multiplying the readiness budget into several minutes.
	private static readonly TimeSpan CliogateHttpRequestTimeout = TimeSpan.FromSeconds(10);

	// Hard ceiling for the whole HTTP-handler readiness loop so the worst case stays bounded
	// (min(attempt-budget, deadline)) regardless of how long individual requests hang.
	private static readonly TimeSpan CliogateHttpReadinessOverallTimeout = TimeSpan.FromMinutes(3);

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

		// Emit the COMPLETE captured stdout+stderr to the test log before asserting. FluentAssertions
		// truncates long assertion messages, which previously hid the real install-gate failure
		// (e.g. the CreatioClient.Login HTTP status / "Unable to connect" / 401). TestContext output is
		// not truncated, so this guarantees the full clio error is visible in the CI run.
		TestContext.Out.WriteLine(
			$"[install-gate] exit code: {lastInstallResult!.ExitCode}");
		TestContext.Out.WriteLine(
			$"[install-gate] command: clio {lastInstallResult.Arguments}");
		TestContext.Out.WriteLine(
			$"[install-gate] full stdout:{System.Environment.NewLine}{lastInstallResult.StandardOutput}");
		TestContext.Out.WriteLine(
			$"[install-gate] full stderr:{System.Environment.NewLine}{lastInstallResult.StandardError}");

		lastInstallResult.ExitCode.Should().Be(0,
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
		// Compose the probe URL through ServiceUrlBuilder so the .NET-Framework `0/` alias is applied
		// by the same canonical rule clio uses everywhere (it switches on IsNetCore) instead of
		// re-deriving the `/0` suffix here. Mirrors LookupRegistrationProbe's use of ServiceUrlBuilder.
		string probeUrl = new ServiceUrlBuilder(environment).Build(CliogateProbeRoute);

		using HttpClientHandler handler = new() {
			// Self-signed dev/CI stands serve over HTTPS with untrusted certs; this probe is a
			// read-only, unauthenticated GET that sends no credentials, body, or secrets, so
			// accepting any server cert here carries no exposure. Mirrors clio's production
			// HealthCheckCommand, which bypasses validation the same way for the same reason.
			ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
			// Observe a 3xx as a redirect (warming up) instead of following it to a login 200,
			// which would be mistaken for a serving route.
			AllowAutoRedirect = false
		};
		using HttpClient httpClient = new(handler) {
			Timeout = CliogateHttpRequestTimeout
		};
		ICliogateHttpReadinessProbe probe = new CliogateHttpReadinessProbe(
			httpClient,
			CliogateHttpReadinessAttempts,
			CliogateHttpReadinessDelay,
			CliogateHttpReadinessOverallTimeout);
		await probe.WaitUntilServingAsync(probeUrl, cancellationToken);
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
