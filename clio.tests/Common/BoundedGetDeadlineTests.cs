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
		Stopwatch elapsed = Stopwatch.StartNew();

		// Act
		Func<Task> read = () => adapter.ExecuteGetRequestBoundedAsync(
			_prefix + "odata/Contact", Ceiling, DeadlineMs, CancellationToken.None);

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
	[Description("A client with no session cookies is declined instead of being sent through a form login, which is what broke OAuth/bearer environments before the OData GET was ever issued.")]
	public void BoundedGet_ShouldDecline_WhenTheClientHasNoSessionCookies() {
		// Arrange
		// No listener handler is started on purpose: the assertion is that nothing is sent at all.
		CreatioClient client = new(_prefix.TrimEnd('/'), "user", "password", true, true);
		using CreatioClientAdapter adapter = new(client);

		// Act
		Func<Task> read = () => adapter.ExecuteGetRequestBoundedAsync(
			_prefix + "odata/Contact", Ceiling, DeadlineMs, CancellationToken.None);

		// Assert
		// NotSupportedException specifically, because that is the value the caller's fallback is written
		// against: ODataReadToFileTool catches it and reissues the request through the buffered, fully
		// configured transport. Any other exception type would surface as a failed tool call instead.
		read.Should().ThrowAsync<NotSupportedException>(
				because: "authenticating here would be a second authentication path, and on a bearer client the "
					+ "only login it could reach is the form login, which fails")
			.GetAwaiter().GetResult();
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
