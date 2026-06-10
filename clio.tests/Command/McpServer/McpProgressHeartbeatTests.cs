using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpProgressHeartbeatTests {

	private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(20);
	private static readonly TimeSpan StopGuard = TimeSpan.FromSeconds(5);

	[Test]
	[Category("Unit")]
	[Description("Emits at least one progress beat while the synchronous work blocks longer than the heartbeat interval, then returns the work result.")]
	public async Task RunWithBeatAsync_ShouldEmitBeats_WhenWorkExceedsInterval() {
		// Arrange
		int beatCount = 0;
		Func<int, Task> beat = _ => {
			Interlocked.Increment(ref beatCount);
			return Task.CompletedTask;
		};
		// Work blocks until it has observed at least one beat (capped) so the assertion is
		// deterministic on slow CI agents instead of relying on wall-clock timing.
		Func<int> work = () => {
			Stopwatch stopwatch = Stopwatch.StartNew();
			while (Volatile.Read(ref beatCount) < 1 && stopwatch.Elapsed < StopGuard) {
				Thread.Sleep(5);
			}

			return 99;
		};

		// Act
		int result = await McpProgressHeartbeat.RunWithBeatAsync(beat, work, CancellationToken.None, FastInterval);

		// Assert
		result.Should().Be(99,
			because: "the heartbeat wrapper must return the value produced by the synchronous work");
		beatCount.Should().BeGreaterThanOrEqualTo(1,
			because: "a synchronous operation that outlives the heartbeat interval must trigger at least one progress beat");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the exact value produced by the work delegate when a heartbeat sink is supplied.")]
	public async Task RunWithBeatAsync_ShouldReturnWorkResult_WhenWorkCompletes() {
		// Arrange
		Func<int, Task> beat = _ => Task.CompletedTask;
		Func<string> work = () => "done";

		// Act
		string result = await McpProgressHeartbeat.RunWithBeatAsync(beat, work, CancellationToken.None, FastInterval);

		// Assert
		result.Should().Be("done",
			because: "the wrapper must be transparent to the work result");
	}

	[Test]
	[Category("Unit")]
	[Description("Runs the work inline with no heartbeat when no MCP server is available, preserving the behavior clients without progress support see today.")]
	public async Task RunWithProgressAsync_ShouldRunInline_WhenServerIsNull() {
		// Arrange
		bool executed = false;
		Func<int> work = () => {
			executed = true;
			return 7;
		};

		// Act — server is null, so there is no progress token and the helper must not require one.
		int result = await McpProgressHeartbeat.RunWithProgressAsync(
			server: null,
			progressToken: null,
			operationName: "no-op",
			work: work,
			cancellationToken: CancellationToken.None);

		// Assert
		executed.Should().BeTrue(
			because: "the work must still execute when no heartbeat can be sent");
		result.Should().Be(7,
			because: "the no-heartbeat path must return the work result unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Propagates the work exception unchanged and stops the heartbeat once the work delegate throws.")]
	public async Task RunWithBeatAsync_ShouldPropagateException_AndStopHeartbeat_WhenWorkThrows() {
		// Arrange
		int beatCount = 0;
		Func<int, Task> beat = _ => {
			Interlocked.Increment(ref beatCount);
			return Task.CompletedTask;
		};
		// Wait for at least one beat so the heartbeat is provably running before the throw.
		Func<int> work = () => {
			Stopwatch stopwatch = Stopwatch.StartNew();
			while (Volatile.Read(ref beatCount) < 1 && stopwatch.Elapsed < StopGuard) {
				Thread.Sleep(5);
			}

			throw new InvalidOperationException("boom");
		};

		// Act
		Func<Task> act = async () =>
			await McpProgressHeartbeat.RunWithBeatAsync(beat, work, CancellationToken.None, FastInterval);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom").ConfigureAwait(false);
		int beatsAtThrow = Volatile.Read(ref beatCount);
		await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);
		Volatile.Read(ref beatCount).Should().Be(beatsAtThrow,
			because: "the heartbeat must stop once the work delegate has thrown — no beats may fire afterwards");
	}

	[Test]
	[Category("Unit")]
	[Description("Swallows heartbeat sink failures so a broken progress channel never breaks the tool execution.")]
	public async Task RunWithBeatAsync_ShouldSwallowBeatFailures_WhenBeatThrows() {
		// Arrange
		int beatAttempts = 0;
		Func<int, Task> throwingBeat = _ => {
			Interlocked.Increment(ref beatAttempts);
			throw new InvalidOperationException("sink down");
		};
		// Work blocks until the sink has been attempted at least once (capped).
		Func<bool> work = () => {
			Stopwatch stopwatch = Stopwatch.StartNew();
			while (Volatile.Read(ref beatAttempts) < 1 && stopwatch.Elapsed < StopGuard) {
				Thread.Sleep(5);
			}

			return true;
		};

		// Act
		bool result = await McpProgressHeartbeat.RunWithBeatAsync(throwingBeat, work, CancellationToken.None, FastInterval);

		// Assert
		result.Should().BeTrue(
			because: "a failing heartbeat sink must not surface from the tool — keep-alive is best-effort");
		beatAttempts.Should().BeGreaterThanOrEqualTo(1,
			because: "the heartbeat must keep attempting beats even after a sink failure");
	}
}
