using System.Net;
using System.Net.Http;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// Unit tests for <see cref="CliogateHttpReadinessProbe"/>. They run against an in-process
/// <see cref="HttpListener"/> stub (no Creatio, no network), so they execute everywhere the
/// unit suite runs and validate the 404-until-ready polling contract locally.
/// </summary>
[TestFixture]
[Category("Unit")]
[NonParallelizable]
public sealed class CliogateHttpReadinessProbeTests {
	[Test]
	[Description("Retries while the cliogate route returns 404 and succeeds once the route starts serving a non-404 status.")]
	public async Task WaitUntilServingAsync_ShouldRetryThenSucceed_WhenRouteStops404AfterSeveralAttempts() {
		// Arrange
		const int notFoundResponses = 3;
		await using StubCliogateServer server = StubCliogateServer.StartReturning404Then200(notFoundResponses);
		using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
		ICliogateHttpReadinessProbe probe = new CliogateHttpReadinessProbe(
			httpClient,
			maxAttempts: 10,
			delayBetweenAttempts: TimeSpan.FromMilliseconds(10));

		// Act
		Func<Task> act = () => probe.WaitUntilServingAsync(server.BaseUri, StubCliogateServer.ProbeRoute, CancellationToken.None);

		// Assert
		await act.Should().NotThrowAsync(
			because: "the probe must keep polling through transient 404 responses and succeed once the cliogate REST module starts serving");
		server.RequestCount.Should().Be(notFoundResponses + 1,
			because: "the probe should stop polling on the first non-404 response, immediately after the configured number of 404s");
	}

	[Test]
	[Description("Throws a descriptive CliogateReadinessTimeoutException naming the probed route and last status when the route never stops returning 404.")]
	public async Task WaitUntilServingAsync_ShouldThrowDescriptiveTimeout_WhenRouteAlwaysReturns404() {
		// Arrange
		await using StubCliogateServer server = StubCliogateServer.StartAlwaysReturning(HttpStatusCode.NotFound);
		using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
		const int maxAttempts = 4;
		ICliogateHttpReadinessProbe probe = new CliogateHttpReadinessProbe(
			httpClient,
			maxAttempts,
			delayBetweenAttempts: TimeSpan.FromMilliseconds(10));

		// Act
		Func<Task> act = () => probe.WaitUntilServingAsync(server.BaseUri, StubCliogateServer.ProbeRoute, CancellationToken.None);

		// Assert
		CliogateReadinessTimeoutException exception = (await act.Should().ThrowAsync<CliogateReadinessTimeoutException>(
			because: "an environment whose cliogate REST module never starts must surface a clear readiness failure rather than letting tests proceed against 404 routes")).Which;
		exception.LastStatusCode.Should().Be(HttpStatusCode.NotFound,
			because: "the exception must report the last observed HTTP status so the real readiness problem is visible");
		exception.ProbedUri.Should().Contain(StubCliogateServer.ProbeRoute,
			because: "the exception message must name the cliogate route that was probed for diagnostics");
		exception.Message.Should().Contain("404",
			because: "the failure message must surface the 404 status that distinguishes a not-yet-serving REST module from a real success");
		server.RequestCount.Should().Be(maxAttempts,
			because: "the probe should attempt exactly the configured number of times before giving up");
	}

	[Test]
	[Description("Treats a non-404 status such as 401 Unauthorized as 'module is serving' and succeeds without authenticating.")]
	public async Task WaitUntilServingAsync_ShouldSucceed_WhenRouteReturnsUnauthorized() {
		// Arrange
		await using StubCliogateServer server = StubCliogateServer.StartAlwaysReturning(HttpStatusCode.Unauthorized);
		using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
		ICliogateHttpReadinessProbe probe = new CliogateHttpReadinessProbe(
			httpClient,
			maxAttempts: 5,
			delayBetweenAttempts: TimeSpan.FromMilliseconds(10));

		// Act
		Func<Task> act = () => probe.WaitUntilServingAsync(server.BaseUri, StubCliogateServer.ProbeRoute, CancellationToken.None);

		// Assert
		await act.Should().NotThrowAsync(
			because: "a registered cliogate route returns 401 (not 404) to an unauthenticated probe, so the probe must treat any non-404 status as 'serving' and need no credentials");
		server.RequestCount.Should().Be(1,
			because: "the probe should stop on the first non-404 response without further retries");
	}

	/// <summary>
	/// In-process HTTP stub that mimics a cliogate route lifecycle: returns 404 while "warming up"
	/// and a configurable status once "serving". Loopback-only, so it is OS-agnostic and needs no privileges.
	/// </summary>
	private sealed class StubCliogateServer : IAsyncDisposable {
		public const string ProbeRoute = "rest/CreatioApiGateway/GetApiVersion";

		private readonly HttpListener _listener;
		private readonly CancellationTokenSource _cts = new();
		private readonly Task _loop;
		private readonly int _notFoundResponses;
		private readonly HttpStatusCode _servingStatus;
		private int _requestCount;

		private StubCliogateServer(HttpListener listener, string baseUri, int notFoundResponses, HttpStatusCode servingStatus) {
			_listener = listener;
			BaseUri = baseUri;
			_notFoundResponses = notFoundResponses;
			_servingStatus = servingStatus;
			_loop = Task.Run(LoopAsync);
		}

		public string BaseUri { get; }

		public int RequestCount => Volatile.Read(ref _requestCount);

		public static StubCliogateServer StartReturning404Then200(int notFoundResponses) =>
			Start(notFoundResponses, HttpStatusCode.OK);

		public static StubCliogateServer StartAlwaysReturning(HttpStatusCode status) =>
			Start(status == HttpStatusCode.NotFound ? int.MaxValue : 0, status);

		private static StubCliogateServer Start(int notFoundResponses, HttpStatusCode servingStatus) {
			for (int attempt = 0; attempt < 10; attempt++) {
				int port = Random.Shared.Next(20000, 60000);
				HttpListener listener = new();
				listener.Prefixes.Add($"http://127.0.0.1:{port}/");
				try {
					listener.Start();
					return new StubCliogateServer(listener, $"http://127.0.0.1:{port}/", notFoundResponses, servingStatus);
				} catch (HttpListenerException) {
					// Port collision — try another one.
				}
			}

			throw new InvalidOperationException("Unable to start the stub cliogate server on a free loopback port.");
		}

		private async Task LoopAsync() {
			while (!_cts.IsCancellationRequested) {
				HttpListenerContext context;
				try {
					context = await _listener.GetContextAsync().WaitAsync(_cts.Token);
				} catch (Exception) {
					return;
				}

				int currentCall = Interlocked.Increment(ref _requestCount);
				HttpStatusCode status = currentCall <= _notFoundResponses
					? HttpStatusCode.NotFound
					: _servingStatus;
				context.Response.StatusCode = (int)status;
				context.Response.Close();
			}
		}

		public async ValueTask DisposeAsync() {
			await _cts.CancelAsync();
			try {
				_listener.Stop();
				_listener.Close();
			} catch (ObjectDisposedException) {
				// Listener already closed — cleanup must not hide assertion failures.
			} catch (HttpListenerException) {
				// Listener already stopped — cleanup must not hide assertion failures.
			}

			try {
				await _loop;
			} catch (OperationCanceledException) {
				// Loop exit after cancellation is expected.
			}

			_cts.Dispose();
		}
	}
}
