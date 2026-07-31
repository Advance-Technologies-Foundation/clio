using System;
using System.Threading;
using Clio.Command;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Common;

/// <summary>
/// Target and timing parameters for a <see cref="IServerReadinessWaiter"/> wait.
/// </summary>
public sealed record ServerReadinessOptions {

	/// <summary>Base application uri to probe.</summary>
	public required string Uri { get; init; }

	/// <summary>Whether the target instance runs on .NET Core (WebAppLoader) or .NET Framework (WebHost).</summary>
	public required bool IsNetCore { get; init; }

	/// <summary>Total time budget to wait for readiness before giving up.</summary>
	public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(600);

	/// <summary>Delay before the first probe. The previous app domain may still answer briefly after a
	/// restart request, so an immediate probe risks a false-ready result.</summary>
	public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(10);

	/// <summary>Delay between subsequent probes.</summary>
	public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// When <see langword="true"/>, a passing liveness probe is necessary but NOT sufficient: the waiter
	/// additionally requires an authenticated application-layer round-trip to return a genuine JSON answer
	/// before reporting ready. This closes the gap where <c>/api/HealthCheck/Ping</c> answers (liveness) while
	/// the application layer still serves a login page / a 25s+ login during warm-up (ENG-94417). Left
	/// <see langword="false"/> (the default), the waiter keeps the pre-existing liveness-only behavior, so the
	/// Creatio installer's post-deploy readiness wait is unchanged.
	/// </summary>
	public bool RequireAuthenticatedReadiness { get; init; }

}

/// <summary>
/// Polls a Creatio instance's health-check endpoint until it responds successfully or a timeout elapses.
/// </summary>
public interface IServerReadinessWaiter {

	/// <summary>
	/// Waits for the target instance to answer its health-check endpoint and, when
	/// <see cref="ServerReadinessOptions.RequireAuthenticatedReadiness"/> is set, an authenticated
	/// application-layer round-trip as well.
	/// </summary>
	/// <param name="options">Target uri, host type, timing budget, and the authenticated-readiness toggle.</param>
	/// <returns><c>true</c> when a probe (and, when required, the authenticated round-trip) succeeded within the
	/// timeout; otherwise <c>false</c>.</returns>
	bool WaitForReady(ServerReadinessOptions options);

}

/// <inheritdoc cref="IServerReadinessWaiter"/>
public class ServerReadinessWaiter(
	HealthCheckCommand healthCheckCommand,
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	ILogger logger) : IServerReadinessWaiter {

	/// <summary>
	/// Test seam replacing <see cref="Thread.Sleep(TimeSpan)"/> so unit tests can exercise the wait loop
	/// without real delays.
	/// </summary>
	internal Action<TimeSpan> Sleep { get; set; } = Thread.Sleep;

	/// <summary>
	/// Floor for a single probe's request timeout. Keeps an all-but-exhausted budget from degenerating
	/// into a 0 ms request that fails before the instance can possibly answer.
	/// </summary>
	private const int MinProbeTimeoutMs = 1_000;

	/// <summary>
	/// Minimal authenticated DataService <c>SelectQuery</c> used as the application-layer round-trip. A
	/// <c>rowCount:1</c> select of <c>SysSettings.Id</c> — a core table present on every Creatio edition and
	/// readable by any authenticated user — is the cheapest call that proves the app layer authenticated the
	/// request and answered with genuine JSON. Its rows are irrelevant: only that a genuine JSON envelope came
	/// back (rather than a login page / redirect) matters, so even a permission/validation failure envelope
	/// still proves the application layer is serving authenticated requests.
	/// </summary>
	private const string AuthenticatedReadinessQuery =
		"{\"rootSchemaName\":\"SysSettings\",\"operationType\":0," +
		"\"columns\":{\"items\":{\"Id\":{\"expression\":{\"expressionType\":0,\"columnPath\":\"Id\"}}}}," +
		"\"rowCount\":1}";

	/// <inheritdoc/>
	public bool WaitForReady(ServerReadinessOptions options) {
		logger.WriteInfo($"Waiting {options.InitialDelay.TotalSeconds:0} seconds for server to start...");
		Sleep(options.InitialDelay);

		// Start the timeout budget AFTER the initial delay: the delay is a fixed pre-condition, not part
		// of the probing window. Computing the deadline before the delay meant any Timeout <= InitialDelay
		// (e.g. --ready-timeout 5 with the 10s default delay) elapsed before the loop ran and returned a
		// false "not ready" for a healthy instance. The do/while also guarantees at least one probe even
		// when the caller passes a tiny or non-positive Timeout.
		DateTime deadlineUtc = DateTime.UtcNow + options.Timeout;

		int attempt = 0;
		do {
			attempt++;
			// Bound EACH probe by what is left of the readiness budget. HealthCheckOptions inherits
			// RemoteCommandOptions' 100 s DefaultTimeout with MaxAttempts=3/RetryDelay=1, and the deadline
			// below is only checked AFTER Execute returns — so an instance that accepts the connection and
			// then stalls (the normal warm-up shape) could pin this loop for minutes past a small
			// waitTimeoutSeconds. MaxAttempts=1 because THIS loop is the retry: the inner retry would
			// multiply the overshoot while hiding it from the budget arithmetic.
			int probeTimeoutMs = ResolveProbeTimeoutMs(deadlineUtc);
			HealthCheckOptions healthOptions = new() {
				Uri = options.Uri,
				IsNetCore = options.IsNetCore,
				TimeOut = probeTimeoutMs,
				MaxAttempts = 1,
				RetryDelay = 0
			};
			// A passing liveness probe (exit 0) is necessary but, when an authenticated readiness signal is
			// required, NOT sufficient: /api/HealthCheck/Ping can answer while the application layer still
			// returns a login page / an unauthenticated redirect during warm-up (ENG-94417). Gate readiness on
			// the authenticated round-trip too, and keep polling until the deadline when only liveness answers.
			bool liveness = healthCheckCommand.Execute(healthOptions) == 0;
			if (liveness && (!options.RequireAuthenticatedReadiness || IsApplicationLayerReady(probeTimeoutMs))) {
				logger.WriteInfo($"Server is ready after {attempt} attempt(s).");
				return true;
			}

			if (DateTime.UtcNow >= deadlineUtc) {
				break;
			}

			logger.WriteInfo(
				$"Waiting for server to become ready... (attempt {attempt}). Next check in {options.PollInterval.TotalSeconds:0} seconds.");
			Sleep(options.PollInterval);
		} while (DateTime.UtcNow < deadlineUtc);

		logger.WriteWarning($"Server did not become ready within {options.Timeout.TotalSeconds:0} seconds.");
		return false;
	}

	/// <summary>
	/// Performs the authenticated application-layer round-trip: logs in and issues a minimal authenticated
	/// DataService call, returning <c>true</c> only when the app answers with a genuine JSON response (not a
	/// login page / HTML redirect / 401 auth-failure envelope). A transient warm-up failure (login page, a
	/// throw from <see cref="IApplicationClient.Login"/>, or a non-JSON body) is treated as "not ready yet" so
	/// the caller keeps polling until the deadline rather than failing hard.
	/// </summary>
	/// <param name="probeTimeoutMs">Per-request timeout, derived from the remaining readiness budget.</param>
	/// <returns><c>true</c> when the authenticated round-trip returned a genuine JSON answer; otherwise <c>false</c>.</returns>
	private bool IsApplicationLayerReady(int probeTimeoutMs) {
		try {
			applicationClient.Login();
			string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
			string body = applicationClient.ExecutePostRequest(url, AuthenticatedReadinessQuery, probeTimeoutMs, 1, 0);
			if (IsGenuineAuthenticatedJsonAnswer(body)) {
				return true;
			}
			logger.WriteInfo(
				"Liveness probe passed, but the authenticated application-layer round-trip has not returned a "
				+ "genuine JSON answer yet (login page / warm-up).");
			return false;
		} catch (Exception exception) {
			logger.WriteInfo(
				"Liveness probe passed, but the authenticated application-layer round-trip is not ready yet: "
				+ exception.Message);
			return false;
		}
	}

	/// <summary>
	/// Classifies a response body as a genuine authenticated application-layer answer. A login page (HTML) or a
	/// JSON 401 auth-failure envelope — both flagged by <see cref="ReauthExecutor.IsSessionExpiredResponse"/> —
	/// is rejected; any other well-formed JSON (an answer, or even a DataService failure envelope, both of which
	/// prove the app authenticated the request) is accepted.
	/// </summary>
	/// <param name="body">The raw response body of the authenticated round-trip.</param>
	/// <returns><c>true</c> when the body is a genuine authenticated JSON answer; otherwise <c>false</c>.</returns>
	internal static bool IsGenuineAuthenticatedJsonAnswer(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			return false;
		}
		if (ReauthExecutor.IsSessionExpiredResponse(body)) {
			return false;
		}
		try {
			JToken.Parse(body);
			return true;
		} catch (JsonReaderException) {
			return false;
		}
	}

	/// <summary>
	/// Per-probe request timeout, derived from the time left before <paramref name="deadlineUtc"/>. Never
	/// exceeds the inherited default (so this only ever tightens the probe, never loosens it) and never
	/// drops below <see cref="MinProbeTimeoutMs"/> — the do/while guarantees one probe even on an already
	/// exhausted budget, and that probe still deserves a usable window.
	/// </summary>
	private static int ResolveProbeTimeoutMs(DateTime deadlineUtc) {
		double remainingMs = (deadlineUtc - DateTime.UtcNow).TotalMilliseconds;
		return (int)Math.Clamp(remainingMs, MinProbeTimeoutMs, DefaultProbeTimeoutMs);
	}

	/// <summary>Upper bound for a single probe: <see cref="RemoteCommandOptions"/>' inherited default.</summary>
	private const int DefaultProbeTimeoutMs = 100_000;

}
