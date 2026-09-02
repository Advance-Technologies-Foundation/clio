namespace Clio.Tests.Common;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Creatio.Client;
using FluentAssertions;
using NUnit.Framework;

/// <summary>
/// Proves the streamed GET is bounded by its own deadline once the response headers have arrived.
/// </summary>
/// <remarks>
/// This is the case <c>HttpClient.Timeout</c> does NOT cover. With <c>ResponseHeadersRead</c> the transport's
/// timeout stops governing the operation the moment the headers are read, so a server that answers the
/// headers immediately and then withholds the body leaves the body reads bounded only by the caller's token -
/// and an MCP host is not guaranteed to deliver cancellation at all. Without the linked deadline this test
/// hangs rather than fails, which is exactly why it exists.
/// </remarks>
[TestFixture]
[Property("Module", "Common")]
internal sealed class BoundedGetDeadlineTests {

	private const int DeadlineMs = 500;
	private const long Ceiling = 64L * 1024 * 1024;

	// A safety net on the CALLER side, an order of magnitude above the production deadline. Without the
	// linked deadline the stalled body would never return, and the regression would be a CI job hang rather
	// than a failure; with this token it surfaces as caller cancellation and the TimeoutException assertion
	// fails promptly. It must never be the thing that ends a passing run - hence the wide margin.
	private const int CallerSafetyMs = 10_000;

	private HttpListener _listener;
	private string _prefix;
	private CancellationTokenSource _stall;

	[SetUp]
	public void SetUp() {
		_stall = new CancellationTokenSource();
		_prefix = $"http://127.0.0.1:{FreePort()}/";
		_listener = new HttpListener();
		_listener.Prefixes.Add(_prefix);
		_listener.Start();
	}

	[TearDown]
	public void TearDown() {
		_stall.Cancel();
		_stall.Dispose();
		try {
			_listener.Stop();
		}
		catch (ObjectDisposedException) {
			// Already torn down; a listener that is gone must not fail the test.
		}
		((IDisposable)_listener).Dispose();
	}

	[Test]
	[Category("Integration")]
	[Description("Sends the response headers, then withholds the body: the streamed GET must end on its own deadline as a TimeoutException rather than waiting on the caller's token.")]
	public void BoundedGet_ShouldFailOnItsOwnDeadline_WhenHeadersArriveAndTheBodyStalls() {
		// Arrange
		Task serve = ServeHeadersThenStallAsync();
		using CreatioClientAdapter adapter = CookieAuthenticatedAdapter();
		using CancellationTokenSource callerSafety = new(CallerSafetyMs);
		Stopwatch elapsed = Stopwatch.StartNew();

		// Act
		Func<Task> read = () => adapter.ExecuteGetRequestBoundedAsync(
			_prefix + "odata/Contact", Ceiling, DeadlineMs, callerSafety.Token);

		// Assert
		read.Should().ThrowAsync<TimeoutException>(
				because: "the deadline expiring is the server failing to deliver in time, which is a different "
					+ "outcome from the caller withdrawing the request")
			.GetAwaiter().GetResult();
		elapsed.Stop();
		elapsed.ElapsedMilliseconds.Should().BeLessThan(DeadlineMs * 20,
			because: "the deadline has to end the body read, not merely be recorded somewhere");
		_stall.Cancel();
		WaitQuietly(serve);
	}

	[Test]
	[Category("Integration")]
	[Description("Cancelling the caller's token during the stalled body surfaces as cancellation, NOT as the deadline's TimeoutException, so the two causes stay distinguishable.")]
	public void BoundedGet_ShouldReportCallerCancellation_AsCancellation_NotAsTheDeadline() {
		// Arrange
		Task serve = ServeHeadersThenStallAsync();
		using CreatioClientAdapter adapter = CookieAuthenticatedAdapter();
		using CancellationTokenSource caller = new();

		// Act
		Task<byte[]> read = adapter.ExecuteGetRequestBoundedAsync(
			_prefix + "odata/Contact", Ceiling, requestTimeout: 60_000, cancellationToken: caller.Token);
		caller.CancelAfter(200);

		// Assert
		Func<Task> awaiting = () => read;
		awaiting.Should().ThrowAsync<OperationCanceledException>(
				because: "a caller that withdraws its request has not experienced a server-side timeout, and "
					+ "reporting one would send the caller looking for a slow server")
			.GetAwaiter().GetResult();
		_stall.Cancel();
		WaitQuietly(serve);
	}

	[Test]
	[Category("Integration")]
	[Description("A client with no exported session cookies still issues the request through the configured transport, instead of being declined and pushed onto an unbounded buffered fallback.")]
	public void BoundedGet_ShouldUseTheConfiguredTransport_WhenTheClientHasNoSessionCookies() {
		// Arrange - no cookies are imported, which is the permanent state of an OAuth/bearer client. The
		// listener answers the headers and then stalls, so the only way the assertion below can be reached
		// is if the request was actually sent.
		Task serve = ServeHeadersThenStallAsync();
		CreatioClient client = new(_prefix.TrimEnd('/'), "user", "password", true, true);
		using CreatioClientAdapter adapter = new(client);
		using CancellationTokenSource callerSafety = new(CallerSafetyMs);

		// Act
		Func<Task> read = () => adapter.ExecuteGetRequestBoundedAsync(
			_prefix + "odata/Contact", Ceiling, DeadlineMs, callerSafety.Token);

		// Assert
		// The earlier implementation threw NotSupportedException here without sending anything, and
		// ODataReadToFileTool caught it and reissued the request through the fully buffered path - which
		// defeats the byte ceiling on exactly the environments that cannot use cookies. Reaching the
		// deadline instead proves the request went out on the configured, authenticated client.
		read.Should().ThrowAsync<TimeoutException>(
				because: "file mode has no buffered fallback any more, so a cookie-less client must be able to "
					+ "stream through the transport it is configured with")
			.GetAwaiter().GetResult();
		_stall.Cancel();
		WaitQuietly(serve);
	}

	// Cookies are imported rather than obtained by logging in: the deadline is a property of the streamed GET,
	// and driving a real login first would make the test depend on the authentication handshake it is not about.
	private CreatioClientAdapter CookieAuthenticatedAdapter() {
		CreatioClient client = new(_prefix.TrimEnd('/'), "user", "password", true, true);
		List<CreatioSessionCookie> cookies = [
			new("BPMCSRF", "token", "127.0.0.1", "/", true, false, "Lax", DateTime.MinValue)
		];
		client.ImportSessionCookies(cookies);
		return new CreatioClientAdapter(client);
	}

	private Task ServeHeadersThenStallAsync() => Task.Run(async () => {
		try {
			HttpListenerContext context = await _listener.GetContextAsync().ConfigureAwait(false);
			context.Response.StatusCode = 200;
			context.Response.ContentType = "application/json";
			// Chunked, with one byte written and flushed: that puts the headers and the first chunk on the wire
			// so the client has definitely passed ResponseHeadersRead, while the body stays unfinished.
			context.Response.SendChunked = true;
			context.Response.OutputStream.WriteByte((byte)'{');
			await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
			await Task.Delay(Timeout.Infinite, _stall.Token).ConfigureAwait(false);
		}
		catch (Exception) {
			// The listener is torn down while this is parked by design; the assertions live in the test body.
		}
	});

	private static void WaitQuietly(Task task) {
		try {
			task.Wait(TimeSpan.FromSeconds(5));
		}
		catch (AggregateException) {
			// The serving task is cancelled on purpose at the end of every case.
		}
	}

	private static int FreePort() {
		System.Net.Sockets.TcpListener probe = new(IPAddress.Loopback, 0);
		probe.Start();
		int port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();
		return port;
	}

}
