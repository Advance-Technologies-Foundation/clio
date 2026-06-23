using System.Net;
using System.Net.Http;

namespace Clio.Mcp.E2E.Support.Configuration;

/// <summary>
/// Polls a cliogate HTTP route until the cliogate REST module is actually serving requests.
/// </summary>
/// <remarks>
/// The cliogate package can be installed and Creatio's DataService layer can report ready
/// while the <c>/rest/CreatioApiGateway/*</c> HTTP handlers are still warming up after a
/// restart. During that window the routes return HTTP 404, which is why a DataService-only
/// readiness check is a false-positive. This probe closes the gap by treating 404 as
/// "not ready yet" and any other status (200, 401, 302, …) as "the REST module is serving",
/// because an unauthenticated request to a registered route never returns 404.
/// </remarks>
internal interface ICliogateHttpReadinessProbe {
	/// <summary>
	/// Polls the cliogate route until it stops returning HTTP 404, or throws when retries are exhausted.
	/// </summary>
	/// <param name="baseUri">Environment base URI (for example <c>https://host/</c> or <c>https://host/0/</c>).</param>
	/// <param name="relativeRoute">cliogate route relative to <paramref name="baseUri"/>, for example <c>rest/CreatioApiGateway/GetApiVersion</c>.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>A task that completes once the route stops returning HTTP 404.</returns>
	Task WaitUntilServingAsync(string baseUri, string relativeRoute, CancellationToken cancellationToken);
}

/// <summary>
/// Default <see cref="ICliogateHttpReadinessProbe"/> implementation backed by <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// The probe issues an unauthenticated GET on purpose: it only needs to distinguish a
/// not-yet-registered route (HTTP 404) from a registered one (anything else). It therefore
/// requires no credentials and produces no side effects, which makes it safe to call
/// repeatedly and to unit-test against a stub HTTP server.
/// </remarks>
internal sealed class CliogateHttpReadinessProbe : ICliogateHttpReadinessProbe {
	private readonly HttpClient _httpClient;
	private readonly int _maxAttempts;
	private readonly TimeSpan _delayBetweenAttempts;

	/// <summary>
	/// Initializes a new instance of the <see cref="CliogateHttpReadinessProbe"/> class.
	/// </summary>
	/// <param name="httpClient">HTTP client used to issue GET requests against the probed route.</param>
	/// <param name="maxAttempts">Maximum number of GET attempts before failing.</param>
	/// <param name="delayBetweenAttempts">Delay between consecutive attempts.</param>
	public CliogateHttpReadinessProbe(HttpClient httpClient, int maxAttempts, TimeSpan delayBetweenAttempts) {
		ArgumentNullException.ThrowIfNull(httpClient);
		ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
		_httpClient = httpClient;
		_maxAttempts = maxAttempts;
		_delayBetweenAttempts = delayBetweenAttempts;
	}

	/// <inheritdoc />
	public async Task WaitUntilServingAsync(string baseUri, string relativeRoute, CancellationToken cancellationToken) {
		Uri requestUri = BuildRequestUri(baseUri, relativeRoute);
		HttpStatusCode? lastStatusCode = null;
		string? lastError = null;

		for (int attempt = 0; attempt < _maxAttempts; attempt++) {
			cancellationToken.ThrowIfCancellationRequested();
			try {
				using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
				using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
				lastStatusCode = response.StatusCode;
				lastError = null;
				if (response.StatusCode != HttpStatusCode.NotFound) {
					return;
				}
			} catch (HttpRequestException exception) {
				// Connection refused / DNS failures while Creatio restarts are transient — keep retrying.
				lastStatusCode = null;
				lastError = exception.Message;
			} catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) {
				// Per-request timeout (not the caller cancelling) is transient — keep retrying.
				lastStatusCode = null;
				lastError = "request timed out";
			}

			if (attempt < _maxAttempts - 1) {
				await Task.Delay(_delayBetweenAttempts, cancellationToken);
			}
		}

		throw new CliogateReadinessTimeoutException(requestUri.ToString(), lastStatusCode, lastError, _maxAttempts);
	}

	private static Uri BuildRequestUri(string baseUri, string relativeRoute) {
		string normalizedBase = baseUri.EndsWith('/') ? baseUri : baseUri + "/";
		string normalizedRoute = relativeRoute.StartsWith('/') ? relativeRoute[1..] : relativeRoute;
		return new Uri(new Uri(normalizedBase, UriKind.Absolute), normalizedRoute);
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
		string lastObservation = lastStatusCode.HasValue
			? $"HTTP {(int)lastStatusCode.Value} ({lastStatusCode.Value})"
			: $"unreachable ({lastError ?? "unknown error"})";
		return $"cliogate REST module did not start serving '{probedUri}' after {attempts} attempt(s); " +
			$"last observation: {lastObservation}. The DataService layer reported ready but the " +
			$"/rest/CreatioApiGateway/* handlers still returned 404, which is why MCP tests that call " +
			$"cliogate routes (PageCreate, BusinessRuleCreate, …) fail with (404) Not Found.";
	}
}
