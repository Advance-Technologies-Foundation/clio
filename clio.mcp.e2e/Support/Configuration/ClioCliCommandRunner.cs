using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Clio.Common;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Configuration;

internal static class ClioCliCommandRunner {
	// Login-readiness budget. Runs FIRST in EnsureCliogateInstalledAsync, BEFORE the install-gate
	// loop, against the freshly-deployed stand. A just-deployed Creatio accepts TCP connections and
	// even serves anonymous pages before the auth pipeline finishes warming up; without this gate the
	// very first install-gate immediately fails inside CreatioClient.Login (HttpWebRequest.GetResponse)
	// and cascades to ~30 fixtures. ping-app is an AUTHENTICATED probe on .NET-Core stands: clio routes
	// it through CreatioClient.ExecuteGetRequest, which forces CreatioClient.Login via the lazy
	// AuthCookie — the SAME login codepath install-gate's UploadFile uses. So a 401 (bad creds / wrong
	// --ep) keeps failing this gate (cleanly isolating the auth case) while a transient warm-up is
	// absorbed by the retries. Mirrors the 12 × 5s shape of the readiness loops below.
	private const int CliogateLoginReadinessAttempts = 12;
	private static readonly TimeSpan CliogateLoginReadinessDelay = TimeSpan.FromSeconds(5);

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
		// Gate on an AUTHENTICATED login before attempting install-gate. install-gate's first action
		// is CreatioClient.Login; on a stand that was just deployed that login throws a WebException
		// (connect/timeout while warming up) and the whole arrange phase collapses. Waiting until the
		// stand actually accepts a login here absorbs the warm-up window and, if login NEVER succeeds,
		// fails with a credentials-specific diagnostic instead of a misleading install-gate failure.
		await WaitForLoginReadinessAsync(settings, environmentName, cancellationToken);

		// Re-register the env fresh so its stored IsNetCore is auto-detected against the LIVE stand
		// before install-gate runs. install-gate uploads the package through ServiceUrlBuilder.Build,
		// which prepends the `0/` workspace alias only when IsNetCore == false; a stale registration
		// with the wrong runtime targets a non-existent URL and the upload fails with HTTP 404 (so
		// cliogate never installs and ~30 fixtures cascade). reg-web-app with no --IsNetCore flag makes
		// clio probe the stand and overwrite the stored runtime — see RegAppCommand.ResolveIsNetCore.
		await ReRegisterEnvironmentWithRuntimeAutoDetectionAsync(settings, environmentName, cancellationToken);

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

	/// <summary>
	/// Re-registers <paramref name="environmentName"/> via <c>reg-web-app</c> using the credentials/URI
	/// already stored for it, but WITHOUT an explicit <c>--IsNetCore</c> flag, so clio auto-detects the
	/// runtime against the live stand and overwrites the persisted <c>IsNetCore</c>. This corrects a
	/// stale/wrong runtime on the registered env that would otherwise make install-gate upload the
	/// package to a URL missing (or wrongly carrying) the <c>0/</c> workspace alias and fail with HTTP
	/// 404. The resolved env name, URI, and <c>IsNetCore</c> are logged BEFORE and AFTER re-registration
	/// to <see cref="TestContext.Out"/> so a CI run shows the before→after runtime flip (or proves
	/// auto-detection wrong). Runs AFTER the login-readiness gate and BEFORE the install-gate loop.
	/// </summary>
	private static async Task ReRegisterEnvironmentWithRuntimeAutoDetectionAsync(
		McpE2ESettings settings,
		string environmentName,
		CancellationToken cancellationToken) {
		// Read the env exactly as the rest of the harness does (same appsettings.json source as the
		// probe URL resolver below) to reuse its URI/credentials for the fresh registration.
		EnvironmentSettings registeredEnvironment =
			RegisteredClioEnvironmentSettingsResolver.Resolve(environmentName);
		TestContext.Out.WriteLine(
			$"[reg-web-app] before re-registration: environment '{environmentName}', "
			+ $"uri '{registeredEnvironment.Uri}', IsNetCore {registeredEnvironment.IsNetCore}");

		// No -i/--IsNetCore: with a URI present and the flag absent, RegAppCommand.ResolveIsNetCore
		// runs EnvironmentRuntimeDetectionService against the live stand and persists the detected
		// runtime. No --check-login either: the login-readiness gate already proved the stand is
		// loginable, and reg-web-app performs no interactive prompts of its own, so this is safe and
		// non-interactive. Passing -i here would short-circuit detection and defeat the fix.
		ClioCliCommandResult registrationResult = await RunAsync(
			settings,
			[
				"reg-web-app", environmentName,
				"-u", registeredEnvironment.Uri,
				"-l", registeredEnvironment.Login,
				"-p", registeredEnvironment.Password
			],
			cancellationToken: cancellationToken);

		if (registrationResult.ExitCode != 0) {
			// Emit the COMPLETE captured output before asserting: FluentAssertions truncates long
			// assertion messages and would otherwise hide the real auto-detection failure (e.g. an
			// ambiguous/unreachable probe). TestContext output is not truncated. Mirrors the
			// install-gate / login-readiness diagnostics above.
			TestContext.Out.WriteLine(
				$"[reg-web-app] exit code: {registrationResult.ExitCode}");
			TestContext.Out.WriteLine(
				$"[reg-web-app] command: clio {registrationResult.Arguments}");
			TestContext.Out.WriteLine(
				$"[reg-web-app] full stdout:{System.Environment.NewLine}{registrationResult.StandardOutput}");
			TestContext.Out.WriteLine(
				$"[reg-web-app] full stderr:{System.Environment.NewLine}{registrationResult.StandardError}");
		}

		registrationResult.ExitCode.Should().Be(0,
			because:
			$"re-registering '{environmentName}' must succeed so install-gate targets the correct "
			+ $"runtime-specific URL. reg-web-app stdout: {registrationResult.StandardOutput}. "
			+ $"reg-web-app stderr: {registrationResult.StandardError}");

		EnvironmentSettings reResolvedEnvironment =
			RegisteredClioEnvironmentSettingsResolver.Resolve(environmentName);
		TestContext.Out.WriteLine(
			$"[reg-web-app] after re-registration: environment '{environmentName}', "
			+ $"uri '{reResolvedEnvironment.Uri}', IsNetCore {reResolvedEnvironment.IsNetCore}");
	}

	/// <summary>
	/// Polls <c>ping-app -e &lt;env&gt;</c> until it succeeds, confirming the freshly-deployed stand
	/// accepts an AUTHENTICATED session before install-gate is attempted. On .NET-Core stands clio
	/// runs <c>ping-app</c> through <c>CreatioClient.ExecuteGetRequest</c>, which forces
	/// <c>CreatioClient.Login</c> via the lazily-initialised auth cookie — the same login path
	/// install-gate's <c>UploadFile</c> exercises — so a 401/bad-credentials failure keeps failing
	/// this gate while a transient warm-up is retried away. On exhaustion it throws with the last
	/// command's exit code and full stdout/stderr (also written to <see cref="TestContext.Out"/>) so
	/// a never-loginable stand (creds / <c>--ep</c>) is cleanly distinguishable from a slow warm-up.
	/// </summary>
	private static async Task WaitForLoginReadinessAsync(
		McpE2ESettings settings,
		string environmentName,
		CancellationToken cancellationToken) {
		ClioCliCommandResult? lastResult = null;
		for (int attempt = 0; attempt < CliogateLoginReadinessAttempts; attempt++) {
			lastResult = await RunAsync(
				settings,
				["ping-app", "-e", environmentName],
				cancellationToken: cancellationToken);
			if (lastResult.ExitCode == 0) {
				return;
			}

			if (attempt < CliogateLoginReadinessAttempts - 1) {
				await Task.Delay(CliogateLoginReadinessDelay, cancellationToken);
			}
		}

		lastResult.Should().NotBeNull(
			because: "login-readiness polling should capture the last ping-app result for diagnostics");

		// Emit the COMPLETE captured output before asserting: FluentAssertions truncates long
		// assertion messages and would otherwise hide the real login failure (HTTP status / 401 /
		// "Cannot connect"). TestContext output is not truncated, so the full clio error is visible.
		int totalSeconds =
			(int)(CliogateLoginReadinessAttempts * CliogateLoginReadinessDelay.TotalSeconds);
		TestContext.Out.WriteLine(
			$"[login-readiness] exit code: {lastResult!.ExitCode}");
		TestContext.Out.WriteLine(
			$"[login-readiness] command: clio {lastResult.Arguments}");
		TestContext.Out.WriteLine(
			$"[login-readiness] full stdout:{System.Environment.NewLine}{lastResult.StandardOutput}");
		TestContext.Out.WriteLine(
			$"[login-readiness] full stderr:{System.Environment.NewLine}{lastResult.StandardError}");

		lastResult.ExitCode.Should().Be(0,
			because:
			$"the stand '{environmentName}' did not become loginable within {totalSeconds}s "
			+ $"({CliogateLoginReadinessAttempts} ping-app attempts every "
			+ $"{CliogateLoginReadinessDelay.TotalSeconds:0}s); a login that never succeeds points at "
			+ $"credentials/endpoint rather than warm-up. last exit code: {lastResult.ExitCode}. "
			+ $"ping-app stdout: {lastResult.StandardOutput}. ping-app stderr: {lastResult.StandardError}");
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
