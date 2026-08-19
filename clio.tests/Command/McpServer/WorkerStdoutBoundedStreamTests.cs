using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Relay;
using FluentAssertions;
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
