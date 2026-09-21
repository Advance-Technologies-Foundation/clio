using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Knowledge;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CuratedKnowledgeBackgroundRefreshTests {
	private ISettingsRepository _settings = null!;
	private IKnowledgeSourceManagementService _management = null!;
	private ILogger _logger = null!;
	private RefreshClock _clock = null!;
	private ScriptedDelay _delay = null!;

	[SetUp]
	public void SetUp() {
		_settings = Substitute.For<ISettingsRepository>();
		_management = Substitute.For<IKnowledgeSourceManagementService>();
		_logger = Substitute.For<ILogger>();
		_clock = new RefreshClock();
		_delay = new ScriptedDelay();
		_management.Update(
			Arg.Any<string>(),
			Arg.Any<CancellationToken>()).Returns(new KnowledgeSourceBatchResult(
				true,
				"Knowledge source 'creatio-curated' was updated.",
				[new KnowledgeSourceOperationResult(
					CuratedKnowledgeSourceDefaults.Alias, true, "updated", "updated")]));
	}

	[TearDown]
	public void TearDown() {
		_management.ClearReceivedCalls();
		_settings.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("A running MCP host updates knowledge when the shared autoupdate schedule is due, with no update-knowledge call from the operator.")]
	public async Task Start_ShouldUpdateKnowledge_WhenTheAutoupdateScheduleIsDue() {
		// Arrange
		ScheduleDueOn(1);

		// Act
		await RunTicks(1);

		// Assert
		_management.Received(1).Update(null, Arg.Any<CancellationToken>());
		_settings.Received().TryScheduleAutoupdate(
			AutoUpdateTarget.Knowledge,
			Arg.Any<DateTimeOffset>());
	}

	[Test]
	[Description("The first schedule check happens only after the start delay, so the startup path stays untouched.")]
	public async Task Start_ShouldWaitTheStartDelay_BeforeTheFirstScheduleCheck() {
		// Arrange
		ScheduleDueOn(1);
		_settings.When(repository => repository.TryScheduleAutoupdate(
			Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>())).Do(_ => _delay.RecordFirstCheck());

		// Act
		await RunTicks(1);

		// Assert
		_delay.Waits.Should().StartWith([
			TimeSpan.FromSeconds(CuratedKnowledgeSourceDefaults.BackgroundRefreshStartDelaySeconds)
		], because: "the refresh must not contribute anything to the pre-serve startup budget");
		_delay.WaitsBeforeFirstCheck.Should().Be(1,
			because: "nothing may consult the schedule, let alone the publisher, before that wait has elapsed");
	}

	[Test]
	[Description("An operator who disabled or postponed knowledge autoupdate gets no unattended publisher call.")]
	public async Task Start_ShouldNotUpdate_WhenTheScheduleIsDisabledOrNotYetDue() {
		// Arrange - a disabled or not-yet-due policy is exactly what TryScheduleAutoupdate answers false for.
		_settings.TryScheduleAutoupdate(
			AutoUpdateTarget.Knowledge, Arg.Any<DateTimeOffset>()).Returns(false);

		// Act
		await RunTicks(3);

		// Assert
		_management.DidNotReceive().Update(Arg.Any<string>(), Arg.Any<CancellationToken>());
		_settings.Received(3).TryScheduleAutoupdate(
			AutoUpdateTarget.Knowledge,
			Arg.Any<DateTimeOffset>());
	}

	[Test]
	[Description("Only the knowledge schedule is consulted; the clio self-update and toolkit schedules stay the CLI's business.")]
	public async Task Start_ShouldNeverTouchTheOtherUpdateSchedules() {
		// Arrange
		ScheduleDueOn(1);

		// Act
		await RunTicks(2);

		// Assert
		_settings.DidNotReceive().TryScheduleAutoupdate(
			AutoUpdateTarget.Clio, Arg.Any<DateTimeOffset>());
		_settings.DidNotReceive().TryScheduleAutoupdate(
			AutoUpdateTarget.Toolkit, Arg.Any<DateTimeOffset>());
	}

	[Test]
	[Description("The schedule keeps being polled after it fires once, so a release published later in the session still arrives.")]
	public async Task Start_ShouldKeepPolling_AfterTheScheduleFiredOnce() {
		// Arrange - due on the first and the third wake-up, not on the second.
		ScheduleDueOn(1, 3);

		// Act
		await RunTicks(3);

		// Assert
		_management.Received(2).Update(null, Arg.Any<CancellationToken>());
		_delay.Waits.Should().Contain(
			TimeSpan.FromMinutes(CuratedKnowledgeSourceDefaults.BackgroundRefreshPollIntervalMinutes),
			because: "the loop keeps the configured wake-up cadence between schedule checks");
	}

	[Test]
	[Description("A publisher that cannot be reached leaves the loop alive and the cached generation serving.")]
	public async Task Start_ShouldKeepRunning_WhenTheUpdateFails() {
		// Arrange
		ScheduleDueOn(1, 2);
		_management.Update(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(_ => throw new System.Net.Http.HttpRequestException("No such host is known."));

		// Act
		Task loop = await RunTicks(2);

		// Assert
		loop.IsCompletedSuccessfully.Should().BeTrue(
			because: "an offline host must keep serving its cached generation instead of faulting a background loop");
		_management.Received(2).Update(null, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A settings file that cannot be read pauses the refresh without killing the loop.")]
	public async Task Start_ShouldKeepRunning_WhenTheScheduleCannotBeRead() {
		// Arrange
		_settings.TryScheduleAutoupdate(AutoUpdateTarget.Knowledge, Arg.Any<DateTimeOffset>())
			.Returns(_ => throw new InvalidOperationException("appsettings.json is locked."));

		// Act
		Task loop = await RunTicks(2);

		// Assert
		loop.IsCompletedSuccessfully.Should().BeTrue(
			because: "a settings fault must not end the loop that would recover on the next wake-up");
		_management.DidNotReceive().Update(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Cancelling the host's shutdown token ends the loop without another schedule check.")]
	public async Task Start_ShouldStop_WhenTheHostShutsDown() {
		// Arrange
		ScheduleDueOn(1);
		using CancellationTokenSource shutdown = new();
		_delay.CancelOnWait(1, shutdown);

		// Act
		Task loop = NewRefresh().Start(shutdown.Token);
		await loop;

		// Assert
		loop.IsCompletedSuccessfully.Should().BeTrue(
			because: "shutdown ends the loop normally rather than leaving a faulted background task behind");
		_settings.DidNotReceive().TryScheduleAutoupdate(
			Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>());
	}

	[Test]
	[Description("A loop whose token is cancelled and whose source is then disposed still completes normally.")]
	public async Task Start_ShouldComplete_WhenTheCancelledSourceIsDisposedUnderIt() {
		// Arrange - the host's shutdown ordering: cancel, then dispose the using-scoped source.
		ScheduleDueOn(1);
		CancellationTokenSource shutdown = new();
		_delay.CancelOnWait(1, shutdown);
		Task loop = NewRefresh().Start(shutdown.Token);
		await loop;

		// Act
		Action dispose = shutdown.Dispose;

		// Assert
		dispose.Should().NotThrow(
			because: "the loop has already ended, so disposing the source cannot reach a parked wait");
		loop.IsCompletedSuccessfully.Should().BeTrue(
			because: "the loop must never leave an unobserved faulted task behind at shutdown");
	}

	private ICuratedKnowledgeBackgroundRefresh NewRefresh() =>
		new CuratedKnowledgeBackgroundRefresh(_settings, _management, _delay, _clock, _logger);

	/// <summary>Makes the autoupdate schedule report "due" on the given 1-based schedule checks.</summary>
	private void ScheduleDueOn(params int[] dueChecks) {
		HashSet<int> due = [.. dueChecks];
		int checks = 0;
		_settings.TryScheduleAutoupdate(AutoUpdateTarget.Knowledge, Arg.Any<DateTimeOffset>())
			.Returns(_ => due.Contains(++checks));
	}

	/// <summary>Runs the loop for <paramref name="ticks"/> refresh ticks, then cancels and awaits it.</summary>
	private async Task<Task> RunTicks(int ticks) {
		using CancellationTokenSource shutdown = new();
		// Wait 1 is the start delay, then one poll wait follows each tick: cancelling on the wait that
		// follows the last tick ends the loop deterministically, with no real time spent asleep.
		_delay.CancelOnWait(ticks + 1, shutdown);
		Task loop = NewRefresh().Start(shutdown.Token);
		await loop;
		return loop;
	}

	/// <summary>A clock the fixture controls, so the timestamp the schedule is asked about is exact.</summary>
	private sealed class RefreshClock : TimeProvider {
		internal DateTimeOffset UtcNow { get; } = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => UtcNow;
	}

	/// <summary>
	/// A wait that returns immediately and records what it was asked for, and cancels the host token on
	/// a chosen wait so the loop under test ends after a known number of ticks without sleeping.
	/// </summary>
	private sealed class ScriptedDelay : ICancellableDelay {
		private int _waits;
		private int _cancelOnWait = int.MaxValue;
		private CancellationTokenSource? _shutdown;

		internal List<TimeSpan> Waits { get; } = [];

		internal int WaitsBeforeFirstCheck { get; private set; } = -1;

		internal void CancelOnWait(int waitNumber, CancellationTokenSource shutdown) {
			_cancelOnWait = waitNumber;
			_shutdown = shutdown;
		}

		internal void RecordFirstCheck() {
			if (WaitsBeforeFirstCheck < 0) {
				WaitsBeforeFirstCheck = _waits;
			}
		}

		public bool WaitOrCancelled(TimeSpan duration, CancellationToken ct) {
			Waits.Add(duration);
			_waits++;
			if (_waits >= _cancelOnWait) {
				_shutdown?.Cancel();
				return true;
			}
			return ct.IsCancellationRequested;
		}
	}
}
