using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>ENG-103330: NUL-terminated framing of Chromium's DevTools pipe, its 4 MB cap, and its end.</summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class PipeCdpTransportTests {

	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

	[Test]
	[Description("A sent message is its UTF-8 JSON followed by exactly one NUL byte.")]
	public async Task SendAsync_Should_Terminate_Each_Message_With_One_Nul() {
		// Arrange
		MemoryStream toBrowser = new();
		await using PipeCdpTransport transport = new(new ChunkedStream([]), toBrowser);

		// Act
		await transport.SendAsync("""{"id":1,"method":"Page.enable","params":{"q":"é"}}""", CancellationToken.None);
		await transport.SendAsync("""{"id":2}""", CancellationToken.None);

		// Assert
		byte[] expected = Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"Page.enable\",\"params\":{\"q\":\"é\"}}\0{\"id\":2}\0");
		toBrowser.ToArray().Should().Equal(expected, because: "Chromium splits commands on the NUL after each one");
	}

	[Test]
	[Description("Messages split across reads, several in one read, and a NUL that arrives alone are each returned whole and in order.")]
	public async Task ReceiveAsync_Should_Reassemble_Messages_Across_Reads() {
		// Arrange
		ChunkedStream fromBrowser = new([
			Bytes("""{"id":1,"res"""),
			Bytes("""ult":{}}"""),
			[0],
			Bytes("{\"id\":2}\0{\"method\":\"Page.loadEventFired\"}\0{\"id\""),
			Bytes(":3}\0")
		]);
		await using PipeCdpTransport transport = new(fromBrowser, new MemoryStream());

		// Act
		List<string> messages = [];
		for (string message = await Receive(transport); message is not null; message = await Receive(transport)) {
			messages.Add(message);
		}

		// Assert
		messages.Should().Equal(["""{"id":1,"result":{}}""", """{"id":2}""", """{"method":"Page.loadEventFired"}""", """{"id":3}"""],
			because: "the transport hands the session one whole message at a time, however the pipe chunked them");
	}

	[Test]
	[Description("A multi-byte UTF-8 character split across two reads is decoded once the message is whole.")]
	public async Task ReceiveAsync_Should_Decode_A_Character_Split_Across_Reads() {
		// Arrange
		byte[] message = Bytes("{\"v\":\"Привіт\"}\0");
		ChunkedStream fromBrowser = new([message[..7], message[7..]]);
		await using PipeCdpTransport transport = new(fromBrowser, new MemoryStream());

		// Act
		string received = await Receive(transport);

		// Assert
		received.Should().Be("{\"v\":\"Привіт\"}", because: "bytes are decoded only after the terminator");
	}

	[Test]
	[Description("A message over the cap becomes the reply-too-large sentinel with its leading id, and the next message is read normally.")]
	public async Task ReceiveAsync_Should_Replace_An_Oversized_Reply_And_Keep_Reading() {
		// Arrange
		string huge = "{\"id\":7,\"result\":{\"value\":\"" + new string('x', 300) + "\"}}";
		byte[] oversized = Bytes(huge + "\0");
		ChunkedStream fromBrowser = new([oversized[..100], oversized[100..250], oversized[250..], Bytes("{\"id\":8}\0")]);
		await using PipeCdpTransport transport = new(fromBrowser, new MemoryStream(), maxMessageBytes: 128);

		// Act
		string first = await Receive(transport);
		string second = await Receive(transport);

		// Assert
		JsonElement sentinel = JsonDocument.Parse(first).RootElement;
		sentinel.GetProperty("id").GetInt32().Should().Be(7, because: "the reply's command must fail at once, not time out");
		sentinel.TryGetProperty(PipeCdpTransport.ReplyTooLargeMarker, out _).Should().BeTrue(
			because: "the session recognises the sentinel by its marker");
		second.Should().Be("{\"id\":8}", because: "the rest of the oversized message is skipped up to its terminator only");
	}

	[Test]
	[Description("The real cap is the 4 MB the WebSocket transport had.")]
	public void MaxMessageBytes_Should_Be_4_MB() {
		// Arrange
		const int fourMegabytes = 4 * 1024 * 1024;

		// Act
		int cap = PipeCdpTransport.MaxMessageBytes;

		// Assert
		cap.Should().Be(fourMegabytes, because: "a page answer is small, and memory stays bounded whatever the browser sends");
	}

	[Test]
	[Description("The end of the pipe (the browser exited) is null, also when it cuts a message short, and stays null.")]
	public async Task ReceiveAsync_Should_Return_Null_When_The_Pipe_Closes() {
		// Arrange
		ChunkedStream fromBrowser = new([Bytes("{\"id\":1}\0{\"id\":2,\"res")]);
		await using PipeCdpTransport transport = new(fromBrowser, new MemoryStream());

		// Act
		string whole = await Receive(transport);
		string cut = await Receive(transport);
		string after = await Receive(transport);

		// Assert
		whole.Should().Be("{\"id\":1}", because: "the message before the end is complete");
		cut.Should().BeNull(because: "a message the browser died writing is never answered");
		after.Should().BeNull(because: "a closed pipe stays closed");
	}

	[Test]
	[Description("Over a session, a pipe that closes fails the pending command at once with CdpSessionClosedException.")]
	public async Task Session_Should_Fail_Fast_When_The_Pipe_Closes() {
		// Arrange
		ChunkedStream fromBrowser = new([], blockAfterChunks: true);
		await using CdpSession session = new(new PipeCdpTransport(fromBrowser, new MemoryStream()));
		Task<JsonElement> pending = session.SendAsync("Target.getTargets", null, CancellationToken.None);

		// Act
		fromBrowser.End();
		Func<Task> act = () => pending.WaitAsync(Wait);

		// Assert
		(await act.Should().ThrowAsync<CdpSessionClosedException>(because: "the browser is gone"))
			.Which.Message.Should().Contain("closed the DevTools connection", because: "the caller learns why");
	}

	[Test]
	[Description("A receive blocked on a pipe read returns to a cancelled caller at once, and the next receive takes over that same read instead of starting a second one.")]
	public async Task ReceiveAsync_Should_Honour_Cancellation_On_A_Blocked_Read() {
		// Arrange
		ChunkedStream fromBrowser = new([], blockAfterChunks: true);
		await using PipeCdpTransport transport = new(fromBrowser, new MemoryStream());
		using CancellationTokenSource cancel = new();
		Task<string> blocked = transport.ReceiveAsync(cancel.Token);

		// Act
		await cancel.CancelAsync();
		Func<Task> act = () => blocked.WaitAsync(Wait);
		await act.Should().ThrowAsync<OperationCanceledException>(because: "a disposing session must not wait for the browser");
		Task<string> next = transport.ReceiveAsync(CancellationToken.None);
		fromBrowser.Release(Bytes("{\"id\":1}\0"));
		string received = await next.WaitAsync(Wait);

		// Assert
		received.Should().Be("{\"id\":1}", because: "the read left running is the one the next receive completes");
		fromBrowser.ReadCalls.Should().Be(1, because: "two concurrent reads on one pipe would interleave its bytes");
	}

	private static Task<string> Receive(PipeCdpTransport transport) =>
		transport.ReceiveAsync(CancellationToken.None).WaitAsync(Wait);

	private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

	/// <summary>A read-only stream that returns the given chunks one read at a time, then either ends or blocks.</summary>
	private sealed class ChunkedStream(IEnumerable<byte[]> chunks, bool blockAfterChunks = false) : Stream {

		private readonly Queue<byte[]> _chunks = new(chunks);
		private readonly TaskCompletionSource<byte[]> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public int ReadCalls { get; private set; }

		public void End() => _gate.TrySetResult([]);

		public void Release(byte[] chunk) => _gate.TrySetResult(chunk);

		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
			ReadCalls++;
			byte[] chunk = _chunks.Count > 0 ? _chunks.Dequeue()
				: blockAfterChunks ? await _gate.Task.ConfigureAwait(false)
				: [];
			chunk.CopyTo(buffer, offset);
			return chunk.Length;
		}

		public override int Read(byte[] buffer, int offset, int count) =>
			ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
