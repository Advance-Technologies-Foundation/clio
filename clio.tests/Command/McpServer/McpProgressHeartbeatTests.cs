using System;
using System.IO;
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
		using ManualResetEventSlim beatReceived = new ManualResetEventSlim(false);
		int beatCount = 0;
		Func<int, Task> beat = _ => {
			Interlocked.Increment(ref beatCount);
			beatReceived.Set();
			return Task.CompletedTask;
		};
		// Work blocks until at least one beat has been received — deterministic on any agent speed.
		Func<int> work = () => {
			beatReceived.Wait(StopGuard);
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
		using ManualResetEventSlim beatReceived = new ManualResetEventSlim(false);
		int beatCount = 0;
		Func<int, Task> beat = _ => {
			Interlocked.Increment(ref beatCount);
			beatReceived.Set();
			return Task.CompletedTask;
		};
		// Wait for at least one beat so the heartbeat is provably running before the throw.
		Func<int> work = () => {
			beatReceived.Wait(StopGuard);
			throw new InvalidOperationException("boom");
		};

		// Act
		Func<Task> act = async () =>
			await McpProgressHeartbeat.RunWithBeatAsync(beat, work, CancellationToken.None, FastInterval);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom",
			because: "a work exception must propagate unchanged through the heartbeat wrapper").ConfigureAwait(false);
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
		using ManualResetEventSlim beatAttempted = new ManualResetEventSlim(false);
		int beatAttempts = 0;
		Func<int, Task> throwingBeat = _ => {
			Interlocked.Increment(ref beatAttempts);
			beatAttempted.Set();
			throw new InvalidOperationException("sink down");
		};
		// Work blocks until the sink has been attempted at least once.
		Func<bool> work = () => {
			beatAttempted.Wait(StopGuard);
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

	[Test]
	[Category("Unit")]
	[Description("Returns the work result when the deadline-bounded run completes within the response deadline.")]
	public async Task RunWithProgressAndDeadlineAsync_ShouldReturnResult_WhenWorkCompletesWithinDeadline() {
		// Arrange
		Func<int> work = () => 42;

		// Act
		int result = await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
			server: null,
			progressToken: null,
			operationName: "fast-op",
			work: work,
			deadline: TimeSpan.FromSeconds(5),
			cancellationToken: CancellationToken.None,
			interval: FastInterval);

		// Assert
		result.Should().Be(42,
			because: "work finishing before the deadline must return its value unchanged, like the synchronous path");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws McpResponseDeadlineExceededException when work outlives the deadline, yet leaves the work running to completion in the background.")]
	public async Task RunWithProgressAndDeadlineAsync_ShouldThrowAndKeepWorkRunning_WhenDeadlineElapses() {
		// Arrange — the work blocks on a gate the test opens only AFTER the deadline has fired, so
		// the work provably outlives the deadline without a wall-clock sleep (deterministic on any agent).
		using ManualResetEventSlim releaseWork = new ManualResetEventSlim(false);
		using ManualResetEventSlim workCompleted = new ManualResetEventSlim(false);
		Func<int> work = () => {
			releaseWork.Wait(StopGuard);
			workCompleted.Set();
			return 7;
		};

		// Act
		Func<Task> act = async () => await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
			server: null,
			progressToken: null,
			operationName: "slow-op",
			work: work,
			deadline: TimeSpan.FromMilliseconds(60),
			cancellationToken: CancellationToken.None,
			interval: FastInterval).ConfigureAwait(false);

		// Assert
		(await act.Should().ThrowAsync<McpResponseDeadlineExceededException>(
				because: "work that outlives the response deadline must surface the deadline exception, not the work result")
			.ConfigureAwait(false))
			.Which.OperationName.Should().Be("slow-op",
				because: "the deadline exception must name the operation so the tool can build an in-progress envelope");
		// The deadline has fired; now let the still-running work finish and confirm it was never cancelled.
		releaseWork.Set();
		workCompleted.Wait(StopGuard).Should().BeTrue(
			because: "the deadline must NOT cancel the work — it continues on the long-lived server so a later poll can observe the result");
	}

	[Test]
	[Category("Unit")]
	[Description("Propagates a work exception unchanged when the deadline-bounded work throws before the deadline elapses.")]
	public async Task RunWithProgressAndDeadlineAsync_ShouldPropagateException_WhenWorkThrowsBeforeDeadline() {
		// Arrange
		Func<int> work = () => throw new InvalidOperationException("boom");

		// Act
		Func<Task> act = async () => await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
			server: null,
			progressToken: null,
			operationName: "throwing-op",
			work: work,
			deadline: TimeSpan.FromSeconds(5),
			cancellationToken: CancellationToken.None,
			interval: FastInterval).ConfigureAwait(false);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom",
			because: "a work exception thrown before the deadline must propagate unchanged, not be masked as a deadline").ConfigureAwait(false);
	}

	[Test]
	[Category("Unit")]
	[Description("Propagates an OperationCanceledException instead of a fabricated deadline when the request is cancelled before the work completes, because the detached work does not survive server shutdown.")]
	public async Task RunWithProgressAndDeadlineAsync_ShouldThrowCancellation_WhenRequestCancelledBeforeWorkCompletes() {
		// Arrange — the work blocks on a gate that is never opened, so the only way the wait ends is
		// via cancellation, not completion (no wall-clock sleep, deterministic on any agent).
		using CancellationTokenSource cts = new CancellationTokenSource();
		using ManualResetEventSlim workStarted = new ManualResetEventSlim(false);
		using ManualResetEventSlim neverReleased = new ManualResetEventSlim(false);
		Func<int> work = () => {
			workStarted.Set();
			neverReleased.Wait(StopGuard);
			return 1;
		};

		// Act — cancel the request once the work is provably running, well inside the long deadline.
		Func<Task> act = async () => await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
			server: null,
			progressToken: null,
			operationName: "cancelled-op",
			work: work,
			deadline: TimeSpan.FromSeconds(30),
			cancellationToken: cts.Token,
			interval: FastInterval).ConfigureAwait(false);
		workStarted.Wait(StopGuard);
		cts.Cancel();

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>(
			because: "a cancelled request (or server shutdown) must surface as cancellation, not a fabricated 150 s deadline whose 'work continues, keep polling' guidance would be false").ConfigureAwait(false);
	}

	[Test]
	[Category("Unit")]
	[Description("Writes the unwrapped fault to stderr (and never crashes the process) when the detached work faults after the deadline already returned, turning the otherwise-silent post-deadline failure into a diagnostic trail.")]
	public async Task RunWithProgressAndDeadlineAsync_ShouldWriteFaultToStdErr_WhenBackgroundWorkFaultsAfterDeadline() {
		// Arrange — the work blocks on a gate the test opens only AFTER the deadline has fired, then
		// faults: the post-deadline background failure mode, reproduced without a wall-clock sleep.
		// Capture stderr so the otherwise-fire-and-forget diagnostic can be asserted deterministically.
		using ManualResetEventSlim releaseWork = new ManualResetEventSlim(false);
		TextWriter originalError = Console.Error;
		using StringWriterWithSignal captured = new StringWriterWithSignal();
		Console.SetError(captured);
		try {
			Func<int> work = () => {
				releaseWork.Wait(StopGuard);
				throw new InvalidOperationException("late boom");
			};

			// Act
			Func<Task> act = async () => await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
				server: null,
				progressToken: null,
				operationName: "faulting-bg-op",
				work: work,
				deadline: TimeSpan.FromMilliseconds(60),
				cancellationToken: CancellationToken.None,
				interval: FastInterval).ConfigureAwait(false);

			// Assert
			await act.Should().ThrowAsync<McpResponseDeadlineExceededException>(
				because: "the deadline elapses before the work faults, so the caller still receives the in-progress signal").ConfigureAwait(false);
			// The deadline has been reported; now let the detached work fault and confirm it is logged.
			releaseWork.Set();
			captured.Written.Wait(StopGuard).Should().BeTrue(
				because: "the post-deadline background fault must be written to stderr, not swallowed silently");
			captured.ToString().Should().Contain("faulting-bg-op",
				because: "the diagnostic must name the operation so the failure can be correlated");
			captured.ToString().Should().Contain("late boom",
				because: "the original fault detail must reach the stderr diagnostic trail");
		}
		finally {
			Console.SetError(originalError);
		}
	}

	// StringWriter that signals once anything has been written, so a test can wait for the
	// fire-and-forget background continuation deterministically instead of polling.
	private sealed class StringWriterWithSignal : StringWriter {
		public ManualResetEventSlim Written { get; } = new ManualResetEventSlim(false);

		public override void WriteLine(string value) {
			base.WriteLine(value);
			Written.Set();
		}

		protected override void Dispose(bool disposing) {
			if (disposing) {
				Written.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
