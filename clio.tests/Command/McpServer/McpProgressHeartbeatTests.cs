using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
	public async Task RunWithProgressAsync_ShouldEmitBeats_WhenWorkExceedsInterval() {
		// Arrange — a fake sink channel counts and signals every heartbeat send.
		using ManualResetEventSlim beatReceived = new ManualResetEventSlim(false);
		int beatCount = 0;
		McpProgressHeartbeat.ProgressChannel channel = new(_ => {
			Interlocked.Increment(ref beatCount);
			beatReceived.Set();
			return Task.CompletedTask;
		});
		// Work blocks until at least one beat has been received — deterministic on any agent speed.
		Func<Action<string>, int> work = _ => {
			beatReceived.Wait(StopGuard);
			return 99;
		};

		// Act
		int result = await McpProgressHeartbeat.RunWithProgressAsync(channel, "op", work, CancellationToken.None, FastInterval);

		// Assert
		result.Should().Be(99,
			because: "the heartbeat wrapper must return the value produced by the synchronous work");
		beatCount.Should().BeGreaterThanOrEqualTo(1,
			because: "a synchronous operation that outlives the heartbeat interval must trigger at least one progress beat");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the exact value produced by the work delegate when a heartbeat sink channel is supplied.")]
	public async Task RunWithProgressAsync_ShouldReturnWorkResult_WhenWorkCompletes() {
		// Arrange
		McpProgressHeartbeat.ProgressChannel channel = new(_ => Task.CompletedTask);
		Func<Action<string>, string> work = _ => "done";

		// Act
		string result = await McpProgressHeartbeat.RunWithProgressAsync(channel, "op", work, CancellationToken.None, FastInterval);

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
	public async Task RunWithProgressAsync_ShouldPropagateException_AndStopHeartbeat_WhenWorkThrows() {
		// Arrange
		using ManualResetEventSlim beatReceived = new ManualResetEventSlim(false);
		int beatCount = 0;
		McpProgressHeartbeat.ProgressChannel channel = new(_ => {
			Interlocked.Increment(ref beatCount);
			beatReceived.Set();
			return Task.CompletedTask;
		});
		// Wait for at least one beat so the heartbeat is provably running before the throw.
		Func<Action<string>, int> work = _ => {
			beatReceived.Wait(StopGuard);
			throw new InvalidOperationException("boom");
		};

		// Act
		Func<Task> act = async () =>
			await McpProgressHeartbeat.RunWithProgressAsync(channel, "op", work, CancellationToken.None, FastInterval);

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
	public async Task RunWithProgressAsync_ShouldSwallowBeatFailures_WhenSinkThrows() {
		// Arrange — the fake sink throws on every send.
		using ManualResetEventSlim beatAttempted = new ManualResetEventSlim(false);
		int beatAttempts = 0;
		McpProgressHeartbeat.ProgressChannel channel = new(_ => {
			Interlocked.Increment(ref beatAttempts);
			beatAttempted.Set();
			throw new InvalidOperationException("sink down");
		});
		// Work blocks until the sink has been attempted at least once.
		Func<Action<string>, bool> work = _ => {
			beatAttempted.Wait(StopGuard);
			return true;
		};

		// Act
		bool result = await McpProgressHeartbeat.RunWithProgressAsync(channel, "op", work, CancellationToken.None, FastInterval);

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

	[Test]
	[Category("Unit")]
	[Description("Assigns a monotonically increasing progress sequence across sends so timer beats and stage markers sharing one channel never regress the Progress value.")]
	public async Task ProgressChannel_ShouldAssignMonotonicSequence_WhenMessagesSent() {
		// Arrange — a fake transport records every value the channel sends, in order.
		List<ModelContextProtocol.ProgressNotificationValue> sent = new();
		McpProgressHeartbeat.ProgressChannel channel = new(value => {
			sent.Add(value);
			return Task.CompletedTask;
		});

		// Act — three sends standing in for the two sources: a stage, a beat, another stage.
		await channel.SendAsync("1/2: create-entity UsrAlpha");
		await channel.SendAsync("operation is still running… (~1s elapsed)");
		await channel.SendAsync("2/2: create-lookup UsrBeta");

		// Assert
		sent.Select(value => value.Progress).Should().Equal([1f, 2f, 3f],
			because: "beats and stage markers must share one monotonically increasing counter so no client sees the progress value regress");
		sent.Select(value => value.Message).Should().Equal(
			["1/2: create-entity UsrAlpha", "operation is still running… (~1s elapsed)", "2/2: create-lookup UsrBeta"],
			because: "the channel must forward each message verbatim in send order");
	}

	[Test]
	[Category("Unit")]
	[Description("Assigns a gap-free 1..N progress sequence with no duplicates AND never overlaps two awaited transport sends when many sends race concurrently, proving the send gate serializes the awaited send rather than merely making the counter lock-free.")]
	public async Task ProgressChannel_ShouldAssignGapFreeSequence_WhenSendsAreConcurrent() {
		// Arrange — a thread-safe sink records every assigned Progress value under concurrent load and,
		// crucially, widens the send window with an await so a non-serialized transport would overlap.
		const int sendCount = 200;
		ConcurrentBag<float> assigned = new();
		int concurrent = 0;
		int overlapObserved = 0;
		McpProgressHeartbeat.ProgressChannel channel = new(async value => {
			int active = Interlocked.Increment(ref concurrent);
			if (active > 1) {
				// A gap-free counter alone would still allow the awaited send to overlap; the gate must not.
				Interlocked.Exchange(ref overlapObserved, 1);
			}

			assigned.Add(value.Progress);
			await Task.Yield();
			Interlocked.Decrement(ref concurrent);
		});

		// Act — fire all sends concurrently so they contend for the channel's send gate.
		await Task.WhenAll(Enumerable.Range(0, sendCount).Select(_ => channel.SendAsync("beat")));

		// Assert
		assigned.OrderBy(value => value).Should().Equal(
			Enumerable.Range(1, sendCount).Select(index => (float)index),
			because: "the send gate must hand out a gap-free 1..N sequence with no duplicates even under concurrent sends");
		overlapObserved.Should().Be(0,
			because: "the send gate must serialize the awaited transport send so at most one send is ever in flight, not just the counter increment");
	}

	[Test]
	[Category("Unit")]
	[Description("Cancels a queued ProgressChannel send when the gate is held by an in-flight (stalled) send, proving SendAsync passes its token to _sendGate.WaitAsync — so a wedged transport can never block a queued send (and therefore the response deadline) indefinitely (PR #837 N1).")]
	public async Task ProgressChannel_ShouldCancelQueuedSend_WhenGateHeldAndTokenCancelled() {
		// Arrange — the first send blocks inside the transport, holding the gate. A gate wait that ignored
		// the token (the pre-fix no-arg WaitAsync) would make the second send wait forever.
		using ManualResetEventSlim firstSendEntered = new ManualResetEventSlim(false);
		TaskCompletionSource releaseFirstSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		McpProgressHeartbeat.ProgressChannel channel = new(async _ => {
			firstSendEntered.Set();
			await releaseFirstSend.Task.ConfigureAwait(false);
		});
		Task blockingSend = Task.Run(() => channel.SendAsync("holds-the-gate"));
		firstSendEntered.Wait(StopGuard);
		using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

		// Act — the second send must wait on the held gate; cancelling the token must abort that wait.
		Task queuedSend = channel.SendAsync("queued-behind-the-gate", cancellationTokenSource.Token);
		await cancellationTokenSource.CancelAsync();
		Func<Task> act = async () => await queuedSend;

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>(
			because: "SendAsync must forward its token to _sendGate.WaitAsync so a queued send is cancellable while a stalled transport holds the gate");

		// Cleanup — release the first send so the gate-holding task completes.
		releaseFirstSend.SetResult();
		await blockingSend.ConfigureAwait(false);
	}

	[Test]
	[Category("Unit")]
	[Description("Interleaves a caller-pushed stage marker with a timer heartbeat on one channel, recording both messages under strictly increasing progress values.")]
	public async Task RunWithProgressAsync_ShouldInterleaveMarkerAndBeat_WhenWorkReportsThenWaits() {
		// Arrange — the sink signals separately once the marker lands and once a heartbeat lands, so the
		// work can block until BOTH have been recorded (the marker send is fire-and-forget).
		const string markerText = "1/1: create-entity UsrAlpha";
		using ManualResetEventSlim markerRecorded = new ManualResetEventSlim(false);
		using ManualResetEventSlim beatRecorded = new ManualResetEventSlim(false);
		object gate = new();
		List<ModelContextProtocol.ProgressNotificationValue> sent = new();
		McpProgressHeartbeat.ProgressChannel channel = new(value => {
			lock (gate) {
				sent.Add(value);
			}
			if (string.Equals(value.Message, markerText, StringComparison.Ordinal)) {
				markerRecorded.Set();
			} else if (value.Message?.Contains("still running", StringComparison.Ordinal) == true) {
				beatRecorded.Set();
			}
			return Task.CompletedTask;
		});
		Func<Action<string>, int> work = reportStage => {
			reportStage(markerText);
			markerRecorded.Wait(StopGuard);
			beatRecorded.Wait(StopGuard);
			return 5;
		};

		// Act
		int result = await McpProgressHeartbeat.RunWithProgressAsync(channel, "sync-schemas", work, CancellationToken.None, FastInterval);

		// Assert
		result.Should().Be(5,
			because: "the wrapper must return the work result even while markers and beats interleave");
		List<ModelContextProtocol.ProgressNotificationValue> snapshot;
		lock (gate) {
			snapshot = sent.ToList();
		}
		snapshot.Should().Contain(value => value.Message == markerText,
			because: "the caller-pushed stage marker must reach the channel");
		snapshot.Should().Contain(value => value.Message!.Contains("still running", StringComparison.Ordinal),
			because: "a timer heartbeat must also reach the same channel while the work waits");
		List<float> progresses = snapshot.Select(value => value.Progress).ToList();
		progresses.Should().BeInAscendingOrder(
			because: "the shared counter must never regress as markers and beats interleave");
		progresses.Should().OnlyHaveUniqueItems(
			because: "each interleaved send must take a distinct sequence value, so the progression is strictly increasing");
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards a caller-pushed stage message through the channel when a progress channel is present.")]
	public async Task BuildStageReporter_ShouldForwardStageMessage_WhenChannelPresent() {
		// Arrange — the reporter fires the send fire-and-forget, so signal when it lands.
		using ManualResetEventSlim reported = new ManualResetEventSlim(false);
		List<ModelContextProtocol.ProgressNotificationValue> sent = new();
		McpProgressHeartbeat.ProgressChannel channel = new(value => {
			sent.Add(value);
			reported.Set();
			return Task.CompletedTask;
		});
		Action<string> reportStage = McpProgressHeartbeat.BuildStageReporter(channel);

		// Act
		reportStage("3/9: create-entity UsrContact");

		// Assert
		reported.Wait(StopGuard).Should().BeTrue(
			because: "invoking the stage reporter must forward the message to the progress channel");
		sent.Should().ContainSingle().Which.Message.Should().Be("3/9: create-entity UsrContact",
			because: "the stage marker text must reach the wire unchanged so the client can show the current operation");
	}

	[Test]
	[Category("Unit")]
	[Description("Does nothing and never throws when the stage reporter is built without a progress channel (the client sent no progress token).")]
	public void BuildStageReporter_ShouldBeNoOp_WhenChannelIsNull() {
		// Arrange
		Action<string> reportStage = McpProgressHeartbeat.BuildStageReporter(channel: null);

		// Act
		Action act = () => reportStage("1/1: create-entity UsrThing");

		// Assert
		act.Should().NotThrow(
			because: "a progress-less client sends no token, so the stage reporter must be an inert no-op");
	}

	[Test]
	[Category("Unit")]
	[Description("Runs the work inline with a safe no-op reporter for the reporter overload when no MCP server is available, preserving the progress-less client path.")]
	public async Task RunWithProgressAsync_WithReporter_ShouldRunInline_WhenServerIsNull() {
		// Arrange
		bool executed = false;
		Func<Action<string>, int> work = reportStage => {
			reportStage("1/1: create-entity UsrThing"); // must be a safe no-op, not throw
			executed = true;
			return 11;
		};

		// Act
		int result = await McpProgressHeartbeat.RunWithProgressAsync(
			server: null,
			progressToken: null,
			operationName: "no-op",
			work: work,
			cancellationToken: CancellationToken.None);

		// Assert
		executed.Should().BeTrue(
			because: "the batch work must still run when no progress channel is available");
		result.Should().Be(11,
			because: "the reporter overload must return the work result unchanged on the no-progress path");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the work result for the deadline-bounded reporter overload when it completes within the deadline on the no-progress path.")]
	public async Task RunWithProgressAndDeadlineAsync_WithReporter_ShouldReturnResult_WhenWorkCompletes() {
		// Arrange
		Func<Action<string>, int> work = reportStage => {
			reportStage("creating section");
			return 42;
		};

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
			because: "the deadline reporter overload must return the work result when it finishes within the deadline");
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
