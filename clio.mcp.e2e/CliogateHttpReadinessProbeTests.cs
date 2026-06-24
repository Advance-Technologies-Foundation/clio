using System.Net;
using System.Net.Http;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// Unit tests for <see cref="CliogateHttpReadinessProbe"/>. They run against an in-process
/// stub <see cref="HttpMessageHandler"/> (no sockets, no Creatio, no network I/O), so they are
/// true unit tests that execute everywhere the unit suite runs and validate the
/// "serve on 2xx/401/403, retry on 404/3xx/5xx" polling contract locally.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class CliogateHttpReadinessProbeTests {
	private const string ProbeRoute = "rest/CreatioApiGateway/GetApiVersion";
	private const string BaseUri = "https://localhost/";

	[Test]
	[Description("Retries while the cliogate route returns 404 and succeeds once the route starts serving a 200 status.")]
	public async Task WaitUntilServingAsync_ShouldRetryThenSucceed_WhenRouteStops404AfterSeveralAttempts() {
		// Arrange
		const int notReadyResponses = 3;
		using SequencedHttpMessageHandler handler = SequencedHttpMessageHandler.NotReadyThenServing(
			notReadyResponses, HttpStatusCode.NotFound, HttpStatusCode.OK);
		ICliogateHttpReadinessProbe probe = CreateProbe(handler, maxAttempts: 10);

		// Act
		Func<Task> act = () => probe.WaitUntilServingAsync(BaseUri, ProbeRoute, CancellationToken.None);

		// Assert
		await act.Should().NotThrowAsync(
			because: "the probe must keep polling through transient 404 responses and succeed once the cliogate REST module starts serving");
		handler.RequestCount.Should().Be(notReadyResponses + 1,
			because: "the probe should stop polling on the first serving response, immediately after the configured number of 404s");
	}

	[Test]
	[Description("Throws a descriptive CliogateReadinessTimeoutException naming the probed route and last status when the route never stops returning 404.")]
	public async Task WaitUntilServingAsync_ShouldThrowDescriptiveTimeout_WhenRouteAlwaysReturns404() {
		// Arrange
		using SequencedHttpMessageHandler handler = SequencedHttpMessageHandler.AlwaysReturns(HttpStatusCode.NotFound);
		const int maxAttempts = 4;
		ICliogateHttpReadinessProbe probe = CreateProbe(handler, maxAttempts);

		// Act
		Func<Task> act = () => probe.WaitUntilServingAsync(BaseUri, ProbeRoute, CancellationToken.None);

		// Assert
		CliogateReadinessTimeoutException exception = (await act.Should().ThrowAsync<CliogateReadinessTimeoutException>(
			because: "an environment whose cliogate REST module never starts must surface a clear readiness failure rather than letting tests proceed against 404 routes")).Which;
		exception.LastStatusCode.Should().Be(HttpStatusCode.NotFound,
			because: "the exception must report the last observed HTTP status so the real readiness problem is visible");
		exception.ProbedUri.Should().Contain(ProbeRoute,
			because: "the exception message must name the cliogate route that was probed for diagnostics");
		exception.Message.Should().Contain("404",
			because: "the failure message must surface the 404 status that distinguishes a not-yet-serving REST module from a real success");
		handler.RequestCount.Should().Be(maxAttempts,
			because: "the probe should attempt exactly the configured number of times before giving up");
	}

	[Test]
	[Description("Treats a 401 Unauthorized as 'module is serving' and succeeds without authenticating.")]
	public async Task WaitUntilServingAsync_ShouldSucceed_WhenRouteReturnsUnauthorized() {
		// Arrange
		using SequencedHttpMessageHandler handler = SequencedHttpMessageHandler.AlwaysReturns(HttpStatusCode.Unauthorized);
		ICliogateHttpReadinessProbe probe = CreateProbe(handler, maxAttempts: 5);

		// Act
		Func<Task> act = () => probe.WaitUntilServingAsync(BaseUri, ProbeRoute, CancellationToken.None);

		// Assert
		await act.Should().NotThrowAsync(
			because: "a registered cliogate route returns 401 (not 404) to an unauthenticated probe, so the probe must treat 401 as 'serving' and need no credentials");
		handler.RequestCount.Should().Be(1,
			because: "the probe should stop on the first serving response without further retries");
	}

	[Test]
	[Description("Treats a 403 Forbidden as 'module is serving' because the no-auth GET can legitimately be rejected by a registered route.")]
	public async Task WaitUntilServingAsync_ShouldSucceed_WhenRouteReturnsForbidden() {
		// Arrange
		using SequencedHttpMessageHandler handler = SequencedHttpMessageHandler.AlwaysReturns(HttpStatusCode.Forbidden);
		ICliogateHttpReadinessProbe probe = CreateProbe(handler, maxAttempts: 5);

		// Act
		Func<Task> act = () => probe.WaitUntilServingAsync(BaseUri, ProbeRoute, CancellationToken.None);

		// Assert
		await act.Should().NotThrowAsync(
			because: "403 proves the route is registered and answering, so the probe must treat it as 'serving' rather than warming up");
		handler.RequestCount.Should().Be(1,
			because: "the probe should stop on the first serving response without further retries");
	}

	[Test]
	[Description("Retries while the route returns a transient 503 and succeeds only once it serves a 200, so an IIS/ANCM warm-up error is not mistaken for readiness.")]
	public async Task WaitUntilServingAsync_ShouldRetryThenSucceed_WhenRouteReturns503ThenServing() {
		// Arrange
		const int notReadyResponses = 2;
		using SequencedHttpMessageHandler handler = SequencedHttpMessageHandler.NotReadyThenServing(
			notReadyResponses, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
		ICliogateHttpReadinessProbe probe = CreateProbe(handler, maxAttempts: 10);

		// Act
		Func<Task> act = () => probe.WaitUntilServingAsync(BaseUri, ProbeRoute, CancellationToken.None);

		// Assert
		await act.Should().NotThrowAsync(
			because: "a transient 5xx during restart means 'not ready yet', so the probe must keep polling and succeed only when the route actually serves");
		handler.RequestCount.Should().Be(notReadyResponses + 1,
			because: "the probe should retry through the 503 warm-up responses and stop on the first serving status");
	}

	[Test]
	[Description("Keeps retrying on a persistent 503 and surfaces it as the last status, proving a 5xx is never accepted as 'serving'.")]
	public async Task WaitUntilServingAsync_ShouldKeepRetrying_WhenRouteAlwaysReturnsServerError() {
		// Arrange
		using SequencedHttpMessageHandler handler = SequencedHttpMessageHandler.AlwaysReturns(HttpStatusCode.ServiceUnavailable);
		const int maxAttempts = 3;
		ICliogateHttpReadinessProbe probe = CreateProbe(handler, maxAttempts);

		// Act
		Func<Task> act = () => probe.WaitUntilServingAsync(BaseUri, ProbeRoute, CancellationToken.None);

		// Assert
		CliogateReadinessTimeoutException exception = (await act.Should().ThrowAsync<CliogateReadinessTimeoutException>(
			because: "treating a transient 5xx as ready would reintroduce the false-positive ENG-92146 removes, so the probe must keep retrying and ultimately fail")).Which;
		exception.LastStatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
			because: "the exception must report the 503 so the warm-up failure is visible in diagnostics");
		handler.RequestCount.Should().Be(maxAttempts,
			because: "the probe should attempt exactly the configured number of times before giving up on a persistent 5xx");
	}

	[Test]
	[Description("Keeps retrying on a 302 redirect rather than treating a followed login page as 'serving'.")]
	public async Task WaitUntilServingAsync_ShouldKeepRetrying_WhenRouteAlwaysReturnsRedirect() {
		// Arrange
		using SequencedHttpMessageHandler handler = SequencedHttpMessageHandler.AlwaysReturns(HttpStatusCode.Redirect);
		const int maxAttempts = 3;
		ICliogateHttpReadinessProbe probe = CreateProbe(handler, maxAttempts);

		// Act
		Func<Task> act = () => probe.WaitUntilServingAsync(BaseUri, ProbeRoute, CancellationToken.None);

		// Assert
		CliogateReadinessTimeoutException exception = (await act.Should().ThrowAsync<CliogateReadinessTimeoutException>(
			because: "a 3xx is a redirect to a login/warm-up page, not proof the cliogate route answers, so the probe must keep retrying")).Which;
		exception.LastStatusCode.Should().Be(HttpStatusCode.Redirect,
			because: "the exception must report the 302 so the redirect-based false signal is visible in diagnostics");
		handler.RequestCount.Should().Be(maxAttempts,
			because: "the probe should attempt exactly the configured number of times before giving up on a persistent redirect");
	}

	[Test]
	[Description("Stops once the overall deadline is exceeded so an accept-then-hang host cannot stack per-request waits into a multi-minute arrange stall.")]
	public async Task WaitUntilServingAsync_ShouldStopOnOverallDeadline_WhenRouteNeverServesWithinBudget() {
		// Arrange
		using SequencedHttpMessageHandler handler = SequencedHttpMessageHandler.AlwaysReturns(HttpStatusCode.NotFound);
		ICliogateHttpReadinessProbe probe = new CliogateHttpReadinessProbe(
			new HttpClient(handler, disposeHandler: false),
			maxAttempts: 1000,
			delayBetweenAttempts: TimeSpan.FromMilliseconds(50),
			overallTimeout: TimeSpan.FromMilliseconds(20));

		// Act
		Func<Task> act = () => probe.WaitUntilServingAsync(BaseUri, ProbeRoute, CancellationToken.None);

		// Assert
		CliogateReadinessTimeoutException exception = (await act.Should().ThrowAsync<CliogateReadinessTimeoutException>(
			because: "the overall deadline must bound the worst case instead of letting the attempt budget run to completion")).Which;
		exception.Message.Should().Contain("deadline",
			because: "the failure message must explain that the overall readiness deadline, not the attempt count, ended the loop");
		handler.RequestCount.Should().BeLessThan(1000,
			because: "the deadline should cut the loop short well before the 1000-attempt budget is exhausted");
	}

	private static ICliogateHttpReadinessProbe CreateProbe(HttpMessageHandler handler, int maxAttempts) =>
		new CliogateHttpReadinessProbe(
			new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(5) },
			maxAttempts,
			delayBetweenAttempts: TimeSpan.FromMilliseconds(10));

	/// <summary>
	/// In-process <see cref="HttpMessageHandler"/> stub that mimics a cliogate route lifecycle:
	/// returns a "not ready" status for the first N calls and a "serving" status afterwards.
	/// No sockets and no real network, so it is OS-agnostic and runs in the unit tier.
	/// </summary>
	private sealed class SequencedHttpMessageHandler : HttpMessageHandler {
		private readonly int _notReadyResponses;
		private readonly HttpStatusCode _notReadyStatus;
		private readonly HttpStatusCode _servingStatus;
		private int _requestCount;

		private SequencedHttpMessageHandler(int notReadyResponses, HttpStatusCode notReadyStatus, HttpStatusCode servingStatus) {
			_notReadyResponses = notReadyResponses;
			_notReadyStatus = notReadyStatus;
			_servingStatus = servingStatus;
		}

		public int RequestCount => Volatile.Read(ref _requestCount);

		public static SequencedHttpMessageHandler NotReadyThenServing(
			int notReadyResponses, HttpStatusCode notReadyStatus, HttpStatusCode servingStatus) =>
			new(notReadyResponses, notReadyStatus, servingStatus);

		public static SequencedHttpMessageHandler AlwaysReturns(HttpStatusCode status) =>
			new(int.MaxValue, status, status);

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			cancellationToken.ThrowIfCancellationRequested();
			int currentCall = Interlocked.Increment(ref _requestCount);
			HttpStatusCode status = currentCall <= _notReadyResponses
				? _notReadyStatus
				: _servingStatus;
			return Task.FromResult(new HttpResponseMessage(status));
		}
	}
}
