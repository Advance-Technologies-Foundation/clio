using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Clio.Common;
using Clio.CreatioModel;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class CompilationActivityWatcherTests {

	private const int WaitTimeoutMs = 5_000;

	private static CompilationHistory Row(string projectName = "CrtBase.csproj",
		string errorsWarnings = "[]", int secondsFromEpoch = 1) {
		CompilationHistory record = new() {
			CreatedOn = new DateTime(2026, 9, 9, 16, 0, 0, DateTimeKind.Utc).AddSeconds(secondsFromEpoch),
			ProjectName = projectName,
			ErrorsWarnings = errorsWarnings,
			DurationInSeconds = 2
		};
		AssignId(record, Guid.NewGuid());
		return record;
	}

	// ATF's BaseModel.Id has no accessible setter - the repository writes it when a row is materialised -
	// so a test that needs DISTINCT row identities has to reach the backing field. Without this every
	// constructed row would share Guid.Empty, and the watcher's own de-duplication would collapse them:
	// a de-duplication test would then pass for the wrong reason.
	private static void AssignId(CompilationHistory record, Guid id) {
		FieldInfo backingField = FindIdBackingField(typeof(CompilationHistory));
		backingField.Should().NotBeNull(
			because: "the test needs distinct row identities and ATF exposes no setter for them");
		backingField!.SetValue(record, id);
	}

	private static FieldInfo FindIdBackingField(Type type) {
		for (Type current = type; current is not null; current = current.BaseType) {
			FieldInfo field = current.GetField($"<{nameof(CompilationHistory.Id)}>k__BackingField",
				BindingFlags.NonPublic | BindingFlags.Instance);
			if (field is not null) {
				return field;
			}
		}
		return null;
	}

	[Test]
	[Description("Every new compilation-history row is reported once and counted once, so a table that returns the same row again across rounds cannot inflate the activity count or reset the activity clock.")]
	public void Watcher_ShouldReportEachRowOnce_WhenTheSameRowIsReturnedAgain() {
		// Arrange
		ICompilationHistoryPoller poller = Substitute.For<ICompilationHistoryPoller>();
		CompilationHistory row = Row();
		poller.PollOnce(Arg.Any<DateTime>()).Returns(_ => new List<CompilationHistory> { row });
		using CountdownEvent reported = new(1);
		List<CompilationHistory> observed = [];
		ICompilationActivityWatcher watcher = new CompilationActivityWatcher(poller, new PollRetryPolicy());

		// Act
		watcher.Start(DateTime.MinValue, record => {
			lock (observed) {
				observed.Add(record);
			}
			if (!reported.IsSet) {
				reported.Signal();
			}
		});
		reported.Wait(WaitTimeoutMs);
		Thread.Sleep(CompilationActivityWatcher.PollInterval + CompilationActivityWatcher.PollInterval);
		watcher.Stop();

		// Assert
		lock (observed) {
			observed.Should().HaveCount(1,
				because: "a duplicate row must be recognised by Id and reported only the first time");
		}
		watcher.Snapshot.NewRecordCount.Should().Be(1,
			because: "the activity count follows what was reported, not how many times the row was read");
	}

	[Test]
	[Description("A read that throws does not end the watch. The environment stops answering in the middle of every successful configuration build - it is reloading - and a watcher that died there would leave the caller waiting for progress nobody can report.")]
	public void Watcher_ShouldSurviveAReadFailure_AndKeepPolling() {
		// Arrange
		ICompilationHistoryPoller poller = Substitute.For<ICompilationHistoryPoller>();
		CompilationHistory row = Row();
		using CountdownEvent reported = new(1);
		poller.PollOnce(Arg.Any<DateTime>())
			.Returns(
				_ => throw new InvalidOperationException("environment is reloading"),
				_ => new List<CompilationHistory> { row });
		ICompilationActivityWatcher watcher = new CompilationActivityWatcher(poller, new PollRetryPolicy());

		// Act
		watcher.Start(DateTime.MinValue, _ => {
			if (!reported.IsSet) {
				reported.Signal();
			}
		});
		bool signalled = reported.Wait(WaitTimeoutMs);
		watcher.Stop();

		// Assert
		signalled.Should().BeTrue(
			because: "the poll loop must keep running after a failed read and report the row that follows it");
	}

	[Test]
	[Description("A non-warning diagnostic on an observed row is surfaced, so a failed build is reported as failed even when the verdict endpoint cannot be read afterwards.")]
	public void Watcher_ShouldSurfaceRealErrors_FromObservedRows() {
		// Arrange
		ICompilationHistoryPoller poller = Substitute.For<ICompilationHistoryPoller>();
		using CountdownEvent reported = new(1);
		poller.PollOnce(Arg.Any<DateTime>()).Returns(_ => new List<CompilationHistory> {
			Row(errorsWarnings: "[{\"IsWarning\":false,\"ErrorNumber\":\"CS0103\"}]")
		});
		ICompilationActivityWatcher watcher = new CompilationActivityWatcher(poller, new PollRetryPolicy());

		// Act
		watcher.Start(DateTime.MinValue, _ => {
			if (!reported.IsSet) {
				reported.Signal();
			}
		});
		reported.Wait(WaitTimeoutMs);
		CompilationActivitySnapshot snapshot = watcher.Snapshot;
		watcher.Stop();

		// Assert
		snapshot.HasErrors.Should().BeTrue(
			because: "a non-warning compiler diagnostic on a build row means the build failed");
	}

	[Test]
	[Description("Stop is safe on a watcher that was never started, so a caller unwinding from an early failure does not have to track whether it got that far.")]
	public void Watcher_ShouldTolerateStop_WhenItWasNeverStarted() {
		// Arrange
		ICompilationHistoryPoller poller = Substitute.For<ICompilationHistoryPoller>();
		ICompilationActivityWatcher watcher = new CompilationActivityWatcher(poller, new PollRetryPolicy());

		// Act
		Action act = watcher.Stop;

		// Assert
		act.Should().NotThrow(because: "an unstarted watcher has nothing to stop and no state to dispose");
	}

}
