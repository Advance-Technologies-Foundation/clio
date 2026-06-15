using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// Unit tests for <see cref="CdpSession"/> — the CDP frame-pump extracted from the launcher (Story 1).
/// Exercises send/receive, error frames, Runtime.evaluate, and loopback-only page-target discovery
/// through the <see cref="ICdpConnection"/> seam (no real browser).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class CdpSessionTests {

	private static CdpSession Build(ICdpConnection connection, HttpMessageHandler handler = null) {
		IHttpClientFactory httpFactory = Substitute.For<IHttpClientFactory>();
		httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler ?? new ThrowingHandler()));
		httpFactory.CreateClient().Returns(_ => new HttpClient(handler ?? new ThrowingHandler()));
		return new CdpSession(httpFactory, connection);
	}

	[Test]
	[Description("SendAsync returns the result frame whose id matches the issued command.")]
	public async Task SendAsync_ShouldReturnResultFrame_WhenMatchingIdArrives() {
		// Arrange
		FakeCdpConnection connection = new("""{"id":1,"result":{"ok":true}}""");
		CdpSession session = Build(connection);

		// Act
		JsonElement result = await session.SendAsync("Network.enable", new { });

		// Assert
		result.GetProperty("ok").GetBoolean().Should().BeTrue(
			because: "SendAsync must return the result payload of the matching response frame");
	}

	[Test]
	[Description("SendAsync skips interleaved CDP event frames and returns the matching response.")]
	public async Task SendAsync_ShouldSkipEventFrames_WhenInterleavedBeforeResult() {
		// Arrange — an event frame (no id) arrives before the real response.
		FakeCdpConnection connection = new(
			"""{"method":"Network.requestWillBeSent","params":{}}""",
			"""{"id":1,"result":{"done":true}}""");
		CdpSession session = Build(connection);

		// Act
		JsonElement result = await session.SendAsync("Page.navigate", new { url = "https://x" });

		// Assert
		result.GetProperty("done").GetBoolean().Should().BeTrue(
			because: "interleaved event frames without an id must be drained until the matching response arrives");
	}

	[Test]
	[Description("SendAsync throws when the matching frame carries a CDP error.")]
	public async Task SendAsync_ShouldThrow_WhenCdpErrorFrame() {
		// Arrange
		FakeCdpConnection connection = new("""{"id":1,"error":{"code":-32000,"message":"boom"}}""");
		CdpSession session = Build(connection);

		// Act
		Func<Task> act = () => session.SendAsync("Network.setCookie", new { });

		// Assert
		(await act.Should().ThrowAsync<InvalidOperationException>(
			because: "a CDP error frame must be surfaced as a hard failure")).WithMessage("*boom*");
	}

	[Test]
	[Description("EvaluateAsync issues Runtime.evaluate (honoring awaitPromise) and returns the awaited value.")]
	public async Task EvaluateAsync_ShouldIssueRuntimeEvaluateAndReturnValue_WhenSuccessful() {
		// Arrange
		FakeCdpConnection connection = new("""{"id":1,"result":{"result":{"type":"string","value":"hello"}}}""");
		CdpSession session = Build(connection);

		// Act
		JsonElement value = await session.EvaluateAsync("'hello'", awaitPromise: false);

		// Assert
		value.GetString().Should().Be("hello",
			because: "EvaluateAsync must return the Runtime.evaluate result.value");
		connection.LastSentText.Should().Contain("Runtime.evaluate",
			because: "EvaluateAsync must issue the Runtime.evaluate CDP method");
		connection.LastSentText.Should().Contain("\"awaitPromise\":false",
			because: "the awaitPromise flag must be forwarded to CDP as supplied");
	}

	[Test]
	[Description("EvaluateAsync throws when Runtime.evaluate reports exceptionDetails.")]
	public async Task EvaluateAsync_ShouldThrow_WhenExceptionDetailsPresent() {
		// Arrange
		FakeCdpConnection connection = new(
			"""{"id":1,"result":{"exceptionDetails":{"text":"ReferenceError: x is not defined"}}}""");
		CdpSession session = Build(connection);

		// Act
		Func<Task> act = () => session.EvaluateAsync("x");

		// Assert
		(await act.Should().ThrowAsync<InvalidOperationException>(
			because: "a runtime exception during evaluation must be surfaced")).WithMessage("*ReferenceError*");
	}

	[Test]
	[Description("ConnectAsync resolves the page target from the loopback /json endpoint and opens that WebSocket.")]
	public async Task ConnectAsync_ShouldUseLoopbackJsonEndpoint_WhenResolvingPageTarget() {
		// Arrange
		RecordingHandler handler = new(
			"""[{"type":"page","webSocketDebuggerUrl":"ws://127.0.0.1:9222/devtools/page/abc"}]""");
		FakeCdpConnection connection = new();
		CdpSession session = Build(connection, handler);

		// Act
		await session.ConnectAsync(9222);

		// Assert
		handler.LastUri.Should().NotBeNull();
		handler.LastUri!.Host.Should().Be("127.0.0.1",
			because: "the unauthenticated DevTools endpoint must only be reached over loopback (NFR-06)");
		handler.LastUri.AbsoluteUri.Should().Be("http://127.0.0.1:9222/json",
			because: "the page target is discovered from the local DevTools /json list");
		connection.ConnectedUrl!.AbsoluteUri.Should().Be("ws://127.0.0.1:9222/devtools/page/abc",
			because: "ConnectAsync must open the page target's WebSocket URL");
	}

	[Test]
	[Description("ConnectAsync propagates cancellation (the same failure mode the launcher raised before extraction).")]
	public async Task ConnectAsync_ShouldThrow_WhenCancelledBeforePageTarget() {
		// Arrange
		FakeCdpConnection connection = new();
		CdpSession session = Build(connection, new ThrowingHandler());
		using CancellationTokenSource cts = new();
		cts.Cancel();

		// Act
		Func<Task> act = () => session.ConnectAsync(9222, cts.Token);

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>(
			because: "a cancelled token while polling for a page target must propagate, as before extraction");
	}

	/// <summary>In-memory <see cref="ICdpConnection"/> that replays canned frames and records what was sent.</summary>
	private sealed class FakeCdpConnection : ICdpConnection {
		private readonly Queue<string> _frames;

		public FakeCdpConnection(params string[] frames) => _frames = new Queue<string>(frames);

		public bool IsOpen { get; private set; } = true;

		public Uri ConnectedUrl { get; private set; }

		public string LastSentText { get; private set; }

		public Task ConnectAsync(Uri url, CancellationToken ct) {
			ConnectedUrl = url;
			return Task.CompletedTask;
		}

		public Task SendTextAsync(string text, CancellationToken ct) {
			LastSentText = text;
			return Task.CompletedTask;
		}

		public Task<string> ReceiveTextAsync(CancellationToken ct) =>
			Task.FromResult(_frames.Count > 0 ? _frames.Dequeue() : string.Empty);

		public ValueTask DisposeAsync() {
			IsOpen = false;
			return ValueTask.CompletedTask;
		}
	}

	/// <summary>Records the requested URI and returns a canned body for the DevTools /json call.</summary>
	private sealed class RecordingHandler(string body) : HttpMessageHandler {
		public Uri LastUri { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
			LastUri = request.RequestUri;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
		}
	}

	/// <summary>Always throws, simulating a DevTools port that is not listening.</summary>
	private sealed class ThrowingHandler : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
			Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused (unit test)"));
	}
}
