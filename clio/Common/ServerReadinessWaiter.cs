using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
/// Outcome of one authenticated application-layer round-trip. <see cref="AuthenticationRejected"/> is kept
/// separate from <see cref="NotReady"/> because it is not a warm-up symptom: waiting longer cannot fix a
/// credential the application already refused.
/// </summary>
internal enum ApplicationLayerReadiness {

	/// <summary>The application answered a genuine authenticated DataService call.</summary>
	Ready,

	/// <summary>No genuine answer yet (login page, transport error, or the round-trip outran its budget).</summary>
	NotReady,

	/// <summary>The application answered and refused the credentials.</summary>
	AuthenticationRejected

}

/// <summary>
/// Classification of one poll attempt in <see cref="ServerReadinessWaiter.WaitForReady"/>, folding the
/// liveness probe and (when required) the authenticated round-trip into a single verdict for the loop.
/// </summary>
internal enum AttemptOutcome {

	/// <summary>The attempt proves the target is ready; the wait loop can return success.</summary>
	Ready,

	/// <summary>No verdict yet — the loop should keep polling until the deadline.</summary>
	NotReady,

	/// <summary>The authenticated round-trip was rejected too many times in a row; abort the wait.</summary>
	AuthenticationFailed

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
		int consecutiveAuthRejections = 0;
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
			AttemptOutcome outcome = EvaluateAttempt(options, liveness, probeTimeoutMs, ref consecutiveAuthRejections);
			if (outcome == AttemptOutcome.Ready) {
				logger.WriteInfo($"Server is ready after {attempt} attempt(s).");
				return true;
			}
			if (outcome == AttemptOutcome.AuthenticationFailed) {
				logger.WriteError(
					"The application answered its liveness probe, but the authenticated readiness round-trip was "
					+ $"rejected {consecutiveAuthRejections} times in a row. This is an authentication failure, not "
					+ "a warm-up delay — verify the environment credentials (e.g. 'clio reg-web-app "
					+ "--check-login') instead of waiting longer.");
				return false;
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
	/// Classifies one probe attempt against the liveness result and, when required, the authenticated
	/// round-trip. Extracted from <see cref="WaitForReady"/> to keep the poll loop's cognitive complexity low.
	/// </summary>
	/// <param name="options">Target options; only <see cref="ServerReadinessOptions.RequireAuthenticatedReadiness"/>
	/// is consulted here.</param>
	/// <param name="liveness">Result of the liveness probe for this attempt.</param>
	/// <param name="probeTimeoutMs">Per-request timeout for the authenticated round-trip, when needed.</param>
	/// <param name="consecutiveAuthRejections">Running count of consecutive authentication rejections; updated
	/// in place so the caller can report it and the next attempt can keep accumulating it.</param>
	/// <returns>Whether this attempt proves readiness, should continue polling, or should abort as an
	/// authentication failure.</returns>
	private AttemptOutcome EvaluateAttempt(
		ServerReadinessOptions options,
		bool liveness,
		int probeTimeoutMs,
		ref int consecutiveAuthRejections) {
		if (!liveness) {
			return AttemptOutcome.NotReady;
		}
		if (!options.RequireAuthenticatedReadiness) {
			return AttemptOutcome.Ready;
		}

		ApplicationLayerReadiness readiness = ProbeApplicationLayer(probeTimeoutMs);
		if (readiness == ApplicationLayerReadiness.Ready) {
			return AttemptOutcome.Ready;
		}
		// A rejected credential is not a warm-up symptom and will not heal by waiting: retrying it
		// burns the whole readiness budget and then reports a misleading generic timeout. Two
		// consecutive rejections (not one) before giving up, so a single transient rejection during
		// security-cache warm-up cannot abort an otherwise healthy wait.
		consecutiveAuthRejections = readiness == ApplicationLayerReadiness.AuthenticationRejected
			? consecutiveAuthRejections + 1
			: 0;
		return consecutiveAuthRejections >= MaxConsecutiveAuthRejections
			? AttemptOutcome.AuthenticationFailed
			: AttemptOutcome.NotReady;
	}

	/// <summary>
	/// Runs the authenticated round-trip under a hard wall-clock bound of <paramref name="probeTimeoutMs"/>.
	/// The round-trip cannot be bounded from the inside: <c>creatio.client</c> establishes its session lazily
	/// (<c>InitAuthCookie</c> → <c>Login</c> + up to three pings, each on its own ~100 s
	/// <see cref="System.Net.HttpWebRequest.Timeout"/>, with a 300 s response-stream read timeout on top), and
	/// <see cref="IApplicationClient.Login"/> exposes no timeout or cancellation at all. Against the shape this
	/// ticket targets — a socket that accepts and then never answers — a single in-flight round-trip could
	/// therefore overshoot the caller's whole readiness budget inside ONE poll iteration. Running it on a
	/// background task and abandoning the wait keeps the caller's deadline authoritative; the abandoned thread
	/// is not leaked indefinitely — it ends when the client's own internal timeouts fire.
	/// </summary>
	/// <param name="probeTimeoutMs">Per-request timeout, derived from the remaining readiness budget.</param>
	/// <returns>The readiness classification of this round-trip.</returns>
	private ApplicationLayerReadiness ProbeApplicationLayer(int probeTimeoutMs) {
		Task<ApplicationLayerReadiness> roundTrip =
			Task.Run(() => ExecuteAuthenticatedRoundTrip(probeTimeoutMs));
		if (roundTrip.Wait(probeTimeoutMs)) {
			return roundTrip.Result;
		}
		logger.WriteInfo(
			"Liveness probe passed, but the authenticated application-layer round-trip did not answer within "
			+ $"{probeTimeoutMs} ms (the instance accepts connections but is not serving yet).");
		return ApplicationLayerReadiness.NotReady;
	}

	/// <summary>
	/// Issues a minimal authenticated DataService call and classifies the answer. No explicit
	/// <see cref="IApplicationClient.Login"/> is performed: <c>creatio.client</c> establishes the session on
	/// demand (and <see cref="ReauthExecutor"/> re-establishes it when a stale one returns the login page), so an
	/// explicit login would be redundant for the login/password path AND actively wrong for the OAuth and
	/// bearer-passthrough paths — those clients carry a token instead of credentials, so
	/// <see cref="IApplicationClient.Login"/> would post an empty username/password and throw, making readiness
	/// unreachable for a perfectly healthy instance.
	/// </summary>
	/// <param name="probeTimeoutMs">Per-request timeout, derived from the remaining readiness budget.</param>
	/// <returns>The readiness classification of this round-trip.</returns>
	private ApplicationLayerReadiness ExecuteAuthenticatedRoundTrip(int probeTimeoutMs) {
		try {
			string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
			string body = applicationClient.ExecutePostRequest(url, AuthenticatedReadinessQuery, probeTimeoutMs, 1, 0);
			if (IsGenuineAuthenticatedJsonAnswer(body)) {
				return ApplicationLayerReadiness.Ready;
			}
			logger.WriteInfo(
				"Liveness probe passed, but the authenticated application-layer round-trip has not returned a "
				+ "genuine DataService answer yet (login page / warm-up).");
			return ApplicationLayerReadiness.NotReady;
		} catch (UnauthorizedAccessException exception) {
			// Distinguishable from a warm-up stall: the application answered and REFUSED the credentials.
			logger.WriteInfo(
				"The authenticated application-layer round-trip was rejected by the application: "
				+ exception.Message);
			return ApplicationLayerReadiness.AuthenticationRejected;
		} catch (Exception exception) {
			logger.WriteInfo(
				"Liveness probe passed, but the authenticated application-layer round-trip is not ready yet: "
				+ exception.Message);
			return ApplicationLayerReadiness.NotReady;
		}
	}

	/// <summary>
	/// Classifies a response body as a genuine authenticated DataService answer. The check is POSITIVE: the body
	/// must be a JSON object carrying a marker of the DataService response contract
	/// (<see cref="DataServiceAnswerMarkers"/>) — either a <c>SelectQuery</c> result or a DataService error
	/// envelope, both of which prove the application layer authenticated the request and executed it. Accepting
	/// "any JSON that is not a login page" instead would re-admit the very class of false-ready answer this
	/// ticket removes, only JSON-shaped: a reverse proxy, gateway, or half-initialized app tier answering
	/// <c>{"error":"502"}</c> would pass. A login page / session-expired envelope is rejected up front.
	/// </summary>
	/// <param name="body">The raw response body of the authenticated round-trip.</param>
	/// <returns><c>true</c> when the body is a genuine authenticated DataService answer; otherwise <c>false</c>.</returns>
	internal static bool IsGenuineAuthenticatedJsonAnswer(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			return false;
		}
		if (ReauthExecutor.IsSessionExpiredResponse(body)) {
			return false;
		}
		try {
			return JToken.Parse(body) is JObject answer
				&& answer.Properties().Any(property =>
					DataServiceAnswerMarkers.Contains(property.Name, StringComparer.OrdinalIgnoreCase));
		} catch (JsonReaderException) {
			return false;
		}
	}

	/// <summary>
	/// Property names that identify a DataService response: <c>rows</c>/<c>rowsAffected</c> from a successful
	/// <c>SelectQuery</c>, and <c>success</c>/<c>errorInfo</c> from either outcome's envelope.
	/// </summary>
	private static readonly string[] DataServiceAnswerMarkers = [
		"rows", "rowsAffected", "success", "errorInfo"
	];

	/// <summary>Consecutive credential rejections tolerated before the wait gives up (see WaitForReady).</summary>
	private const int MaxConsecutiveAuthRejections = 2;

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
