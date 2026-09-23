using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer;
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
public sealed class KnowledgeRefreshTriggerTests {
	private ISettingsRepository _settings = null!;
	private IKnowledgeSourceManagementService _management = null!;
	private ILogger _logger = null!;
	private MutableClock _clock = null!;

	[SetUp]
	public void SetUp() {
		_settings = Substitute.For<ISettingsRepository>();
		_management = Substitute.For<IKnowledgeSourceManagementService>();
		_logger = Substitute.For<ILogger>();
		_clock = new MutableClock();
		_settings.TryScheduleAutoupdate(
			AutoUpdateTarget.Knowledge, Arg.Any<DateTimeOffset>()).Returns(true);
		_management.Update(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SuccessResult());
	}

	[TearDown]
	public void TearDown() {
		_settings.ClearReceivedCalls();
		_management.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("The first guidance read refreshes knowledge, so an MCP-only installation stops being stuck on its first generation.")]
	public async Task TriggerIfDue_ShouldRefresh_OnTheFirstRead() {
		// Arrange
		IKnowledgeRefreshTrigger trigger = NewTrigger();

		// Act
		await AwaitRefresh(trigger);

		// Assert
		_management.Received(1).Update(null, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Every enabled source is refreshed, exactly as the CLI autoupdate path does, not just the built-in one.")]
	public async Task TriggerIfDue_ShouldRefreshEveryEnabledSource() {
		// Arrange
		IKnowledgeRefreshTrigger trigger = NewTrigger();

		// Act
		await AwaitRefresh(trigger);

		// Assert
		_management.Received(1).Update(
			Arg.Is<string>(alias => alias == null),
			Arg.Any<CancellationToken>());
		_management.DidNotReceive().Update(
			Arg.Is<string>(alias => alias != null),
			Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Guidance reads inside the probe interval cost nothing: the schedule is not even consulted again.")]
	public async Task TriggerIfDue_ShouldNotConsultTheSchedule_WithinTheProbeInterval() {
		// Arrange
		IKnowledgeRefreshTrigger trigger = NewTrigger();
		await AwaitRefresh(trigger);
		_settings.ClearReceivedCalls();
		_management.ClearReceivedCalls();

		// Act - the hot path: many reads seconds apart.
		_clock.Advance(TimeSpan.FromSeconds(30));
		Task? second = trigger.TriggerIfDue();
		Task? third = trigger.TriggerIfDue();

		// Assert
		second.Should().BeNull(because: "a read moments after a refresh must not start another one");
		third.Should().BeNull(because: "the damper applies to every read, not only the second");
		_settings.DidNotReceive().TryScheduleAutoupdate(
			Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>());
		_management.DidNotReceive().Update(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Once the probe interval has passed, a guidance read consults the schedule again so a later release still arrives.")]
	public async Task TriggerIfDue_ShouldRefreshAgain_AfterTheProbeIntervalElapses() {
		// Arrange
		IKnowledgeRefreshTrigger trigger = NewTrigger();
		await AwaitRefresh(trigger);

		// Act
		_clock.Advance(TimeSpan.FromMinutes(6));
		await AwaitRefresh(trigger);

		// Assert
		_management.Received(2).Update(null, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("An operator who disabled or postponed knowledge autoupdate gets no unattended publisher call.")]
	public async Task TriggerIfDue_ShouldNotUpdate_WhenTheScheduleIsDisabledOrNotYetDue() {
		// Arrange - a disabled or not-yet-due policy is what TryScheduleAutoupdate answers false for.
		_settings.TryScheduleAutoupdate(
			AutoUpdateTarget.Knowledge, Arg.Any<DateTimeOffset>()).Returns(false);
		IKnowledgeRefreshTrigger trigger = NewTrigger();

		// Act
		await AwaitRefresh(trigger);

		// Assert
		_management.DidNotReceive().Update(Arg.Any<string>(), Arg.Any<CancellationToken>());
		_settings.Received(1).TryScheduleAutoupdate(
			AutoUpdateTarget.Knowledge, Arg.Any<DateTimeOffset>());
	}

	[Test]
	[Description("Only the knowledge schedule is consulted; the clio self-update and toolkit schedules stay the CLI's business.")]
	public async Task TriggerIfDue_ShouldNeverTouchTheOtherUpdateSchedules() {
		// Arrange
		IKnowledgeRefreshTrigger trigger = NewTrigger();

		// Act
		await AwaitRefresh(trigger);

		// Assert
		_settings.DidNotReceive().TryScheduleAutoupdate(
			AutoUpdateTarget.Clio, Arg.Any<DateTimeOffset>());
		_settings.DidNotReceive().TryScheduleAutoupdate(
			AutoUpdateTarget.Toolkit, Arg.Any<DateTimeOffset>());
	}

	[Test]
	[Description("A publisher that cannot be reached never surfaces on the read path, and the next interval retries.")]
	public async Task TriggerIfDue_ShouldSwallowAFailedRefresh_AndRetryLater() {
		// Arrange
		_management.Update(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(_ => throw new System.Net.Http.HttpRequestException("No such host is known."));
		IKnowledgeRefreshTrigger trigger = NewTrigger();

		// Act
		Task? first = trigger.TriggerIfDue();
		Func<Task> awaiting = async () => await first!;

		// Assert
		await awaiting.Should().NotThrowAsync(
			because: "a guidance read must never observe a transport failure from the refresh beside it");
		_clock.Advance(TimeSpan.FromMinutes(6));
		await AwaitRefresh(trigger);
		_management.Received(2).Update(null, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A settings file that cannot be read pauses the refresh instead of failing the guidance read.")]
	public async Task TriggerIfDue_ShouldSwallowAFailedScheduleRead() {
		// Arrange
		_settings.TryScheduleAutoupdate(AutoUpdateTarget.Knowledge, Arg.Any<DateTimeOffset>())
			.Returns(_ => throw new InvalidOperationException("appsettings.json is locked."));
		IKnowledgeRefreshTrigger trigger = NewTrigger();

		// Act
		Task? first = trigger.TriggerIfDue();
		Func<Task> awaiting = async () => await first!;

		// Assert
		await awaiting.Should().NotThrowAsync(
			because: "an unreadable settings file must not reach the agent asking for guidance");
		_management.DidNotReceive().Update(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A clock that jumped forward and back does not suppress refreshes until it catches up.")]
	public async Task TriggerIfDue_ShouldRefresh_WhenTheDamperIsFurtherOutThanOneInterval() {
		// Arrange - a forward jump stamps the damper far ahead, then the clock is corrected back.
		IKnowledgeRefreshTrigger trigger = NewTrigger();
		await AwaitRefresh(trigger);
		_clock.Advance(TimeSpan.FromDays(-30));

		// Act
		await AwaitRefresh(trigger);

		// Assert
		_management.Received(2).Update(null, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A successful refresh clears the failure run, so an outage that starts later is warned about again.")]
	public async Task TriggerIfDue_ShouldWarnAgain_WhenAnOutageFollowsASuccessfulRefresh() {
		// Arrange - fail, succeed, fail again, with the damper advanced between each read.
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));
		IKnowledgeRefreshTrigger trigger = NewTrigger();
		UpdateReturnsFailure();
		await AwaitRefresh(trigger);

		// Act
		_management.Update(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SuccessResult());
		_clock.Advance(TimeSpan.FromMinutes(6));
		await AwaitRefresh(trigger);
		UpdateReturnsFailure();
		_clock.Advance(TimeSpan.FromMinutes(6));
		await AwaitRefresh(trigger);

		// Assert
		warnings.Should().HaveCount(2,
			because: "the latch must not let the first bad day in a long-lived process silence every later outage");
	}

	[TestCase(McpHostTransportKind.Stdio, TestName = "TriggerIfDue_ShouldRefresh_OnTheStdioHost")]
	[TestCase(McpHostTransportKind.Http, TestName = "TriggerIfDue_ShouldRefresh_OnTheHttpHost")]
	[Description("Both MCP hosts refresh: the HTTP one runs for weeks as a service task and is the case this exists for.")]
	public async Task TriggerIfDue_ShouldRefresh_OnEitherHostTransport(McpHostTransportKind transport) {
		// Arrange
		IKnowledgeRefreshTrigger trigger = NewTrigger(transport);

		// Act
		await AwaitRefresh(trigger);

		// Assert
		_management.Received(1).Update(null, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("An ordinary CLI verb that reads guidance starts no refresh: it would exit before finishing and burn the schedule window.")]
	public void TriggerIfDue_ShouldStartNothing_OutsideAnMcpHost() {
		// Arrange - Unknown is what a CLI process and a hand-built container both read as.
		IKnowledgeRefreshTrigger trigger = NewTrigger(McpHostTransportKind.Unknown);

		// Act
		Task? refresh = trigger.TriggerIfDue();

		// Assert
		refresh.Should().BeNull(
			because: "a short-lived process abandons the refresh, and TryScheduleAutoupdate would already have consumed the window");
		_settings.DidNotReceive().TryScheduleAutoupdate(
			Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>());
		_management.DidNotReceive().Update(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("An MCP worker starts no refresh either: it serves one call and exits, so it abandons the work exactly as a CLI verb does.")]
	public void TriggerIfDue_ShouldStartNothing_InAWorkerProcess() {
		// Arrange - a worker serves the stdio transport, so the transport check alone would let it through.
		IKnowledgeRefreshTrigger trigger = NewTrigger(McpHostTransportKind.Stdio, isWorker: true);

		// Act
		Task? refresh = trigger.TriggerIfDue();

		// Assert
		refresh.Should().BeNull(because: "a worker outlives nothing it starts");
		_settings.DidNotReceive().TryScheduleAutoupdate(
			Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>());
	}

	[Test]
	[Description("The CLIO_NO_UPDATE_CHECK opt-out the end-to-end harness relies on suppresses this path too.")]
	public void TriggerIfDue_ShouldStartNothing_WhenUpdateChecksAreSuppressed() {
		// Arrange
		IKnowledgeRefreshTrigger trigger = NewTrigger(updatesSuppressed: true);

		// Act
		Task? refresh = trigger.TriggerIfDue();

		// Assert
		refresh.Should().BeNull(
			because: "a harness that suppressed unattended updates must not get a publisher call from a guidance read");
		_management.DidNotReceive().Update(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A second concurrent read does not start a second refresh while one is still in flight.")]
	public async Task TriggerIfDue_ShouldStartOneRefresh_WhileAnotherIsInFlight() {
		// Arrange - hold the refresh inside Update until both reads have been made.
		using ManualResetEventSlim release = new(initialState: false);
		using ManualResetEventSlim entered = new(initialState: false);
		_management.Update(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => {
			entered.Set();
			release.Wait(TimeSpan.FromSeconds(10));
			return SuccessResult();
		});
		IKnowledgeRefreshTrigger trigger = NewTrigger();

		// Act
		Task? first = trigger.TriggerIfDue();
		entered.Wait(TimeSpan.FromSeconds(10));
		// Move past the damper so the second read reaches the single-flight guard instead of being
		// refused by the probe interval.
		_clock.Advance(TimeSpan.FromMinutes(6));
		Task? second = trigger.TriggerIfDue();
		release.Set();
		await first!;

		// Assert
		first.Should().NotBeNull(because: "the first read is due and must start the refresh");
		second.Should().BeNull(because: "the single-flight guard must refuse a concurrent second refresh");
		_management.Received(1).Update(null, Arg.Any<CancellationToken>());
	}

	private IKnowledgeRefreshTrigger NewTrigger(
		McpHostTransportKind transport = McpHostTransportKind.Stdio,
		bool isWorker = false,
		bool updatesSuppressed = false) =>
		new KnowledgeRefreshTrigger(
			_settings, _management, _clock, _logger,
			() => transport,
			() => isWorker,
			() => updatesSuppressed);

	/// <summary>Triggers a refresh, asserts one was started, and awaits it.</summary>
	private static async Task AwaitRefresh(IKnowledgeRefreshTrigger trigger) {
		Task? refresh = trigger.TriggerIfDue();
		refresh.Should().NotBeNull(because: "the cache is due for verification, so a refresh must start");
		await refresh!;
	}

	private void UpdateReturnsFailure() =>
		_management.Update(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
			new KnowledgeSourceBatchResult(
				false,
				"Knowledge sources could not be updated.",
				[new KnowledgeSourceOperationResult(
					CuratedKnowledgeSourceDefaults.Alias, false, "failed", "The publisher is unreachable.")]));

	private static KnowledgeSourceBatchResult SuccessResult() => new(
		true,
		"Knowledge source 'creatio-curated' was updated.",
		[new KnowledgeSourceOperationResult(
			CuratedKnowledgeSourceDefaults.Alias, true, "updated", "updated")]);

	/// <summary>A clock the fixture moves by hand, so the damper is exercised without real waiting.</summary>
	private sealed class MutableClock : TimeProvider {
		private DateTimeOffset _utcNow = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => _utcNow;

		internal void Advance(TimeSpan elapsed) => _utcNow += elapsed;
	}
}
