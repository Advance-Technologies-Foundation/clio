using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using Clio.CreatioModel;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class WatchCompilationCommandTests : BaseCommandTests<WatchCompilationOptions> {

	// NSubstitute instances must be (re)created in Setup(), not via field initializers: NUnit reuses
	// one fixture instance across every [Test] in the class, so field-initialized substitutes would
	// carry configured behavior (e.g. .Throws(...)) from one test into the next test's Arrange phase.
	private ICompilationHistoryPoller _poller;
	private ICompilationSettleTracker _settleTracker;
	private IPollRetryPolicy _retryPolicy;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_poller);
		containerBuilder.AddSingleton(_settleTracker);
		containerBuilder.AddSingleton(_retryPolicy);
	}

	[SetUp]
	public override void Setup() {
		_poller = Substitute.For<ICompilationHistoryPoller>();
		_settleTracker = Substitute.For<ICompilationSettleTracker>();
		_retryPolicy = Substitute.For<IPollRetryPolicy>();
		base.Setup();
		// Default "already settled, healthy, clean" wiring; individual tests override what they need.
		_retryPolicy.IsChannelHealthy(Arg.Any<DateTime>()).Returns(true);
		_settleTracker.IsSettled(Arg.Any<DateTime>()).Returns(true);
		_settleTracker.Snapshot.Returns(new CompilationSettleSnapshot(false, true, 0, DateTime.UtcNow));
		_poller.PollOnce(Arg.Any<DateTime>()).Returns(new List<CompilationHistory>());
	}

	[TearDown]
	public override void TearDown() {
		base.TearDown();
	}

	[Test]
	[Description("Verifies Execute returns exit code 0 when the tracker reports settled with no errors")]
	public void Execute_ReturnsZero_WhenSettledWithNoErrors() {
		// Arrange
		WatchCompilationCommand command = Container.GetRequiredService<WatchCompilationCommand>();
		WatchCompilationOptions options = new() { GiveUpAfterSeconds = 300 };
		CompilationHistory baseline = new() { CreatedOn = DateTime.UtcNow, ProjectName = "Some.csproj", ErrorsWarnings = "[]" };
		_poller.GetBaseline().Returns(baseline);

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "the settle tracker reported a clean, settled session");
		_settleTracker.Received(1).SeedFromBaseline(baseline, Arg.Any<DateTime>());
	}

	[Test]
	[Description("Verifies Execute returns exit code 1 when the settled session had errors")]
	public void Execute_ReturnsOne_WhenSettledWithErrors() {
		// Arrange
		WatchCompilationCommand command = Container.GetRequiredService<WatchCompilationCommand>();
		WatchCompilationOptions options = new() { GiveUpAfterSeconds = 300 };
		_poller.GetBaseline().Returns((CompilationHistory)null);
		_settleTracker.Snapshot.Returns(new CompilationSettleSnapshot(true, true, 1, DateTime.UtcNow));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "the settled session contained compilation errors");
	}

	[Test]
	[Description("Verifies Execute returns exit code 2 (gave up), not a confirmed failure, when activity was observed but the ODataEntities marker never appeared and the give-up deadline is exceeded - a CI/TeamCity trigger that knows a full compile is running should get 'still waiting', not 'failed', when only a package-level row has shown up so far")]
	public void Execute_ReturnsTwo_WhenActivityObservedButFinalMarkerNeverSeen_AndDeadlineExceeded() {
		// Arrange
		WatchCompilationCommand command = Container.GetRequiredService<WatchCompilationCommand>();
		WatchCompilationOptions options = new() { GiveUpAfterSeconds = 0 };
		_poller.GetBaseline().Returns((CompilationHistory)null);
		_settleTracker.Snapshot.Returns(new CompilationSettleSnapshot(false, false, 1, DateTime.UtcNow));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(2, because: "IsSettled never reports true while activity was observed but the marker is missing (see CompilationSettleTracker), so only the overall give-up-after budget ends the wait, never a confirmed-failure verdict");
	}

	[Test]
	[Description("Verifies Execute returns exit code 0 when nothing happened this session, even without ever seeing the final marker")]
	public void Execute_ReturnsZero_WhenNoActivityObservedAndNoErrors() {
		// Arrange
		WatchCompilationCommand command = Container.GetRequiredService<WatchCompilationCommand>();
		WatchCompilationOptions options = new() { GiveUpAfterSeconds = 300 };
		_poller.GetBaseline().Returns((CompilationHistory)null);
		_settleTracker.Snapshot.Returns(new CompilationSettleSnapshot(false, false, 0, DateTime.UtcNow));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "an idle environment with zero observed activity and no errors has nothing to disprove success");
	}

	[Test]
	[Description("Verifies Execute returns exit code 3 when reading the initial compilation history baseline fails")]
	public void Execute_ReturnsThree_WhenGetBaselineThrows() {
		// Arrange
		WatchCompilationCommand command = Container.GetRequiredService<WatchCompilationCommand>();
		WatchCompilationOptions options = new() { GiveUpAfterSeconds = 300 };
		_poller.GetBaseline().Throws(new InvalidOperationException("connection refused"));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(3, because: "a failure reading the very first baseline is a startup error, not a mid-watch retry condition");
	}

	[Test]
	[Description("Verifies Execute returns exit code 2 when the give-up deadline has already elapsed before the first check")]
	public void Execute_ReturnsTwo_WhenGiveUpDeadlineAlreadyElapsed() {
		// Arrange
		WatchCompilationCommand command = Container.GetRequiredService<WatchCompilationCommand>();
		WatchCompilationOptions options = new() { GiveUpAfterSeconds = 0 };
		_poller.GetBaseline().Returns((CompilationHistory)null);

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(2, because: "a zero-second give-up window is already exhausted by the time the loop makes its first check, even though the tracker would settle instantly");
	}

	[Test]
	[Description("Verifies Execute feeds each newly polled record into the settle tracker")]
	public void Execute_ObservesEachNewlyPolledRecord() {
		// Arrange
		WatchCompilationCommand command = Container.GetRequiredService<WatchCompilationCommand>();
		WatchCompilationOptions options = new() { GiveUpAfterSeconds = 300 };
		DateTime baselineTime = DateTime.UtcNow;
		_poller.GetBaseline().Returns((CompilationHistory)null);
		CompilationHistory newRecord = new() { CreatedOn = baselineTime.AddSeconds(1), ProjectName = "Some.csproj", ErrorsWarnings = "[]" };
		_poller.PollOnce(Arg.Any<DateTime>()).Returns([newRecord]);

		// Act
		command.Execute(options);

		// Assert
		_settleTracker.Received(1).Observe(newRecord, Arg.Any<DateTime>());
	}

	[Test]
	[Description("Verifies Execute observes a record with an already-seen Id only once, even when PollOnce keeps returning it across multiple rounds (a real Creatio CompilationHistory table can re-return a row on the next round; without this guard the settle tracker's activity clock would never stop resetting)")]
	public void Execute_DoesNotReObserveARecord_WhenPollOnceReturnsTheSameIdAcrossRounds() {
		// Arrange
		WatchCompilationCommand command = Container.GetRequiredService<WatchCompilationCommand>();
		WatchCompilationOptions options = new() { GiveUpAfterSeconds = 300 };
		_poller.GetBaseline().Returns((CompilationHistory)null);
		// Id is a read-only BaseModel property auto-assigned at construction; reusing this SAME instance
		// (not constructing a fresh one) across both poll rounds is what gives it the same Id both times.
		CompilationHistory duplicateRecord = new() {
			CreatedOn = DateTime.UtcNow, ProjectName = "Some.csproj", ErrorsWarnings = "[]"
		};
		int pollCount = 0;
		_poller.PollOnce(Arg.Any<DateTime>()).Returns(_ => {
			pollCount++;
			return new List<CompilationHistory> { duplicateRecord };
		});
		// Settle only once the duplicate has been returned across two separate rounds, proving the
		// second round's repeat did not reset the tracker's activity clock (it would if re-observed).
		_settleTracker.IsSettled(Arg.Any<DateTime>()).Returns(_ => pollCount >= 2);

		// Act
		command.Execute(options);

		// Assert
		_settleTracker.Received(1).Observe(duplicateRecord, Arg.Any<DateTime>());
		pollCount.Should().BeGreaterThanOrEqualTo(2, because: "the test setup requires at least two poll rounds to prove the duplicate is only observed once");
	}

	[Test]
	[Description("Verifies Execute retries and recovers when a poll attempt fails once before later succeeding")]
	public void Execute_RecordsFailureThenSuccess_WhenAPollAttemptThrowsOnce() {
		// Arrange
		WatchCompilationCommand command = Container.GetRequiredService<WatchCompilationCommand>();
		WatchCompilationOptions options = new() { GiveUpAfterSeconds = 300 };
		_poller.GetBaseline().Returns((CompilationHistory)null);
		_retryPolicy.NextDelay.Returns(TimeSpan.FromMilliseconds(1));
		bool healthyAfterFirstFailure = false;
		_retryPolicy.IsChannelHealthy(Arg.Any<DateTime>()).Returns(_ => healthyAfterFirstFailure);
		_poller.PollOnce(Arg.Any<DateTime>())
			.Returns(_ => throw new InvalidOperationException("transient network fault"),
				_ => {
					healthyAfterFirstFailure = true;
					return new List<CompilationHistory>();
				});

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "the channel recovered on the second attempt and the tracker reports settled");
		_retryPolicy.Received(1).RecordFailure(Arg.Any<DateTime>());
		_retryPolicy.Received(1).RecordSuccess(Arg.Any<DateTime>());
	}

}
