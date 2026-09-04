using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Relay;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95262 / R-11: unit coverage for the standard-output half of the worker output bound.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WorkerStdoutBoundedStreamTests {

	private const int SmallBound = 64;

	[Test]
	[Description("R-11: a worker message that exceeds the per-message bound stops the read with a named failure, so one runaway child cannot grow the parent that serves every other tenant.")]
	public async Task ReadAsync_ShouldFail_WhenOneMessageExceedsTheBound() {
		// Arrange - a single message with no delimiter anywhere in it: the runaway shape.
		using MemoryStream inner = new(Encoding.UTF8.GetBytes(new string('x', SmallBound * 4)));
		await using WorkerStdoutBoundedStream sut = new(inner, SmallBound);
		byte[] buffer = new byte[16];

		// Act
		Func<Task> read = async () => {
			while (await sut.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false) > 0) {
			}
		};

		// Assert
		await read.Should().ThrowAsync<IOException>(
			because: "the alternative is unbounded memory growth in the process that is serving every other session, and a named failure is what lets the relay report it as a relay failure rather than as an unhandled exception");
	}

	[Test]
	[Description("R-11: the bound is per MESSAGE, so a worker that sends many delimited messages whose TOTAL far exceeds the bound is read to the end.")]
	public async Task ReadAsync_ShouldReadEverything_WhenTheTotalExceedsTheBoundButNoSingleMessageDoes() {
		// Arrange - ten messages, each comfortably under the bound, ninety bytes of payload over it in total.
		StringBuilder payload = new();
		for (int index = 0; index < 10; index++) {
			payload.Append(new string('y', SmallBound / 2)).Append('\n');
		}
		byte[] bytes = Encoding.UTF8.GetBytes(payload.ToString());
		using MemoryStream inner = new(bytes);
		await using WorkerStdoutBoundedStream sut = new(inner, SmallBound);
		byte[] buffer = new byte[7];

		// Act
		int total = 0;
		int read;
		while ((read = await sut.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false)) > 0) {
			total += read;
		}

		// Assert
		total.Should().Be(bytes.Length,
			because: "a long-lived worker legitimately writes many megabytes across many messages, and a cumulative cap would kill healthy workers for doing exactly their job");
	}

	[Test]
	[Description("R-11: the accounting follows the bytes the reader actually received, so a message split across many small reads is measured as one message rather than as its chunks.")]
	public async Task ReadAsync_ShouldFail_WhenTheOversizedMessageArrivesInSmallChunks() {
		// Arrange - a buffer far smaller than the bound, so no single read can trip a naive per-read check.
		using MemoryStream inner = new(Encoding.UTF8.GetBytes(new string('z', SmallBound * 3)));
		await using WorkerStdoutBoundedStream sut = new(inner, SmallBound);
		byte[] buffer = new byte[4];

		// Act
		Func<Task> read = async () => {
			while (await sut.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false) > 0) {
			}
		};

		// Assert
		await read.Should().ThrowAsync<IOException>(
			because: "the SDK chooses its own read sizes, so a bound that only looked at one read at a time would be defeated by chunking rather than by anything the worker did differently");
	}

	[Test]
	[Description("An over-bound message is caught even when the read that carries it also carries the end of a previous message, because the reader chooses its own buffer size and a delimiter earlier in the buffer must not carry the remainder past the check.")]
	public async Task ReadAsync_ShouldFail_WhenTheOversizedMessageFollowsADelimiterInTheSameRead() {
		// Arrange - one read buffer larger than the bound, holding the tail of one message and the start
		// of a runaway.
		byte[] payload = Encoding.UTF8.GetBytes("done\n" + new string('q', SmallBound * 2));
		using MemoryStream inner = new(payload);
		await using WorkerStdoutBoundedStream sut = new(inner, SmallBound);
		byte[] buffer = new byte[payload.Length];

		// Act
		Func<Task> read = async () => {
			while (await sut.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false) > 0) {
			}
		};

		// Assert
		await read.Should().ThrowAsync<IOException>(
			because: "the bound must hold whatever the reader's buffer happens to contain, or a runaway is admitted by the accident of a message boundary landing in the same read");
	}

	[Test]
	[Description("The shipped bound is set far above the largest payload clio produces, so it can only ever fire on a runaway and never truncate a real answer.")]
	public void DefaultMaxMessageBytes_ShouldBeOrdersOfMagnitudeAboveTheLargestRealPayload() {
		// Arrange - the largest real payload measured in this repository is the live component registry
		// snapshot at roughly 598 KB; single-digit megabytes covers the largest responses the tool surface
		// can produce (schema hierarchies with bodies, package inventories on a large environment).
		const long largestObservedRealPayloadBytes = 8L * 1024 * 1024;

		// Act
		long bound = WorkerStdoutBoundedStream.DefaultMaxMessageBytes;

		// Assert
		bound.Should().BeGreaterThan(largestObservedRealPayloadBytes * 4,
			because: "a cap that fires on a working call converts this feature into a regression, which is a worse outcome than the memory it would save");
	}

	[Test]
	[Description("Driven through the SDK transport the relay actually uses: once a worker exceeds the bound the transport delivers nothing further, so a runaway cannot keep the parent reading and cannot smuggle later messages through behind it.")]
	public async Task Transport_ShouldDeliverNothingFurther_WhenTheWorkerExceedsTheBound() {
		// Arrange - an oversized first message, then a PERFECTLY VALID one. Without the bound the valid
		// message arrives; with it, the transport is finished. That second message is the whole
		// discriminator: a test that only asserted "the stream ended" would pass on end-of-file alone and
		// prove nothing about the bound. The SDK frames messages with a newline
		// (StreamClientSessionTransport reads lines), which is what the per-message reset depends on, so
		// driving the real transport pins that assumption where it would otherwise be inference.
		string runaway = new('r', SmallBound * 4);
		const string validMessage = """{"jsonrpc":"2.0","method":"notifications/progress","params":{}}""";
		using MemoryStream workerInput = new();
		using MemoryStream workerOutput = new(Encoding.UTF8.GetBytes($"{runaway}\n{validMessage}\n"));
		WorkerChildTransportOwner sut = new(SmallBound);
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));

		// Act
		int delivered = 0;
		await using ITransport transport = await sut.ConnectAsync(workerInput, workerOutput, timeout.Token);
		try {
			await foreach (JsonRpcMessage _ in transport.MessageReader.ReadAllAsync(timeout.Token)
				.ConfigureAwait(false)) {
				delivered++;
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException) {
			// A faulted stream is an acceptable outcome and a normal completion is too; what must NOT
			// happen is a hang, or a message arriving from behind the runaway.
		}

		// Assert
		delivered.Should().Be(0,
			because: "the bound must end the relay's reading of this worker, not merely skip the oversized message and carry on reading whatever follows it");
		timeout.IsCancellationRequested.Should().BeFalse(
			because: "the failure mode that matters is a HANG: a bound that stalls the read loop instead of ending it turns a runaway worker into a call nobody ever answers, which is the wedge this feature exists to remove");
	}

	[Test]
	[Description("Disposing the wrapper disposes the worker's pipe, because the transport disposes the streams it was handed and a wrapper that swallowed that would leak the pipe for the host's lifetime.")]
	public async Task DisposeAsync_ShouldDisposeTheWorkerStream() {
		// Arrange
		MemoryStream inner = new([1, 2, 3]);
		WorkerStdoutBoundedStream sut = new(inner, SmallBound);

		// Act
		await sut.DisposeAsync();

		// Assert
		inner.CanRead.Should().BeFalse(
			because: "the worker's pipe must close with the transport that owned it, or a host that relays thousands of calls accumulates one open handle per call");
	}
}
