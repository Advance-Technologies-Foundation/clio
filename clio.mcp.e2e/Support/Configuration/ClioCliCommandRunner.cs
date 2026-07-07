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

	/// <summary>
	/// Returns true when the configured sandbox environment is reachable (a successful <c>ping-app</c>).
	/// Used by the process-designer E2E fixtures to skip (Assert.Ignore) on an unreachable stand instead of
	/// failing — the same gate the get-process-signature / generate-process-model E2E tests use.
	/// </summary>
	public static async Task<bool> IsEnvironmentReachableAsync(
		McpE2ESettings settings,
		string environmentName,
		CancellationToken cancellationToken = default) {
		ClioCliCommandResult result = await RunAsync(
			settings,
			["ping-app", "-e", environmentName],
			cancellationToken: cancellationToken);
		return result.ExitCode == 0;
	}

	public static async Task RunAndAssertSuccessAsync(
		McpE2ESettings settings,
		IReadOnlyList<string> arguments,
		string? workingDirectory = null,
		CancellationToken cancellationToken = default) {
		ClioCliCommandResult result = await RunAsync(settings, arguments, workingDirectory, cancellationToken);
		result.ExitCode.Should().Be(0,
			because: $"the arrange step must succeed before MCP behavior can be asserted. command: clio {RedactSecrets(result.Arguments)}. stderr: {result.StandardError}");
	}

	// Flags whose following value is a secret and must never reach the CI log. reg-web-app passes the
	// sandbox login/password verbatim, and the diagnostics blocks echo the full command line on failure.
	private static readonly string[] SecretArgumentFlags = ["-p", "--password", "-l", "--login"];

	/// <summary>
	/// Redacts the value that follows a secret flag (<c>-p</c>/<c>--password</c>, <c>-l</c>/<c>--login</c>)
	/// in a space-joined clio argument string before it is written to <see cref="TestContext.Out"/>.
	/// Without this the plaintext sandbox password (passed verbatim to <c>reg-web-app</c>) would land in
	/// the TeamCity build log on the very failure path these diagnostics exist to surface. Handles both
	/// the space-separated (<c>-p secret</c>) and inline (<c>--password=secret</c>) forms.
	/// </summary>
	internal static string RedactSecrets(string arguments) {
		if (string.IsNullOrEmpty(arguments)) {
			return arguments;
		}

		string[] tokens = arguments.Split(' ');
		for (int i = 0; i < tokens.Length; i++) {
			int separator = tokens[i].IndexOf('=');
			string flag = separator >= 0 ? tokens[i][..separator] : tokens[i];
			if (!SecretArgumentFlags.Contains(flag, StringComparer.OrdinalIgnoreCase)) {
				continue;
			}

			if (separator >= 0) {
				tokens[i] = $"{flag}=***";
			} else if (i + 1 < tokens.Length) {
				tokens[i + 1] = "***";
			}
		}

		return string.Join(" ", tokens);
	}

	public static async Task EnsureCliogateInstalledAsync(
		McpE2ESettings settings,
		string environmentName,
		CancellationToken cancellationToken = default) {
		// Probe-first: when cliogate is already serving on the registered environment — e.g. a CI
		// site-prep step ran reg-web-app + install-gate once before the test run (ENG-91829) — skip the
		// per-test re-register, login-readiness gate, and install-gate entirely. A single cheap HTTP GET
		// against a real /rest/CreatioApiGateway/* route replaces tens of seconds of per-test arrange.
		// Any negative result (env not registered, route 404/warming up, stand unreachable) falls through
		// to the full self-install path below, which local/manual runs without a prep step still rely on.
		if (await IsCliogateAlreadyServingAsync(environmentName, cancellationToken)) {
			TestContext.Out.WriteLine(
				$"[cliogate] '{environmentName}' is already serving cliogate routes; skipping per-test reg-web-app/install-gate (site prep handled it).");
			return;
		}

		// Re-register the sandbox env at the freshly-deployed URL FIRST, before we ping/login it. In CI
		// the env name ("dev") is registered ahead of time but its stored URL is stale (the build's
		// real URL is only known at runtime, exposed as the TeamCity param DeployedUrl). Correcting the
		// URL here — using the configured Sandbox.EnvironmentUrl, NOT the env's own stored URL — points
		// the subsequent login-readiness gate and install-gate at the right stand instead of a default
		// localhost that returns 404 and cascades to ~30 fixtures.
		await ReRegisterSandboxEnvironmentAsync(settings, environmentName, cancellationToken);

		// Gate on an AUTHENTICATED login before attempting install-gate. install-gate's first action
		// is CreatioClient.Login; on a stand that was just deployed that login throws a WebException
		// (connect/timeout while warming up) and the whole arrange phase collapses. Waiting until the
		// stand actually accepts a login here absorbs the warm-up window and, if login NEVER succeeds,
		// fails with a credentials-specific diagnostic instead of a misleading install-gate failure.
		await WaitForLoginReadinessAsync(settings, environmentName, cancellationToken);

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
			$"[install-gate] command: clio {RedactSecrets(lastInstallResult.Arguments)}");
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
	/// Re-registers the sandbox env at the configured <see cref="SandboxSettings.EnvironmentUrl"/> via
	/// <c>reg-web-app</c> so the suite targets the freshly-deployed stand instead of a stale (default
	/// localhost) registration. When no URL override is configured this is a no-op, preserving the
	/// behavior local/manual setups rely on. The env's existing credentials are reused
	/// (<see cref="RegisteredClioEnvironmentSettingsResolver.Resolve"/>); no <c>--IsNetCore</c> flag is
	/// passed so clio auto-detects the runtime against the live stand (see
	/// <c>RegAppCommand.ResolveIsNetCore</c>). On failure the full stdout/stderr is written to
	/// <see cref="TestContext.Out"/>, mirroring the install-gate diagnostics, before asserting exit 0.
	/// </summary>
	private static async Task ReRegisterSandboxEnvironmentAsync(
		McpE2ESettings settings,
		string environmentName,
		CancellationToken cancellationToken) {
		string? environmentUrl = settings.Sandbox.EnvironmentUrl;
		if (string.IsNullOrWhiteSpace(environmentUrl)) {
			// No URL override: keep the existing registration untouched so local/manual runs that
			// point the sandbox at their own stand are unaffected.
			TestContext.Out.WriteLine(
				$"[reg-web-app] no Sandbox.EnvironmentUrl configured; skipping re-registration of '{environmentName}' (using the existing registration).");
			return;
		}

		// Reuse the env's already-registered credentials; the only thing we are correcting is the URL.
		EnvironmentSettings environment = RegisteredClioEnvironmentSettingsResolver.Resolve(environmentName);

		ClioCliCommandResult result = await RunAsync(
			settings,
			["reg-web-app", environmentName, "-u", environmentUrl, "-l", environment.Login, "-p", environment.Password],
			cancellationToken: cancellationToken);

		if (result.ExitCode != 0) {
			// Emit the COMPLETE captured output before asserting: FluentAssertions truncates long
			// assertion messages and would otherwise hide the real reg-web-app failure (e.g. the
			// runtime auto-detection HTTP error). TestContext output is not truncated.
			TestContext.Out.WriteLine(
				$"[reg-web-app] exit code: {result.ExitCode}");
			TestContext.Out.WriteLine(
				$"[reg-web-app] command: clio {RedactSecrets(result.Arguments)}");
			TestContext.Out.WriteLine(
				$"[reg-web-app] full stdout:{System.Environment.NewLine}{result.StandardOutput}");
			TestContext.Out.WriteLine(
				$"[reg-web-app] full stderr:{System.Environment.NewLine}{result.StandardError}");
		}

		result.ExitCode.Should().Be(0,
			because:
			$"the sandbox env '{environmentName}' must be re-registered at the deployed URL before login/install. " +
			$"reg-web-app stdout: {result.StandardOutput}. reg-web-app stderr: {result.StandardError}");

		// Read back the runtime clio auto-detected so the CI log records exactly what the subsequent
		// gates will target (env name + corrected URL + resolved IsNetCore).
		EnvironmentSettings registered = RegisteredClioEnvironmentSettingsResolver.Resolve(environmentName);
		TestContext.Out.WriteLine(
			$"[reg-web-app] re-registered '{environmentName}' at {environmentUrl} (auto-detected IsNetCore={registered.IsNetCore}).");
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
			$"[login-readiness] command: clio {RedactSecrets(lastResult.Arguments)}");
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
	/// One-shot, short-budget check of whether cliogate is already serving its HTTP handlers on the
	/// registered <paramref name="environmentName"/>. Used by <see cref="EnsureCliogateInstalledAsync"/>
	/// to skip the expensive per-test reg-web-app/login/install path when a CI site-prep step has
	/// already installed cliogate once (ENG-91829). Returns <c>false</c> on any negative or error
	/// outcome (env not registered, route still 404/warming up, stand unreachable) so the caller falls
	/// back to the full self-install path; caller cancellation is propagated.
	/// </summary>
	private static async Task<bool> IsCliogateAlreadyServingAsync(
		string environmentName,
		CancellationToken cancellationToken) {
		try {
			// Single attempt bounded by one request timeout: a serving route returns immediately; a
			// not-yet-serving or unreachable route throws CliogateReadinessTimeoutException, which we
			// translate to "not ready" rather than waiting out the full install-readiness budget here.
			await ProbeCliogateServingAsync(
				environmentName,
				maxAttempts: 1,
				delayBetweenAttempts: TimeSpan.Zero,
				overallTimeout: CliogateHttpRequestTimeout,
				cancellationToken: cancellationToken);
			return true;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			throw;
		}
		catch (Exception exception) {
			TestContext.Out.WriteLine(
				$"[cliogate] readiness probe for '{environmentName}' did not confirm a serving route " +
				$"({exception.GetType().Name}: {exception.Message}); falling back to reg-web-app + install-gate.");
			return false;
		}
	}

	/// <summary>
	/// After <c>list-packages</c> (DataService) reports ready, polls a read-only cliogate HTTP route
	/// until it stops returning 404. The DataService layer can come up before the
	/// <c>/rest/CreatioApiGateway/*</c> handlers do, so this closes the readiness race that causes
	/// MCP tools calling cliogate routes to fail with (404) Not Found.
	/// </summary>
	private static Task WaitForCliogateHttpHandlersAsync(
		string environmentName,
		CancellationToken cancellationToken) =>
		ProbeCliogateServingAsync(
			environmentName,
			CliogateHttpReadinessAttempts,
			CliogateHttpReadinessDelay,
			CliogateHttpReadinessOverallTimeout,
			cancellationToken);

	/// <summary>
	/// Shared cliogate HTTP-probe setup for both the one-shot skip check
	/// (<see cref="IsCliogateAlreadyServingAsync"/>) and the post-install readiness poll
	/// (<see cref="WaitForCliogateHttpHandlersAsync"/>). Resolves the env, composes the probe URL,
	/// and wires the HttpClient/probe with the cert-accept + no-redirect rationale once, so the two
	/// call sites differ only in their attempt/delay/timeout budget and the non-obvious TLS/redirect
	/// rationale cannot drift between them.
	/// </summary>
	private static async Task ProbeCliogateServingAsync(
		string environmentName,
		int maxAttempts,
		TimeSpan delayBetweenAttempts,
		TimeSpan overallTimeout,
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
			maxAttempts,
			delayBetweenAttempts,
			overallTimeout);
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
