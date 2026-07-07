using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common;

/// <summary>
/// Polls a cliogate HTTP route until the cliogate REST module is actually serving requests.
/// </summary>
/// <remarks>
/// The cliogate package can be installed and Creatio's DataService layer can report ready
/// while the <c>/rest/CreatioApiGateway/*</c> HTTP handlers are still warming up after a
/// restart. During that window the routes return HTTP 404, and a fronting IIS/ANCM layer or
/// proxy can answer with a transient 5xx (warm-up) or a 3xx redirect to a login/warm-up page
/// — all of which mean "not ready yet". A DataService-only readiness check is therefore a
/// false-positive. This probe closes the gap by treating only a status that proves the route
/// actually answered — <c>2xx</c>, or <c>401</c>/<c>403</c> from the unauthenticated GET — as
/// "the REST module is serving", and retrying every other status (404, 3xx, 5xx) until the
/// route answers or the budget is exhausted. Accepting a transient 5xx/3xx as ready would
/// reintroduce exactly the cascade ENG-92146 exists to remove.
/// </remarks>
internal interface ICliogateHttpReadinessProbe {
	/// <summary>
	/// Polls the cliogate route until it returns a status proving the route is serving
	/// (<c>2xx</c>, <c>401</c> or <c>403</c>), or throws when retries are exhausted.
	/// </summary>
	/// <param name="requestUri">
	/// Absolute cliogate route URI to probe, for example
	/// <c>https://host/rest/CreatioApiGateway/GetApiVersion</c> or
	/// <c>https://host/0/rest/CreatioApiGateway/GetApiVersion</c> on .NET Framework. Compose it with
	/// <see cref="ServiceUrlBuilder"/> so the <c>/0</c> net-framework alias is applied consistently.
	/// </param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>A task that completes once the route returns a serving status.</returns>
	Task WaitUntilServingAsync(string requestUri, CancellationToken cancellationToken);
}

/// <summary>
/// Default <see cref="ICliogateHttpReadinessProbe"/> implementation backed by <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// The probe issues an unauthenticated GET on purpose: it only needs to distinguish a route
/// that actually answered (<c>2xx</c>, or <c>401</c>/<c>403</c> for the no-auth GET) from one
/// that is still warming up (404 / 3xx / 5xx). It therefore requires no credentials and
/// produces no side effects, which makes it safe to call repeatedly and to unit-test against a
/// stub HTTP message handler. An optional overall deadline bounds the worst case so an
/// accept-then-hang host cannot stack per-request timeouts into a multi-minute arrange stall.
/// </remarks>
internal sealed class CliogateHttpReadinessProbe : ICliogateHttpReadinessProbe {
	private readonly HttpClient _httpClient;
	private readonly int _maxAttempts;
	private readonly TimeSpan _delayBetweenAttempts;
	private readonly TimeSpan _overallTimeout;

	/// <summary>
	/// Initializes a new instance of the <see cref="CliogateHttpReadinessProbe"/> class.
	/// </summary>
	/// <param name="httpClient">HTTP client used to issue GET requests against the probed route.</param>
	/// <param name="maxAttempts">Maximum number of GET attempts before failing.</param>
	/// <param name="delayBetweenAttempts">Delay between consecutive attempts.</param>
	/// <param name="overallTimeout">
	/// Optional hard ceiling for the whole polling loop. When greater than <see cref="TimeSpan.Zero"/>
	/// the worst case is bounded by <c>min(attempt-budget, overallTimeout)</c> regardless of how long
	/// individual requests hang. Pass <see cref="TimeSpan.Zero"/> (the default) to rely on the
	/// attempt budget alone.
	/// </param>
	public CliogateHttpReadinessProbe(
		HttpClient httpClient,
		int maxAttempts,
		TimeSpan delayBetweenAttempts,
		TimeSpan overallTimeout = default) {
		ArgumentNullException.ThrowIfNull(httpClient);
		ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
		_httpClient = httpClient;
		_maxAttempts = maxAttempts;
		_delayBetweenAttempts = delayBetweenAttempts;
		_overallTimeout = overallTimeout;
	}

	/// <summary>Result of a single probe attempt, driving the polling loop.</summary>
	private enum ProbeOutcome {
		/// <summary>The route returned a serving status — stop and succeed.</summary>
		Serving,

		/// <summary>The route is not serving yet (404/3xx/5xx, timeout, or transport error) — retry.</summary>
		Retry,

		/// <summary>The overall readiness deadline tripped — stop and fail.</summary>
		DeadlineExceeded
	}

	/// <inheritdoc />
	public async Task WaitUntilServingAsync(string requestUri, CancellationToken cancellationToken) {
		ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
		Uri probedUri = new(requestUri, UriKind.Absolute);
		HttpStatusCode? lastStatusCode = null;
		string? lastError = null;
		int attemptsPerformed = 0;

		using CancellationTokenSource deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		if (_overallTimeout > TimeSpan.Zero) {
			deadlineCts.CancelAfter(_overallTimeout);
		}

		CancellationToken probeToken = deadlineCts.Token;
		for (int attempt = 0; attempt < _maxAttempts; attempt++) {
			cancellationToken.ThrowIfCancellationRequested();
			if (deadlineCts.IsCancellationRequested) {
				lastError = OverallDeadlineExceededMessage();
				break;
			}

			attemptsPerformed++;
			(ProbeOutcome outcome, lastStatusCode, lastError) =
				await ProbeOnceAsync(probedUri, probeToken, cancellationToken, deadlineCts);
			if (outcome == ProbeOutcome.Serving) {
				return;
			}

			if (outcome == ProbeOutcome.DeadlineExceeded) {
				break;
			}

			if (attempt < _maxAttempts - 1) {
				string? delayError = await DelayBeforeNextAttemptAsync(probeToken, cancellationToken);
				if (delayError is not null) {
					lastError = delayError;
					break;
				}
			}
		}

		// Report the attempts actually performed, not _maxAttempts: when the overall deadline trips
		// the loop breaks early, so reporting the cap would blur "deadline tripped" against
		// "attempts exhausted". A genuine attempt-budget exhaustion still reports _maxAttempts.
		throw new CliogateReadinessTimeoutException(SanitizeUriForDiagnostics(probedUri), lastStatusCode, lastError, attemptsPerformed);
	}

	/// <summary>
	/// Issues a single GET and classifies the result, translating transport failures and the
	/// per-request timeout into a retry while letting caller cancellation propagate.
	/// </summary>
	private async Task<(ProbeOutcome Outcome, HttpStatusCode? StatusCode, string? Error)> ProbeOnceAsync(
		Uri probedUri,
		CancellationToken probeToken,
		CancellationToken cancellationToken,
		CancellationTokenSource deadlineCts) {
		try {
			using HttpRequestMessage request = new(HttpMethod.Get, probedUri);
			using HttpResponseMessage response = await _httpClient.SendAsync(request, probeToken);
			ProbeOutcome outcome = IsServingStatus(response.StatusCode) ? ProbeOutcome.Serving : ProbeOutcome.Retry;
			return (outcome, response.StatusCode, null);
		} catch (HttpRequestException exception) {
			// Connection refused / DNS failures while Creatio restarts are transient — keep retrying.
			return (ProbeOutcome.Retry, null, exception.Message);
		} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			// The caller cancelled — surface it rather than retrying.
			throw;
		} catch (OperationCanceledException) {
			// Per-request timeout or the overall deadline (not the caller cancelling).
			return deadlineCts.IsCancellationRequested
				? (ProbeOutcome.DeadlineExceeded, null, OverallDeadlineExceededMessage())
				: (ProbeOutcome.Retry, null, "request timed out");
		}
	}

	/// <summary>
	/// Waits between attempts, returning <c>null</c> to continue or the deadline message when the
	/// overall deadline trips during the delay. Caller cancellation propagates as a thrown exception.
	/// </summary>
	private async Task<string?> DelayBeforeNextAttemptAsync(CancellationToken probeToken, CancellationToken cancellationToken) {
		try {
			await Task.Delay(_delayBetweenAttempts, probeToken);
			return null;
		} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			throw;
		} catch (OperationCanceledException) {
			// Overall deadline tripped during the inter-attempt delay.
			return OverallDeadlineExceededMessage();
		}
	}

	private string OverallDeadlineExceededMessage() =>
		$"overall readiness deadline of {_overallTimeout.TotalSeconds:0.#}s exceeded";

	/// <summary>
	/// Decides whether an HTTP status proves the cliogate REST module is actually serving the route.
	/// A registered route answers an unauthenticated GET with <c>2xx</c> or <c>401</c>/<c>403</c>;
	/// <c>404</c> (route not yet registered), <c>3xx</c> (redirect to a login/warm-up page) and
	/// <c>5xx</c> (IIS/ANCM warm-up errors) all mean "not ready yet" and must be retried — accepting
	/// any of them would reintroduce the false-positive readiness signal ENG-92146 removes.
	/// <para>
	/// <strong>Known trade-off — 3xx is deliberately excluded.</strong> On a forms-auth .NET
	/// Framework topology a <em>ready</em> stand could in principle answer the anonymous probe with a
	/// <c>302</c> redirect to a login page, which this predicate then treats as "not serving" and
	/// retries to exhaustion against a healthy environment — the inverse failure mode. We accept that
	/// risk on purpose: a transient warm-up <c>3xx</c> and a ready-but-gated <c>302</c>-to-login are
	/// indistinguishable by status alone, and accepting <c>3xx</c> as serving would reopen the exact
	/// false positive this probe exists to close. The cliogate REST handlers answer the unauthenticated
	/// GET on <c>GetApiVersion</c> with <c>401</c> (not a redirect) on the .NET Core e2e targets this
	/// probe runs against. If a forms-auth stand is ever brought into the e2e matrix and a ready app
	/// there answers <c>302</c>, narrow this to treat a redirect to the login endpoint specifically as
	/// serving — confirm the real status against that stand first rather than widening blindly.
	/// </para>
	/// </summary>
	private static bool IsServingStatus(HttpStatusCode statusCode) {
		int code = (int)statusCode;
		return code is >= 200 and < 300
			|| statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
	}

	/// <summary>
	/// Strips any <c>user:pass@</c> userinfo from the probed URI before it is surfaced in logs or
	/// the timeout exception, so a base URI carrying credentials cannot leak them into diagnostics.
	/// </summary>
	private static string SanitizeUriForDiagnostics(Uri uri) {
		if (string.IsNullOrEmpty(uri.UserInfo)) {
			return uri.ToString();
		}

		UriBuilder builder = new(uri) { UserName = string.Empty, Password = string.Empty };
		return builder.Uri.ToString();
	}
}

/// <summary>
/// Raised when the cliogate HTTP route keeps returning HTTP 404 (or is unreachable) after all retries.
/// </summary>
public sealed class CliogateReadinessTimeoutException : Exception {
	/// <summary>
	/// Initializes a new instance of the <see cref="CliogateReadinessTimeoutException"/> class.
	/// </summary>
	/// <param name="probedUri">Absolute URI that was probed.</param>
	/// <param name="lastStatusCode">Last observed HTTP status code, or <c>null</c> when the route was unreachable.</param>
	/// <param name="lastError">Last transport error message, or <c>null</c> when the last attempt produced an HTTP status.</param>
	/// <param name="attempts">Number of attempts performed.</param>
	public CliogateReadinessTimeoutException(string probedUri, HttpStatusCode? lastStatusCode, string? lastError, int attempts)
		: base(BuildMessage(probedUri, lastStatusCode, lastError, attempts)) {
		ProbedUri = probedUri;
		LastStatusCode = lastStatusCode;
	}

	/// <summary>Gets the absolute URI that was probed.</summary>
	public string ProbedUri { get; }

	/// <summary>Gets the last observed HTTP status code, or <c>null</c> when the route was unreachable.</summary>
	public HttpStatusCode? LastStatusCode { get; }

	private static string BuildMessage(string probedUri, HttpStatusCode? lastStatusCode, string? lastError, int attempts) {
		string lastObservation;
		if (lastStatusCode.HasValue) {
			lastObservation = $"HTTP {(int)lastStatusCode.Value} ({lastStatusCode.Value})";
			if (!string.IsNullOrEmpty(lastError)) {
				// Keep the last status for diagnostics, but also surface why the loop ended
				// (for example an overall-deadline cut-off after the last observed status).
				lastObservation += $"; {lastError}";
			}
		} else {
			lastObservation = $"unreachable ({lastError ?? "unknown error"})";
		}
		return $"cliogate REST module did not start serving '{probedUri}' after {attempts} attempt(s); " +
			$"last observation: {lastObservation}. The DataService layer reported ready but the " +
			$"/rest/CreatioApiGateway/* handlers never returned a serving status (2xx/401/403) — they " +
			$"stayed on 404/3xx/5xx, which is why MCP tests that call cliogate routes (PageCreate, " +
			$"BusinessRuleCreate, …) fail with (404) Not Found or transient warm-up errors.";
	}
}
