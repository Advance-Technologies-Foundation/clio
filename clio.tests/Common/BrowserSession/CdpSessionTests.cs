using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// ENG-103032, ENG-103330: the CDP session's correlation, event dispatch, flat-session routing and failure handling, over a
/// fake transport.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class CdpSessionTests {

	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

	// A transport whose incoming messages the test pushes, and which records what the session sent.
	private sealed class FakeTransport : ICdpTransport {
		private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();

		public ConcurrentQueue<JsonElement> Sent { get; } = new();

		public Func<JsonElement, string> Responder { get; set; }

		// Thrown by the next SendAsync, as a pipe that broke mid-send.
		public Exception SendException { get; set; }

		// Thrown by ReceiveAsync once set, as a failure the receive loop does not expect.
		public Exception ReceiveException { get; set; }

		public bool Disposed { get; private set; }

		public void Push(string message) => _incoming.Writer.TryWrite(message);

		public void Close() => _incoming.Writer.TryComplete();

		public Task SendAsync(string message, CancellationToken ct) {
			if (SendException is not null) {
				return Task.FromException(SendException);
			}
			JsonElement sent = JsonDocument.Parse(message).RootElement.Clone();
			Sent.Enqueue(sent);
			string reply = Responder?.Invoke(sent);
			if (reply is not null) {
				Push(reply);
			}
			return Task.CompletedTask;
		}

		public async Task<string> ReceiveAsync(CancellationToken ct) {
			if (ReceiveException is not null) {
				throw ReceiveException;
			}
			try {
				return await _incoming.Reader.ReadAsync(ct);
			}
			catch (ChannelClosedException) {
				return null;
			}
		}

		public ValueTask DisposeAsync() {
			Disposed = true;
			Close();
			return ValueTask.CompletedTask;
		}
	}

	[Test]
	[Description("Each command gets the answer carrying its own id, even when the browser answers out of order.")]
	public async Task SendAsync_Should_Correlate_Answers_By_Id() {
		// Arrange
		FakeTransport transport = new();
		await using CdpSession session = new(transport);

		// Act
		Task<JsonElement> first = session.SendAsync("A.first", null, CancellationToken.None);
		Task<JsonElement> second = session.SendAsync("B.second", new { x = 1 }, CancellationToken.None);
		await WaitForSentAsync(transport, 2);
		transport.Push("""{"id":2,"result":{"which":"second"}}""");
		transport.Push("""{"id":1,"result":{"which":"first"}}""");
		JsonElement firstResult = await first.WaitAsync(Wait);
		JsonElement secondResult = await second.WaitAsync(Wait);

		// Assert
		firstResult.GetProperty("which").GetString().Should().Be("first", because: "answer id 1 belongs to the first command");
		secondResult.GetProperty("which").GetString().Should().Be("second", because: "answer id 2 belongs to the second command");
		transport.Sent.ToArray()[1].GetProperty("params").GetProperty("x").GetInt32().Should().Be(1,
			because: "the parameters are sent as the params object");
	}

	[Test]
	[Description("A message without an id is an event: it is raised with its method and params, and a throwing subscriber does not stop the answers that follow.")]
	public async Task Events_Should_Be_Raised_And_A_Throwing_Handler_Should_Not_Stop_The_Loop() {
		// Arrange
		FakeTransport transport = new() { Responder = sent => "{\"id\":" + sent.GetProperty("id").GetInt32() + ",\"result\":{}}" };
		await using CdpSession session = new(transport);
		List<CdpEvent> seen = [];
		TaskCompletionSource received = new(TaskCreationOptions.RunContinuationsAsynchronously);
		session.EventReceived += (_, _) => throw new InvalidOperationException("subscriber bug");
		session.EventReceived += (_, cdpEvent) => {
			lock (seen) {
				seen.Add(cdpEvent);
			}
			received.TrySetResult();
		};

		// Act
		transport.Push("""{"method":"Runtime.exceptionThrown","params":{"exceptionDetails":{"text":"boom"}}}""");
		await received.Task.WaitAsync(Wait);
		JsonElement after = await session.SendAsync("Page.enable", null, CancellationToken.None).WaitAsync(Wait);

		// Assert
		seen.Should().ContainSingle(because: "one event was pushed").Which.Method.Should().Be("Runtime.exceptionThrown",
			because: "the event keeps its method");
		seen[0].Params.GetProperty("exceptionDetails").GetProperty("text").GetString().Should().Be("boom",
			because: "the event keeps its params");
		after.ValueKind.Should().Be(JsonValueKind.Object, because: "a failing subscriber must not stop later answers");
	}

	[Test]
	[Description("An error answer fails the command with CdpCommandException naming the method and the browser's message.")]
	public async Task SendAsync_Should_Throw_CdpCommandException_On_An_Error_Answer() {
		// Arrange
		FakeTransport transport = new() {
			Responder = sent => "{\"id\":" + sent.GetProperty("id").GetInt32()
				+ ",\"error\":{\"code\":-32000,\"message\":\"No node\"}}"
		};
		await using CdpSession session = new(transport);

		// Act
		Func<Task> act = () => session.SendAsync("DOM.focus", null, CancellationToken.None).WaitAsync(Wait);

		// Assert
		(await act.Should().ThrowAsync<CdpCommandException>(because: "the browser refused the command"))
			.Which.Message.Should().Contain("DOM.focus").And.Contain("No node", because: "the method and reason are named");
	}

	[Test]
	[Description("When the connection closes, a command still waiting fails at once, and a later command is refused.")]
	public async Task Close_Should_Fail_Pending_And_Later_Commands() {
		// Arrange
		FakeTransport transport = new();
		await using CdpSession session = new(transport);
		Task<JsonElement> pending = session.SendAsync("Page.navigate", new { url = "about:blank" }, CancellationToken.None);
		await WaitForSentAsync(transport, 1);

		// Act
		transport.Close();
		Func<Task> waitPending = () => pending.WaitAsync(Wait);
		await waitPending.Should().ThrowAsync<InvalidOperationException>(because: "the answer can never arrive");
		Func<Task> later = () => session.SendAsync("Page.enable", null, CancellationToken.None);

		// Assert
		(await later.Should().ThrowAsync<InvalidOperationException>(because: "the session is closed"))
			.Which.Message.Should().Contain("closed", because: "the reason is given");
	}

	[Test]
	[Description("A frame that is not JSON is ignored and the next answer still arrives.")]
	public async Task Dispatch_Should_Ignore_A_Malformed_Frame() {
		// Arrange
		FakeTransport transport = new();
		await using CdpSession session = new(transport);
		Task<JsonElement> command = session.SendAsync("Runtime.evaluate", null, CancellationToken.None);
		await WaitForSentAsync(transport, 1);

		// Act
		transport.Push("not json {");
		transport.Push("[1,2]");
		transport.Push("""{"id":1,"result":{"ok":true}}""");
		JsonElement result = await command.WaitAsync(Wait);

		// Assert
		result.GetProperty("ok").GetBoolean().Should().BeTrue(because: "a bad frame must not stop later answers");
	}

	[Test]
	[Description("An error answer whose message is not a string fails only that command; the session keeps answering.")]
	public async Task SendAsync_Should_Survive_An_Error_Frame_With_A_Non_String_Message() {
		// Arrange
		FakeTransport transport = new();
		await using CdpSession session = new(transport);
		Task<JsonElement> first = session.SendAsync("DOM.focus", null, CancellationToken.None);
		await WaitForSentAsync(transport, 1);

		// Act
		transport.Push("""{"id":1,"error":{"code":-32000,"message":42}}""");
		Func<Task> failed = () => first.WaitAsync(Wait);
		CdpCommandException exception = (await failed.Should().ThrowAsync<CdpCommandException>(
			because: "the browser refused the command")).Which;
		Task<JsonElement> second = session.SendAsync("Page.enable", null, CancellationToken.None);
		await WaitForSentAsync(transport, 2);
		transport.Push("""{"id":2,"result":{"ok":true}}""");
		JsonElement result = await second.WaitAsync(Wait);

		// Assert
		exception.Message.Should().Contain("42", because: "the raw error object is reported when its message is not text");
		result.GetProperty("ok").GetBoolean().Should().BeTrue(because: "one malformed error frame must not end the session");
	}

	[Test]
	[Description("A reply over the 4 MB read limit fails its own command at once with a reply-too-large error, and the session keeps answering.")]
	public async Task SendAsync_Should_Fail_The_Command_Whose_Reply_Was_Too_Large() {
		// Arrange
		FakeTransport transport = new();
		await using CdpSession session = new(transport);
		Task<JsonElement> first = session.SendAsync("Runtime.evaluate", null, CancellationToken.None);
		await WaitForSentAsync(transport, 1);
		byte[] prefix = System.Text.Encoding.UTF8.GetBytes("""{"id":1,"result":{"result":{"type":"string","value":"xxxxxxxx""");

		// Act
		transport.Push(PipeCdpTransport.OversizedMessageSentinel(prefix));
		Func<Task> failed = () => first.WaitAsync(Wait);
		CdpReplyTooLargeException exception = (await failed.Should().ThrowAsync<CdpReplyTooLargeException>(
			because: "the discarded reply belongs to this command, which must not wait for its timeout")).Which;
		Task<JsonElement> second = session.SendAsync("Page.enable", null, CancellationToken.None);
		await WaitForSentAsync(transport, 2);
		transport.Push("""{"id":2,"result":{"ok":true}}""");
		JsonElement result = await second.WaitAsync(Wait);

		// Assert
		exception.Message.Should().Contain("Runtime.evaluate").And.Contain("exceeded 4 MB",
			because: "the caller is told which command lost its reply and why");
		result.GetProperty("ok").GetBoolean().Should().BeTrue(because: "an oversized reply must not end the session");
	}

	[Test]
	[Description("An oversized message keeps the id that leads it; one without a leading id (an event) becomes an ignored empty object.")]
	public void OversizedMessageSentinel_Should_Keep_The_Leading_Id_Only() {
		// Arrange
		byte[] reply = System.Text.Encoding.UTF8.GetBytes("""  { "id" : 123456789, "result":{"value":"xxxx""");
		byte[] evt = System.Text.Encoding.UTF8.GetBytes("""{"method":"Network.dataReceived","params":{"id":5""");

		// Act
		string replySentinel = PipeCdpTransport.OversizedMessageSentinel(reply);
		string eventSentinel = PipeCdpTransport.OversizedMessageSentinel(evt);

		// Assert
		JsonElement parsed = JsonDocument.Parse(replySentinel).RootElement;
		parsed.GetProperty("id").GetInt32().Should().Be(123456789, because: "the reply's id is read from the kept prefix");
		parsed.TryGetProperty(PipeCdpTransport.ReplyTooLargeMarker, out _).Should().BeTrue(
			because: "the session recognises the sentinel by its marker");
		eventSentinel.Should().Be("{}", because: "an id inside params is not a reply id, and nobody waits for an event");
	}

	[Test]
	[Description("Once routed to an attached page, every command carries the page's session id, only that page's events are raised, and a reply is still matched by its id.")]
	public async Task RouteToSession_Should_Address_Commands_And_Filter_Events_By_Session() {
		// Arrange
		FakeTransport transport = new() {
			Responder = sent => $$"""{"id":{{sent.GetProperty("id").GetInt32()}},"result":{"ok":true},"sessionId":"S1"}"""
		};
		await using CdpSession session = new(transport);
		List<string> raised = [];
		session.EventReceived += (_, cdpEvent) => {
			lock (raised) {
				raised.Add(cdpEvent.Method);
			}
		};
		await session.SendAsync("Target.getTargets", null, CancellationToken.None).WaitAsync(Wait);

		// Act
		session.RouteToSession("S1");
		JsonElement answer = await session.SendAsync("Page.enable", null, CancellationToken.None).WaitAsync(Wait);
		transport.Push("""{"method":"Target.targetCreated","params":{}}""");
		transport.Push("""{"method":"Page.loadEventFired","params":{},"sessionId":"S2"}""");
		transport.Push("""{"method":"Runtime.consoleAPICalled","params":{},"sessionId":"S1"}""");
		await session.SendAsync("Runtime.enable", null, CancellationToken.None).WaitAsync(Wait);

		// Assert
		JsonElement[] sent = transport.Sent.ToArray();
		sent[0].TryGetProperty("sessionId", out _).Should().BeFalse(because: "a command before the attach goes to the browser");
		sent[1].GetProperty("sessionId").GetString().Should().Be("S1", because: "a routed command goes to the attached page");
		answer.GetProperty("ok").GetBoolean().Should().BeTrue(because: "the page's reply is matched by its id");
		lock (raised) {
			raised.Should().Equal(["Runtime.consoleAPICalled"],
				because: "browser-level events and another session's events are not this page's");
		}
	}

	[Test]
	[Description("The browser reporting the routed page detached closes the session: the pending command and every later one fail at once.")]
	public async Task DetachedFromTarget_Should_Close_A_Routed_Session() {
		// Arrange
		FakeTransport transport = new();
		await using CdpSession session = new(transport);
		session.RouteToSession("S1");
		Task<JsonElement> pending = session.SendAsync("Page.navigate", null, CancellationToken.None);
		await WaitForSentAsync(transport, 1);

		// Act
		transport.Push("""{"method":"Target.detachedFromTarget","params":{"sessionId":"S2"}}""");
		transport.Push("""{"method":"Target.detachedFromTarget","params":{"sessionId":"S1"}}""");
		Func<Task> waitPending = () => pending.WaitAsync(Wait);
		CdpSessionClosedException closed = (await waitPending.Should().ThrowAsync<CdpSessionClosedException>(
			because: "the page this command was for is gone")).Which;
		Func<Task> later = () => session.SendAsync("Runtime.enable", null, CancellationToken.None);

		// Assert
		closed.Message.Should().Contain("detached the page", because: "the caller is told why the session ended");
		await later.Should().ThrowAsync<CdpSessionClosedException>(because: "no later command can reach a detached page");
	}

	[Test]
	[Description("A cancelled wait throws OperationCanceledException and disposing the session disposes the transport.")]
	public async Task Cancel_And_Dispose_Should_Release_The_Transport() {
		// Arrange
		FakeTransport transport = new();
		CdpSession session = new(transport);
		using CancellationTokenSource cancel = new();

		// Act
		Task<JsonElement> command = session.SendAsync("Page.navigate", null, cancel.Token);
		await WaitForSentAsync(transport, 1);
		await cancel.CancelAsync();
		Func<Task> wait = () => command;
		await wait.Should().ThrowAsync<OperationCanceledException>(because: "the caller stopped waiting");
		await session.DisposeAsync();

		// Assert
		transport.Disposed.Should().BeTrue(because: "the session owns the transport");
	}

	[Test]
	[Description("A receive-loop failure outside the transport filter (a regex timeout, say) still closes the session: later commands fail at once, and DisposeAsync neither throws nor fails when called twice.")]
	public async Task ReceiveLoop_Should_Close_The_Session_On_An_Unexpected_Failure() {
		// Arrange
		FakeTransport transport = new() {
			ReceiveException = new System.Text.RegularExpressions.RegexMatchTimeoutException("x", "y", TimeSpan.FromSeconds(1))
		};
		CdpSession session = new(transport);
		DateTime until = DateTime.UtcNow + Wait;
		Exception refused = null;
		while (refused is null && DateTime.UtcNow < until) {
			try {
				await session.SendAsync("Page.enable", null, new CancellationTokenSource(TimeSpan.FromMilliseconds(200)).Token);
			}
			catch (CdpSessionClosedException exception) {
				refused = exception;
			}
			catch (OperationCanceledException) {
				// The loop had not failed yet; ask again.
			}
		}

		// Act
		Func<Task> dispose = async () => {
			await session.DisposeAsync();
			await session.DisposeAsync();
		};

		// Assert
		refused.Should().NotBeNull(because: "a session whose loop died must refuse commands instead of letting them wait out a timeout");
		refused!.Message.Should().Contain("stopped reading", because: "the reason names the failure");
		await dispose.Should().NotThrowAsync(because: "a faulted loop must not replace the caller's real error, and a second dispose is a no-op");
		transport.Disposed.Should().BeTrue(because: "the transport is still released");
	}

	[Test]
	[Description("A pipe that breaks while a command is sent (the browser exited) fails it with CdpSessionClosedException (the inner exception kept), not a raw IOException that slips past the callers' filters.")]
	public async Task SendAsync_Should_Wrap_A_Transport_Send_Failure() {
		// Arrange
		FakeTransport transport = new() { SendException = new System.IO.IOException("Broken pipe") };
		await using CdpSession session = new(transport);

		// Act
		Func<Task> send = () => session.SendAsync("Runtime.evaluate", null, CancellationToken.None);

		// Assert
		(await send.Should().ThrowAsync<CdpSessionClosedException>(because: "the session is gone for every caller alike"))
			.Which.InnerException.Should().BeOfType<System.IO.IOException>(
				because: "the transport's own failure is kept for diagnosis");
	}

	private static async Task WaitForSentAsync(FakeTransport transport, int count) {
		DateTime until = DateTime.UtcNow + Wait;
		while (transport.Sent.Count < count && DateTime.UtcNow < until) {
			await Task.Delay(10);
		}
		transport.Sent.Count.Should().BeGreaterThanOrEqualTo(count, because: "the commands must have been sent");
	}
}
