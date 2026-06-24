using System.Net;
using System.Net.Http;

namespace Clio.Mcp.E2E.Support.Configuration;

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
	/// <param name="baseUri">Environment base URI (for example <c>https://host/</c> or <c>https://host/0/</c>).</param>
	/// <param name="relativeRoute">cliogate route relative to <paramref name="baseUri"/>, for example <c>rest/CreatioApiGateway/GetApiVersion</c>.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>A task that completes once the route returns a serving status.</returns>
	Task WaitUntilServingAsync(string baseUri, string relativeRoute, CancellationToken cancellationToken);
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

	/// <inheritdoc />
	public async Task WaitUntilServingAsync(string baseUri, string relativeRoute, CancellationToken cancellationToken) {
		Uri requestUri = BuildRequestUri(baseUri, relativeRoute);
		HttpStatusCode? lastStatusCode = null;
		string? lastError = null;

		using CancellationTokenSource deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		if (_overallTimeout > TimeSpan.Zero) {
			deadlineCts.CancelAfter(_overallTimeout);
		}

		CancellationToken probeToken = deadlineCts.Token;
		for (int attempt = 0; attempt < _maxAttempts; attempt++) {
			cancellationToken.ThrowIfCancellationRequested();
			if (deadlineCts.IsCancellationRequested) {
				lastError = $"overall readiness deadline of {_overallTimeout.TotalSeconds:0.#}s exceeded";
				break;
			}

			try {
				using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
				using HttpResponseMessage response = await _httpClient.SendAsync(request, probeToken);
				lastStatusCode = response.StatusCode;
				lastError = null;
				if (IsServingStatus(response.StatusCode)) {
					return;
				}
			} catch (HttpRequestException exception) {
				// Connection refused / DNS failures while Creatio restarts are transient — keep retrying.
				lastStatusCode = null;
				lastError = exception.Message;
			} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
				// The caller cancelled — surface it rather than retrying.
				throw;
			} catch (OperationCanceledException) {
				// Per-request timeout or the overall deadline (not the caller cancelling).
				lastStatusCode = null;
				if (deadlineCts.IsCancellationRequested) {
					lastError = $"overall readiness deadline of {_overallTimeout.TotalSeconds:0.#}s exceeded";
					break;
				}

				lastError = "request timed out";
			}

			if (attempt < _maxAttempts - 1) {
				try {
					await Task.Delay(_delayBetweenAttempts, probeToken);
				} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
					throw;
				} catch (OperationCanceledException) {
					// Overall deadline tripped during the inter-attempt delay.
					lastError = $"overall readiness deadline of {_overallTimeout.TotalSeconds:0.#}s exceeded";
					break;
				}
			}
		}

		throw new CliogateReadinessTimeoutException(SanitizeUriForDiagnostics(requestUri), lastStatusCode, lastError, _maxAttempts);
	}

	/// <summary>
	/// Decides whether an HTTP status proves the cliogate REST module is actually serving the route.
	/// A registered route answers an unauthenticated GET with <c>2xx</c> or <c>401</c>/<c>403</c>;
	/// <c>404</c> (route not yet registered), <c>3xx</c> (redirect to a login/warm-up page) and
	/// <c>5xx</c> (IIS/ANCM warm-up errors) all mean "not ready yet" and must be retried — accepting
	/// any of them would reintroduce the false-positive readiness signal ENG-92146 removes.
	/// </summary>
	private static bool IsServingStatus(HttpStatusCode statusCode) {
		int code = (int)statusCode;
		return code is >= 200 and < 300
			|| statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
	}

	private static Uri BuildRequestUri(string baseUri, string relativeRoute) {
		string normalizedBase = baseUri.EndsWith('/') ? baseUri : baseUri + "/";
		string normalizedRoute = relativeRoute.StartsWith('/') ? relativeRoute[1..] : relativeRoute;
		return new Uri(new Uri(normalizedBase, UriKind.Absolute), normalizedRoute);
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
internal sealed class CliogateReadinessTimeoutException : Exception {
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
