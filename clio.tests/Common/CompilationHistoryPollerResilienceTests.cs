using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using Clio.CreatioModel;
using FluentAssertions;
using NSubstitute;
using NSubstitute.Core;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class CompilationHistoryPollerResilienceTests {

	#region Helpers

	/// <summary>
	/// A provider that fails or succeeds per round according to <c>shouldFail</c>, which receives the
	/// 1-based round number. Lets a test describe an exact failure PATTERN - a fixed run then a success,
	/// or alternating - rather than only the two extremes.
	/// </summary>
	private sealed class ScriptedDataProvider : IDataProvider {

		private readonly Func<int, bool> _shouldFail;
		private int _calls;

		internal ScriptedDataProvider(Func<int, bool> shouldFail) => _shouldFail = shouldFail;

		internal int Calls => _calls;

		/// <summary>Raised on a failing round, the way <see cref="ClassifyingDataProvider"/> surfaces one.</summary>
		internal string FailureMessage { get; set; } =
			"Failed reading records from entity schema 'VwCompilationHistory': timeout";

		/// <summary>Overrides what a failing round throws, for a test about the exception SHAPE.</summary>
		internal Func<Exception> FailWith { get; set; }

		public IItemsResponse GetItems(ISelectQuery selectQuery) {
			int call = ++_calls;
			if (!_shouldFail(call)) {
				return new SucceedingDataProvider().GetItems(selectQuery);
			}
			throw FailWith?.Invoke()
				?? new InvalidOperationException($"{FailureMessage} (round {call})");
		}

		public IDefaultValuesResponse GetDefaultValues(string schemaName) =>
			new SucceedingDataProvider().GetDefaultValues(schemaName);

		public IExecuteResponse BatchExecute(List<IBaseQuery> queries) =>
			new SucceedingDataProvider().BatchExecute(queries);

		public T GetSysSettingValue<T>(string sysSettingCode) => default;

		public bool GetFeatureEnabled(string featureCode) => true;

		public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) =>
			new SucceedingDataProvider().ExecuteProcess(request);
	}

	/// <summary>A clock that only moves when a test (or the fake wait below) moves it.</summary>
	private sealed class FakeTimeProvider : TimeProvider {

		private long _ticks = TimeSpan.TicksPerHour;

		public override long GetTimestamp() => _ticks;

		public override long TimestampFrequency => TimeSpan.TicksPerSecond;

		public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);

		internal void Advance(TimeSpan by) => _ticks += by.Ticks;
	}

	/// <summary>
	/// The wait seam, recorded rather than performed: each requested gap is remembered and the fake clock
	/// is moved by it, so the poll's real 90-second budget is exercised in microseconds. Optionally
	/// cancels after a given number of waits, which is how a test ends a poll that would otherwise run
	/// forever.
	/// </summary>
	private sealed class RecordingDelay : ICancellableDelay {

		private readonly FakeTimeProvider _timeProvider;
		private readonly CancellationTokenSource _cancelAfterWaits;
		private readonly int _waitsBeforeCancel;

		internal RecordingDelay(FakeTimeProvider timeProvider, CancellationTokenSource cancelAfterWaits = null,
			int waitsBeforeCancel = int.MaxValue) {
			_timeProvider = timeProvider;
			_cancelAfterWaits = cancelAfterWaits;
			_waitsBeforeCancel = waitsBeforeCancel;
		}

		internal List<TimeSpan> Waits { get; } = [];

		/// <summary>Set to cancel DURING a wait rather than after it, to model a wait cut short.</summary>
		internal bool CancelDuringWait { get; set; }

		public bool WaitOrCancelled(TimeSpan duration, CancellationToken ct) {
			Waits.Add(duration);
			if (CancelDuringWait) {
				//A FRACTION of the gap, not all of it: the point of this mode is a wait cut short, and
				//advancing the full duration would model a wait that completed and then happened to be
				//cancelled - which is a different thing and would hide a poll that ignores the token.
				_timeProvider.Advance(duration / 4);
				_cancelAfterWaits?.Cancel();
				return true;
			}
			_timeProvider.Advance(duration);
			if (Waits.Count >= _waitsBeforeCancel) {
				_cancelAfterWaits?.Cancel();
			}
			return ct.IsCancellationRequested;
		}
	}

	private static CompilationHistoryPoller CreatePoller(IDataProvider provider, ILogger logger,
		FakeTimeProvider timeProvider, ICancellableDelay delay,
		CompilationPollingOptions options = null) =>
		new(provider, logger, timeProvider, delay, options);

	#endregion

	[Test]
	[Description("A single failed round must not end the poll: before the classifying decorator an unreachable round came back as an empty list and the loop simply retried, and a compile that runs for minutes cannot be abandoned because one read timed out.")]
	public void Poll_ShouldContinue_AfterOneFailedRound() {
		// Arrange
		ScriptedDataProvider provider = new(round => round == 1);
		FakeTimeProvider clock = new();
		using CancellationTokenSource cts = new();
		RecordingDelay delay = new(clock, cts, waitsBeforeCancel: 2);
		CompilationHistoryPoller sut = CreatePoller(provider, Substitute.For<ILogger>(), clock, delay);

		// Act
		Action act = () => sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		act.Should().NotThrow(
			because: "one transient failure is not a reason to stop watching a compile, and an escaping exception on the poll thread would terminate the whole clio process");
		provider.Calls.Should().BeGreaterThanOrEqualTo(2,
			because: "the loop has to reach a second round for the tolerance to mean anything");
	}

	[Test]
	[Description("A run of failures that outlasts the give-up window does give up, and it does so by throwing so the caller can report the fault instead of polling silently until its own timeout.")]
	public void Poll_ShouldThrow_WhenRoundsKeepFailingPastTheGiveUpWindow() {
		// Arrange - never succeeds, so the window is the only thing that can end the poll.
		ScriptedDataProvider provider = new(_ => true);
		FakeTimeProvider clock = new();
		RecordingDelay delay = new(clock);
		CompilationHistoryPoller sut = CreatePoller(provider, Substitute.For<ILogger>(), clock, delay);
		using CancellationTokenSource cts = new();

		// Act
		Action act = () => sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
			because: "an environment that cannot answer at all must be reported, not waited on until the compile timeout expires").Which;
		exception.Message.Should().Contain("give-up window 90 s",
			because: "the operator has to be told how long the poll waited before it gave up, not just that it did");
		provider.Calls.Should().Be(21,
			because: "the 20th failure is observed at 88 s and is still inside the window, so the 21st - at 93 s - is the one that must throw; an earlier round means the comparison drifted and a compile is abandoned too soon");
		exception.Message.Should().Contain("21 failed rounds",
			because: "the round count is what tells an outage of a few slow rounds apart from a stand that answered nothing at all");
		exception.InnerException.Should().NotBeNull(
			because: "the last underlying failure is the only diagnosable detail available");
		exception.Message.Should().NotContain(exception.InnerException!.Message,
			because: "the last failure must be CHAINED, not interpolated: the CLI renderer drops a wrapper message that already quotes the text below it, which on a live stand deleted the elapsed time, the window and the round count from the only line the operator sees");
		exception.GetReadableMessageException().Should().Contain("gave up after",
			because: "this inner is an ordinary InvalidOperationException with no server detail of its own - the shape ClassifyingDataProvider produces when it rethrows a transport fault unchanged - and that arm of the renderer used to return the inner message alone, silently dropping the window and the round count");
		delay.Waits.Sum(wait => wait.TotalSeconds).Should().BeGreaterThanOrEqualTo(90,
			because: "the give-up is a DURATION: the poll must actually have spent the configured window failing, which a fixed round count could not guarantee once the gaps between rounds stopped being equal");
	}

	[Test]
	[Description("Cancellation still ends the poll promptly and without a fault, so the normal end-of-compile path is unaffected by the failure tolerance.")]
	public void Poll_ShouldReturnQuietly_WhenCancelled() {
		// Arrange
		FakeTimeProvider clock = new();
		CompilationHistoryPoller sut = CreatePoller(new SucceedingDataProvider(), Substitute.For<ILogger>(),
			clock, new RecordingDelay(clock));
		using CancellationTokenSource cts = new();
		cts.Cancel();

		// Act
		Action act = () => sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		act.Should().NotThrow(
			because: "cancellation is how a completed compile stops the poll, and it is not a failure");
	}

	[Test]
	[Description("A run of failures shorter than the window is tolerated even when it spans many rounds: 80 seconds of an unanswering app tier is an ordinary pause in a multi-minute compile, not a reason to stop watching it.")]
	public void Poll_ShouldTolerate_AFailingRunShorterThanTheWindow() {
		// Arrange - under the 1/2/5/5… backoff the N-th failure is observed at 5N-12 seconds, so the 20th
		// lands at 88 s: the last one still inside the 90 s window, one step short of the give-up.
		const int toleratedFailures = 20;
		ScriptedDataProvider provider = new(round => round <= toleratedFailures);
		FakeTimeProvider clock = new();
		using CancellationTokenSource cts = new();
		RecordingDelay delay = new(clock, cts, waitsBeforeCancel: toleratedFailures + 2);
		CompilationHistoryPoller sut = CreatePoller(provider, Substitute.For<ILogger>(), clock, delay);

		// Act
		Action act = () => sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		act.Should().NotThrow(
			because: "the run of failures stayed inside the 90 s window and the round after it succeeded");
		provider.Calls.Should().BeGreaterThan(toleratedFailures,
			because: "the loop has to reach the succeeding round for the tolerance to mean anything");
	}

	[Test]
	[Description("A successful round RESETS the window. Drop that reset - make the budget cumulative instead of per-run - and every other test here still passes, yet a long healthy compile with scattered timeouts would be aborted.")]
	public void Poll_ShouldResetTheWindow_AfterASuccessfulRound() {
		// Arrange - repeating blocks of nine failures then one success. The cumulative failing time passes
		// the 90 s window several times over; no single RUN of failures ever does.
		const int blockLength = 10;
		ScriptedDataProvider provider = new(round => round % blockLength != 0);
		FakeTimeProvider clock = new();
		using CancellationTokenSource cts = new();
		RecordingDelay delay = new(clock, cts, waitsBeforeCancel: blockLength * 4);
		CompilationHistoryPoller sut = CreatePoller(provider, Substitute.For<ILogger>(), clock, delay);

		// Act
		Action act = () => sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		act.Should().NotThrow(
			because: "no single run of failures reached the window, so a cumulative interpretation is the only thing that could have thrown here");
		delay.Waits.Sum(wait => wait.TotalSeconds).Should().BeGreaterThan(90,
			because: "the poll has to have spent more than one window's worth of time failing in total for the reset to be what kept it alive");
	}

	[Test]
	[Description("The failing rounds are spaced 1 s, 2 s, then 5 s apart and stay at 5 s, so a 90-second tolerance costs about twenty requests instead of the ninety an unbacked-off one-second cadence would fire at an environment that is already struggling.")]
	public void Poll_ShouldSpaceFailedRounds_OnTheConfiguredBackoff() {
		// Arrange
		ScriptedDataProvider provider = new(_ => true);
		FakeTimeProvider clock = new();
		using CancellationTokenSource cts = new();
		RecordingDelay delay = new(clock, cts, waitsBeforeCancel: 5);
		CompilationHistoryPoller sut = CreatePoller(provider, Substitute.For<ILogger>(), clock, delay);

		// Act
		sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		delay.Waits.Select(wait => wait.TotalSeconds).Should().Equal([1d, 2d, 5d, 5d, 5d],
			because: "the first two retries are quick because most outages are brief, and the rest settle at the last configured step rather than growing without bound");
	}

	[Test]
	[Description("A successful round returns to the ordinary one-second cadence: the backoff is a reaction to failure, not a permanent slowdown of a compile that is reporting progress again.")]
	public void Poll_ShouldReturnToTheNormalInterval_AfterASuccessfulRound() {
		// Arrange - fail, fail, then succeed for the rest.
		ScriptedDataProvider provider = new(round => round <= 2);
		FakeTimeProvider clock = new();
		using CancellationTokenSource cts = new();
		RecordingDelay delay = new(clock, cts, waitsBeforeCancel: 4);
		CompilationHistoryPoller sut = CreatePoller(provider, Substitute.For<ILogger>(), clock, delay);

		// Act
		sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		delay.Waits.Select(wait => wait.TotalSeconds).Should().Equal([1d, 2d, 1d, 1d],
			because: "the two failures back off 1 s and 2 s, and the rounds after the recovery are one second apart again");
	}

	[Test]
	[Description("Cancellation DURING a backoff wait ends the poll at once and without a fault, so a compile that finishes while the poll is backing off does not have to wait out the remaining five seconds.")]
	public void Poll_ShouldExitPromptly_WhenCancelledDuringBackoff() {
		// Arrange
		ScriptedDataProvider provider = new(_ => true);
		FakeTimeProvider clock = new();
		using CancellationTokenSource cts = new();
		RecordingDelay delay = new(clock, cts) { CancelDuringWait = true };
		CompilationHistoryPoller sut = CreatePoller(provider, Substitute.For<ILogger>(), clock, delay);

		// Act
		Action act = () => sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		act.Should().NotThrow(
			because: "a cancellation during a backoff is the compile ending, not a failure of the poll");
		delay.Waits.Should().ContainSingle(
			because: "the wait that was cut short must be the last thing the poll does - a further round would mean the cancellation was observed only after the full gap elapsed");
	}

	[Test]
	[Description("Every tolerated failed round is reported as a warning: without one nothing at all told the operator that the poll had stopped receiving answers, and silent progress lines look identical to a compile that has simply gone quiet.")]
	public void Poll_ShouldLogAWarning_PerToleratedRound() {
		// Arrange
		ScriptedDataProvider provider = new(_ => true);
		FakeTimeProvider clock = new();
		using CancellationTokenSource cts = new();
		RecordingDelay delay = new(clock, cts, waitsBeforeCancel: 3);
		ILogger logger = Substitute.For<ILogger>();
		CompilationHistoryPoller sut = CreatePoller(provider, logger, clock, delay);

		// Act
		sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning))
			.Should().Be(3,
				because: "each tolerated round is one line, and the round number plus the elapsed/window pair is what makes the lines read as one incident rather than as three unrelated faults");
	}

	[Test]
	[Description("The diagnostic must not copy server-derived text through unscrubbed: the failure message the data layer produces routinely carries the full request URI, and this line goes to a console and to CI logs.")]
	public void Poll_ShouldRedactServerDerivedText_InTheToleratedRoundWarning() {
		// Arrange
		ScriptedDataProvider provider = new(_ => true) {
			FailureMessage = "Failed reading from https://ts1-core-dev04:88/sae_secret/0/odata/VwCompilationHistory"
		};
		FakeTimeProvider clock = new();
		using CancellationTokenSource cts = new();
		RecordingDelay delay = new(clock, cts, waitsBeforeCancel: 1);
		ILogger logger = Substitute.For<ILogger>();
		CompilationHistoryPoller sut = CreatePoller(provider, logger, clock, delay);
		string warning = null;
		logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(call => warning ??= call.Arg<string>());

		// Act
		sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		warning.Should().NotBeNull(
			because: "the tolerated round has to be reported at all before its redaction can be judged");
		warning.Should().NotContain("ts1-core-dev04",
			because: "the environment host is exactly the kind of token UntrustedText.ForConsole exists to remove before a server-derived message reaches a log");
		warning.Should().Contain("[redacted-uri]",
			because: "the text is scrubbed rather than dropped, so the operator still sees that a read failed and roughly why");
		warning.Should().Contain("untrusted-source-text",
			because: "this warning is captured into the compile-creatio MCP result, and McpPassthroughRedaction only scrubs secrets - it never fences - so without the fence here server-authored prose reaches an agent's context as if clio had written it");
	}

	[Test]
	[Description("A round that failed only because the poll was cancelled mid-read is neither counted nor reported: issue #1377 means a cancelled read surfaces as an ordinary failure, and without this guard every normal end of a compile would print a spurious retry warning on its way out.")]
	public void Poll_ShouldNotReportAFailedRound_WhenItFailedAfterCancellation() {
		// Arrange - the read cancels the poll and then fails, which is what a read still in flight when the
		// compile finished does today.
		using CancellationTokenSource cts = new();
		ScriptedDataProvider provider = new(_ => {
			cts.Cancel();
			return true;
		});
		FakeTimeProvider clock = new();
		ILogger logger = Substitute.For<ILogger>();
		CompilationHistoryPoller sut = CreatePoller(provider, logger, clock, new RecordingDelay(clock));

		// Act
		Action act = () => sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		act.Should().NotThrow(
			because: "a read that lost its race with the end of the compile is not an outage");
		logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning))
			.Should().Be(0,
				because: "a round the poll itself ended is not an outage, and warning about it would put a spurious retry line at the end of every successful compile");
		provider.Calls.Should().Be(1,
			because: "the guard must be reached by an actual failed round - a poll that never queried would satisfy the assertion above for the wrong reason");
	}

	[Test]
	[Description("The backoff sequence repeats its last step rather than running off the end of the configured list, which is what keeps a long outage at a fixed five-second cadence instead of throwing an index error deep inside a background thread.")]
	public void BackoffFor_ShouldRepeatTheLastStep_BeyondTheConfiguredSequence() {
		// Arrange
		CompilationPollingOptions options = CompilationPollingOptions.Default;

		// Act
		TimeSpan[] observed = [
			options.BackoffFor(0), options.BackoffFor(1), options.BackoffFor(2), options.BackoffFor(3),
			options.BackoffFor(99)
		];

		// Assert
		observed.Select(step => step.TotalSeconds).Should().Equal([1d, 1d, 2d, 5d, 5d],
			because: "round zero is not a failure and takes the ordinary interval, the first three failures take the configured steps, and every later one holds at the last step");
	}

	[Test]
	[Description("The give-up exception must survive the CLI renderer: the last failure is chained rather than interpolated, because ExceptionReadableMessageExtension drops an outer message that already quotes its carrier's - which on a live stand deleted the window and the round count from the only line the operator sees.")]
	public void GiveUpException_ShouldStillCarryTheWindowAndRoundCount_WhenRenderedForTheConsole() {
		// Arrange - the provider fails the way ClassifyingDataProvider surfaces a rejected read, so the
		// give-up exception wraps a real server-detail carrier and takes the renderer's carrier path.
		const string carrierMessage =
			"Failed reading records from entity schema 'CompilationHistory': the environment answered "
			+ "with a non-JSON page where a DataService response was expected";
		ScriptedDataProvider provider = new(_ => true) { FailWith = () => new DataProviderFailureException(carrierMessage, serverDetail: "<html>login</html>") };
		FakeTimeProvider clock = new();
		CompilationHistoryPoller sut = CreatePoller(provider, Substitute.For<ILogger>(), clock,
			new RecordingDelay(clock));
		using CancellationTokenSource cts = new();

		// Act
		Exception thrown = Assert.Throws<InvalidOperationException>(
			() => sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { }));
		string rendered = thrown.GetReadableMessageException();

		// Assert
		rendered.Should().Contain("gave up after",
			because: "the line the consumer prints is this rendering, and it is the only place the operator learns that MONITORING stopped rather than that the compile failed");
		rendered.Should().Contain("90 s",
			because: "how long the poll waited is what tells a brief outage apart from a stand that answered nothing for a minute and a half");
		rendered.Should().Contain(carrierMessage,
			because: "chaining must not cost the underlying diagnosis either - both halves have to reach the same line");
	}
}
