using System;
using Clio.Common;
using Clio.CreatioModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class CompilationSettleTrackerTests {

	private static CompilationHistory NewRecord(DateTime createdOn, string projectName = "SomePackage.csproj",
		int durationSeconds = 1, string errorsWarnings = "[]") =>
		new() {
			CreatedOn = createdOn,
			ProjectName = projectName,
			DurationInSeconds = durationSeconds,
			ErrorsWarnings = errorsWarnings
		};

	[Test]
	[Description("Verifies the tracker is not settled immediately after seeding from a baseline observed at the same instant")]
	public void IsSettled_ReturnsFalse_ImmediatelyAfterSeeding() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.SeedFromBaseline(NewRecord(now), now);

		// Act
		bool settled = sut.IsSettled(now);

		// Assert
		settled.Should().BeFalse(because: "no time has passed since the last observed activity");
	}

	[Test]
	[Description("Verifies the tracker does NOT settle before the base quiet window elapses when zero activity has ever been observed, so a compile that is merely warming up (no rows written yet) is not mistaken for an idle environment")]
	public void IsSettled_ReturnsFalse_WithinBaseQuietWindow_WhenNoActivityObserved() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime baselineTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.SeedFromBaseline(NewRecord(baselineTime), baselineTime);
		DateTime withinWindow = baselineTime.AddSeconds(CompilationSettleTracker.BaseQuietWindowSeconds - 1);

		// Act
		bool settled = sut.IsSettled(withinWindow);

		// Assert
		settled.Should().BeFalse(
			because: "the base quiet window has not fully elapsed yet, and with zero new activity observed there is no other signal to trust");
	}

	[Test]
	[Description("Verifies the tracker settles once the base quiet window elapses with zero activity ever observed (the genuinely-idle case)")]
	public void IsSettled_ReturnsTrue_AfterBaseQuietWindowElapses_WhenNoActivityObserved() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime baselineTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.SeedFromBaseline(NewRecord(baselineTime), baselineTime);
		DateTime afterWindow = baselineTime.AddSeconds(CompilationSettleTracker.BaseQuietWindowSeconds + 1);

		// Act
		bool settled = sut.IsSettled(afterWindow);

		// Assert
		settled.Should().BeTrue(
			because: "the base quiet window has fully elapsed with zero activity ever observed, which is the genuinely-idle case");
	}

	[Test]
	[Description("Verifies the base quiet window applies even AFTER activity has been observed - a real compile-configuration --all run on cec showed a ~31-33s gap between an ordinary package finishing and the next (slower, aggregate) project's own row; a smaller window here would falsely declare 'finished' mid-compile the first time the next project happens to be slower than anything seen so far")]
	public void IsSettled_ReturnsFalse_WithinBaseQuietWindow_AfterActivityWasObserved() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime baselineTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.SeedFromBaseline(null, baselineTime);
		DateTime recordAt = baselineTime.AddSeconds(1);
		sut.Observe(NewRecord(recordAt, durationSeconds: 2), recordAt);
		// Mirrors the real observed gap (~32s) between two ordinary/fast packages, before the
		// next (slow) project's own row has appeared yet.
		DateTime midGap = recordAt.AddSeconds(32);

		// Act
		bool settled = sut.IsSettled(midGap);

		// Assert
		settled.Should().BeFalse(
			because: "only 32s have passed since the last observed row, well within the 45s base quiet window - the next project may simply be slower than anything seen so far, not finished");
	}

	[Test]
	[Description("Verifies the adaptive quiet window scales with the slowest project duration observed this session, so a slow project in progress is not mistaken for finished")]
	public void IsSettled_ReturnsFalse_WhenElapsedTimeIsWithinAdaptiveWindowForASlowProject() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime baselineTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.SeedFromBaseline(null, baselineTime);
		DateTime slowRecordAt = baselineTime.AddSeconds(1);
		sut.Observe(NewRecord(slowRecordAt, durationSeconds: 40), slowRecordAt);
		// Adaptive window = max(45, 40 * 1.5) = 60s; only 50s have passed since the slow record.
		DateTime stillWithinAdaptiveWindow = slowRecordAt.AddSeconds(50);

		// Act
		bool settled = sut.IsSettled(stillWithinAdaptiveWindow);

		// Assert
		settled.Should().BeFalse(because: "a 40s project extends the quiet window to 60s (40 * DurationScaleFactor), and only 50s have passed");
	}

	[Test]
	[Description("Verifies the tracker settles once the adaptive window for a slow project fully elapses")]
	public void IsSettled_ReturnsTrue_AfterAdaptiveWindowForASlowProjectElapses() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime baselineTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.SeedFromBaseline(null, baselineTime);
		DateTime slowRecordAt = baselineTime.AddSeconds(1);
		sut.Observe(NewRecord(slowRecordAt, durationSeconds: 40), slowRecordAt);
		DateTime pastAdaptiveWindow = slowRecordAt.AddSeconds(61);

		// Act
		bool settled = sut.IsSettled(pastAdaptiveWindow);

		// Assert
		settled.Should().BeTrue(because: "61s have passed, exceeding the 60s adaptive window (40 * DurationScaleFactor)");
	}

	[Test]
	[Description("Verifies SawFinalMarker becomes true only after the ODataEntities project record is observed")]
	public void Snapshot_SawFinalMarker_IsTrue_AfterObservingODataEntitiesRecord() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.SeedFromBaseline(null, now);
		sut.Observe(NewRecord(now, projectName: "SomeOtherPackage.csproj"), now);
		bool beforeMarker = sut.Snapshot.SawFinalMarker;

		// Act
		sut.Observe(NewRecord(now, projectName: CompilationSettleTracker.FinalMarkerProjectName), now);

		// Assert
		beforeMarker.Should().BeFalse(because: "only an unrelated project had been observed at that point");
		sut.Snapshot.SawFinalMarker.Should().BeTrue(because: "the ODataEntities marker project was just observed");
	}

	[Test]
	[Description("Verifies HasErrors becomes true after observing a record with a real (non-warning) error and is never cleared by a later clean record")]
	public void Snapshot_HasErrors_StaysTrue_OnceAnyRecordHadErrors() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.SeedFromBaseline(null, now);
		sut.Observe(NewRecord(now, errorsWarnings: "[{\"ErrorText\":\"boom\",\"IsWarning\":false}]"), now);

		// Act
		sut.Observe(NewRecord(now.AddSeconds(1), errorsWarnings: "[]"), now.AddSeconds(1));

		// Assert
		sut.Snapshot.HasErrors.Should().BeTrue(because: "one erroring record was observed earlier in the session and errors are never cleared by a later clean record");
	}

	[Test]
	[Description("Verifies HasErrors stays false when ErrorsWarnings contains only warning entries - a real compile-configuration --all run on cec finished successfully with a CS0114 warning, so a warning alone must not fail the watch's exit code")]
	public void Snapshot_HasErrors_StaysFalse_WhenErrorsWarningsContainsOnlyWarnings() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.SeedFromBaseline(null, now);

		// Act
		sut.Observe(NewRecord(now, errorsWarnings: "[{\"ErrorNumber\":\"CS0114\",\"ErrorText\":\"hides inherited member\",\"IsWarning\":true}]"), now);

		// Assert
		sut.Snapshot.HasErrors.Should().BeFalse(because: "every entry in ErrorsWarnings is a warning (IsWarning=true), so the session is not a real failure");
	}

	[Test]
	[Description("Verifies HasErrors becomes true when ErrorsWarnings mixes warnings with at least one real (non-warning) error")]
	public void Snapshot_HasErrors_BecomesTrue_WhenErrorsWarningsMixesWarningsWithARealError() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.SeedFromBaseline(null, now);

		// Act
		sut.Observe(NewRecord(now, errorsWarnings:
			"[{\"ErrorNumber\":\"CS0114\",\"IsWarning\":true},{\"ErrorNumber\":\"CS1002\",\"IsWarning\":false}]"), now);

		// Assert
		sut.Snapshot.HasErrors.Should().BeTrue(because: "at least one entry is a real (non-warning) error, so the session must be reported as failed regardless of any accompanying warnings");
	}

	[Test]
	[Description("Verifies HasErrors becomes true when ErrorsWarnings cannot be parsed as JSON, favoring under-claiming success over a false clean result in a CI-facing exit code")]
	public void Snapshot_HasErrors_BecomesTrue_WhenErrorsWarningsIsUnparseable() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.SeedFromBaseline(null, now);

		// Act
		sut.Observe(NewRecord(now, errorsWarnings: "not valid json"), now);

		// Assert
		sut.Snapshot.HasErrors.Should().BeTrue(because: "unparseable ErrorsWarnings content cannot be proven warning-only, so it must be treated conservatively as an error");
	}

	[Test]
	[Description("Verifies baseline seeding carries the baseline record's own error state into the snapshot")]
	public void SeedFromBaseline_CarriesOverErrorState_FromBaselineRecordItself() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		CompilationHistory dirtyBaseline = NewRecord(now, errorsWarnings: "[{\"ErrorText\":\"boom\"}]");

		// Act
		sut.SeedFromBaseline(dirtyBaseline, now);

		// Assert
		sut.Snapshot.HasErrors.Should().BeTrue(because: "the baseline record itself already carried errors before watching even started");
	}

	[Test]
	[Description("Verifies NewRecordCount only counts records observed after the baseline, not the seeded baseline itself")]
	public void Snapshot_NewRecordCount_ExcludesTheSeededBaselineRecord() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		sut.SeedFromBaseline(NewRecord(now), now);

		// Act
		sut.Observe(NewRecord(now.AddSeconds(1)), now.AddSeconds(1));
		sut.Observe(NewRecord(now.AddSeconds(2)), now.AddSeconds(2));

		// Assert
		sut.Snapshot.NewRecordCount.Should().Be(2, because: "only the two Observe() calls represent genuine new activity, the seeded baseline does not count");
	}

	[Test]
	[Description("Verifies seeding from a null baseline (brand-new environment with no compilation history at all) uses 'now' as the last-activity timestamp and carries no error/marker state")]
	public void SeedFromBaseline_WithNullBaseline_UsesNowAsLastActivityAndNoState() {
		// Arrange
		CompilationSettleTracker sut = new();
		DateTime now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

		// Act
		sut.SeedFromBaseline(null, now);

		// Assert
		sut.Snapshot.LastActivityAt.Should().Be(now, because: "there is no baseline record to seed the timestamp from, so 'now' is used");
		sut.Snapshot.HasErrors.Should().BeFalse(because: "there is no baseline record to carry error state from");
		sut.Snapshot.SawFinalMarker.Should().BeFalse(because: "there is no baseline record to carry marker state from");
	}

}
