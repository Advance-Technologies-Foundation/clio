using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.CreatioInstallCommand;
using Clio.Command.McpServer.Progress;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for the deploy stage-event emitter (story 2): the manifest built from the resolved deploy
/// execution path, per-stage running/done transitions, the conditional stage-build skip, the failure
/// cascade, monotonic sequencing, secret redaction at the single emission boundary, and inert behavior
/// when no subscriber is attached.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public class StageEventEmitterTests {

	private static IReadOnlyList<StageDescriptor> DeployManifest => CreatioInstallerService.BuildDeployManifest();

	private static (StageEventEmitter Emitter, List<ClioStageEvent> Events) CreateEmitter() {
		StageEventEmitter emitter = new();
		List<ClioStageEvent> events = [];
		emitter.Begin(ClioStageEventContract.Operations.Deploy, DeployManifest, events.Add);
		return (emitter, events);
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-08: the deploy manifest lists the eight deploy stages in order with stage-build conditional.")]
	public void BuildDeployManifest_ShouldListEightDeployStagesInOrder_WhenDeployPathResolved() {
		// Arrange
		string[] expectedOrder = [
			StageIds.StageBuild, StageIds.Unzip, StageIds.CopyFiles, StageIds.RestoreDb, StageIds.DeployApp,
			StageIds.ConfigureConnStrings, StageIds.RegisterEnv, StageIds.WaitReady
		];

		// Act
		IReadOnlyList<StageDescriptor> manifest = CreatioInstallerService.BuildDeployManifest();

		// Assert
		manifest.Select(descriptor => descriptor.StageId).Should().Equal(expectedOrder,
			"because the manifest must mirror the real deploy stage order (FR-05 / ADR fact 7)");
		manifest.Should().ContainSingle(descriptor => descriptor.StageId == StageIds.StageBuild && descriptor.Conditional,
			"because stage-build is the only conditional deploy stage (network-drive source only)");
		manifest.Where(descriptor => descriptor.StageId != StageIds.StageBuild)
			.Should().OnlyContain(descriptor => !descriptor.Conditional,
				"because every deploy stage other than stage-build always runs");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-08: Begin emits a single manifest event first with zero-based index and total equal to the manifest length.")]
	public void Begin_ShouldEmitManifestFirstWithZeroBasedIndexAndTotal_WhenRunStarts() {
		// Arrange
		StageEventEmitter emitter = new();
		List<ClioStageEvent> events = [];

		// Act
		emitter.Begin(ClioStageEventContract.Operations.Deploy, DeployManifest, events.Add);

		// Assert
		events.Should().ContainSingle("because exactly one manifest event is emitted up front");
		ClioStageEvent manifest = events[0];
		manifest.EventType.Should().Be(ClioStageEventContract.EventTypes.Manifest,
			"because the first event of a run is the manifest");
		manifest.Sequence.Should().Be(0, "because the manifest is the first event of the run");
		manifest.Stages.Should().NotBeNull().And.HaveCount(8, "because the deploy path has eight stages");
		manifest.Stages.Select(stage => stage.Index).Should().Equal(Enumerable.Range(0, 8),
			"because manifest indexes are zero-based and contiguous");
		manifest.Stages.Should().OnlyContain(stage => stage.Total == 8,
			"because total equals the manifest length for every entry");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-09: each run stage emits running then done in order with startedAtUtc on running and durationMs on done.")]
	public void RunStage_ShouldEmitRunningThenDoneInOrder_WhenStageSucceeds() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();

		// Act
		emitter.RunStage(StageIds.Unzip, () => { });

		// Assert
		List<ClioStageEvent> stageEvents = events.Where(e => e.EventType == ClioStageEventContract.EventTypes.Stage).ToList();
		stageEvents.Should().HaveCount(2, "because a successful stage emits running then done");
		stageEvents[0].Stage!.Status.Should().Be(ClioStageEventContract.StageStatuses.Running,
			"because the stage first transitions to running");
		stageEvents[0].Stage!.StartedAtUtc.Should().NotBeNull("because startedAtUtc is set on running");
		stageEvents[0].Stage!.Index.Should().Be(1, "because unzip is the second stage (zero-based index 1)");
		stageEvents[1].Stage!.Status.Should().Be(ClioStageEventContract.StageStatuses.Done,
			"because the stage then transitions to done");
		stageEvents[1].Stage!.DurationMs.Should().NotBeNull("because durationMs is set on done");
		stageEvents[1].Stage!.Total.Should().Be(8, "because the stage carries the manifest total");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-10: a stage skipped by condition is emitted skipped with skipReason not-applicable.")]
	public void SkipStage_ShouldEmitSkippedNotApplicable_WhenStageIsInert() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();

		// Act
		emitter.SkipStage(StageIds.StageBuild, ClioStageEventContract.SkipReasons.NotApplicable);

		// Assert
		ClioStageDetail skipped = events.Last().Stage;
		skipped.Should().NotBeNull("because a skip emits a stage event");
		skipped!.StageId.Should().Be(StageIds.StageBuild, "because stage-build is the inert stage");
		skipped.Status.Should().Be(ClioStageEventContract.StageStatuses.Skipped, "because the stage was skipped");
		skipped.SkipReason.Should().Be(ClioStageEventContract.SkipReasons.NotApplicable,
			"because a condition-off skip is distinct from a failure-cascade skip");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-11: a thrown stage emits failed, cascades remaining stages as skipped after-failure, then a failure run-completed, in that order.")]
	public void RunStage_ShouldCascadeFailure_WhenStageThrows() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();
		emitter.RunStage(StageIds.StageBuild, () => { });
		emitter.RunStage(StageIds.Unzip, () => { });
		int eventsBeforeFailure = events.Count;

		// Act
		Action act = () => emitter.RunStage(StageIds.CopyFiles, () => throw new InvalidOperationException("boom"));

		// Assert
		act.Should().Throw<InvalidOperationException>("because RunStage rethrows so the caller's control flow is unchanged");
		List<ClioStageEvent> cascade = events.Skip(eventsBeforeFailure).ToList();
		cascade[0].Stage!.Status.Should().Be(ClioStageEventContract.StageStatuses.Running,
			"because the failing stage first transitions to running");
		cascade[1].Stage!.Status.Should().Be(ClioStageEventContract.StageStatuses.Failed,
			"because the active stage is emitted failed after it throws");
		cascade[1].Stage!.StageId.Should().Be(StageIds.CopyFiles, "because copy-files is the failing stage");
		cascade[1].Stage!.ErrorCode.Should().NotBeNullOrEmpty("because a failed stage carries a symbolic error code");
		List<ClioStageEvent> skips = cascade
			.Where(e => e.Stage?.Status == ClioStageEventContract.StageStatuses.Skipped).ToList();
		skips.Should().OnlyContain(e => e.Stage.SkipReason == ClioStageEventContract.SkipReasons.AfterFailure,
			"because every remaining stage is skipped with skipReason after-failure");
		skips.Select(e => e.Stage.Index).Should().Equal([3, 4, 5, 6, 7],
			"because the stages after copy-files (index 2) are cascaded in order");
		cascade.Last().EventType.Should().Be(ClioStageEventContract.EventTypes.RunCompleted,
			"because the terminal run-completed event closes the cascade");
		cascade.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure,
			"because a cascade ends the run in failure");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-12: a fully successful run emits a terminal run-completed with outcome success and derived url/path.")]
	public void CompleteSuccess_ShouldEmitRunCompletedSuccessWithDerivedValues_WhenAllStagesSucceed() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();

		// Act
		emitter.CompleteSuccess("Deployment completed", "http://localhost:40000/0", @"C:\inetpub\wwwroot\creatio");

		// Assert
		ClioStageEvent terminal = events.Last();
		terminal.EventType.Should().Be(ClioStageEventContract.EventTypes.RunCompleted,
			"because a completed run emits a terminal event");
		terminal.RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Success,
			"because all stages succeeded");
		terminal.RunCompleted!.Summary.Should().Be("Deployment completed", "because a friendly summary is provided");
		terminal.RunCompleted!.DerivedUrl.Should().Be("http://localhost:40000/0",
			"because the deployed application URL is known on success");
		terminal.RunCompleted!.DerivedPath.Should().Be(@"C:\inetpub\wwwroot\creatio",
			"because the install directory is known on success");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-13: the emitter redacts credentials from every string field so no secret reaches message, detail, or errorCode.")]
	public void RunStage_ShouldRejectSecrets_WhenStageThrowsWithCredentialInMessage() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();
		const string secret = "SuperSecret123";
		string connectionString =
			$"Server=db;Port=5432;Database=app;User ID=admin;password={secret};Redis password=RedisPass9 bearer Tok3nAbc";

		// Act
		Action act = () => emitter.RunStage(StageIds.RestoreDb, () => throw new InvalidOperationException(connectionString));

		// Assert
		act.Should().Throw<InvalidOperationException>("because the original exception still propagates");
		string allFields = string.Join("\n", events
			.Where(e => e.Stage is not null)
			.SelectMany(e => new[] { e.Stage.Message, e.Stage.Detail, e.Stage.ErrorCode })
			.Concat(events.Where(e => e.RunCompleted is not null)
				.SelectMany(e => new[] { e.RunCompleted.Summary, e.RunCompleted.Detail, e.RunCompleted.ErrorCode }))
			.Where(value => value is not null));
		allFields.Should().NotContain(secret, "because a database password must never reach an event field");
		allFields.Should().NotContain("RedisPass9", "because Redis credentials must never reach an event field");
		allFields.Should().NotContain("Tok3nAbc", "because bearer tokens must never reach an event field");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-14: every event of a run carries one stable runId and a strictly increasing sequence.")]
	public void StageChanged_ShouldYieldStableRunIdAndMonotonicSequence_WhenEventsAreEmitted() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();

		// Act
		emitter.RunStage(StageIds.StageBuild, () => { });
		emitter.RunStage(StageIds.Unzip, () => { });
		emitter.CompleteSuccess("Deployment completed");

		// Assert
		events.Select(e => e.RunId).Distinct().Should().ContainSingle(
			"because every event of one run carries the same runId");
		events.Select(e => e.Sequence).Should().Equal(Enumerable.Range(0, events.Count),
			"because the sequence is monotonically increasing per run starting at zero");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-15: a stage that throws with no subscriber attached still rethrows and never breaks on emission.")]
	public void RunStage_ShouldBeInertAndRethrow_WhenNoSubscriberAttached() {
		// Arrange
		StageEventEmitter emitter = new();
		emitter.Begin(ClioStageEventContract.Operations.Deploy, DeployManifest, sink: null);

		// Act
		Action act = () => emitter.RunStage(StageIds.RestoreDb, () => throw new InvalidOperationException("boom"));

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("boom",
				"because emission is observational: with no subscriber it neither swallows nor alters the stage exception");
	}

	// One case per deploy stage whose underlying action signals failure by a non-zero return (not a throw) and
	// was previously swallowed by Execute: restore-db, deploy-app, configure-conn-strings, register-env. The
	// second value is the stages that must be cascaded skipped after that stage fails.
	private static IEnumerable<TestCaseData> SwallowedResultFailurePaths() {
		yield return new TestCaseData(StageIds.RestoreDb,
			new[] { StageIds.DeployApp, StageIds.ConfigureConnStrings, StageIds.RegisterEnv, StageIds.WaitReady })
			.SetName("RunStage_ShouldFailCascadeAndReturnCode_WhenRestoreDbReturnsNonzero");
		yield return new TestCaseData(StageIds.DeployApp,
			new[] { StageIds.ConfigureConnStrings, StageIds.RegisterEnv, StageIds.WaitReady })
			.SetName("RunStage_ShouldFailCascadeAndReturnCode_WhenDeployAppReturnsNonzero");
		yield return new TestCaseData(StageIds.ConfigureConnStrings,
			new[] { StageIds.RegisterEnv, StageIds.WaitReady })
			.SetName("RunStage_ShouldFailCascadeAndReturnCode_WhenConfigureConnStringsReturnsNonzero");
		yield return new TestCaseData(StageIds.RegisterEnv,
			new[] { StageIds.WaitReady })
			.SetName("RunStage_ShouldFailCascadeAndReturnCode_WhenRegisterEnvReturnsNonzero");
	}

	[Test]
	[TestCaseSource(nameof(SwallowedResultFailurePaths))]
	[Category("Unit")]
	[Description("TC-U-16: a result-based deploy stage that returns a non-zero exit code is reported as an honest pipeline failure — the active stage is emitted failed, every remaining stage is cascaded skipped after-failure, the terminal run-completed carries outcome failure, and the real non-zero code is returned without throwing. This is the release-blocker guarantee: the guided pipeline can never show Done/Success over a failed underlying deploy action.")]
	public void RunStage_ShouldFailCascadeAndReturnCode_WhenResultStageReturnsNonzero(string failingStageId,
		string[] expectedSkippedStageIds) {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();
		// Drive every stage before the failing one to a successful (zero) completion, mirroring the deploy path.
		foreach (StageDescriptor descriptor in DeployManifest) {
			if (descriptor.StageId == failingStageId) {
				break;
			}

			emitter.RunStage(descriptor.StageId, () => 0);
		}

		int eventsBeforeFailure = events.Count;
		const int nonzeroExitCode = 7;

		// Act
		int returned = emitter.RunStage(failingStageId, () => nonzeroExitCode);

		// Assert
		returned.Should().Be(nonzeroExitCode,
			"because RunStage returns the real non-zero exit code (without throwing) so the deploy can stop with it");
		List<ClioStageEvent> tail = events.Skip(eventsBeforeFailure).ToList();
		ClioStageEvent failed = tail.Single(e => e.Stage?.Status == ClioStageEventContract.StageStatuses.Failed);
		failed.Stage!.StageId.Should().Be(failingStageId,
			"because the stage whose action returned non-zero is the one reported failed");
		failed.Stage!.ErrorCode.Should().NotBeNullOrEmpty(
			"because a failed stage carries a stable symbolic error code");
		failed.Stage!.Detail.Should().Contain(nonzeroExitCode.ToString(),
			"because the real exit code is surfaced as the failure detail");
		List<ClioStageEvent> skipped = tail
			.Where(e => e.Stage?.Status == ClioStageEventContract.StageStatuses.Skipped).ToList();
		skipped.Select(e => e.Stage!.StageId).Should().Equal(expectedSkippedStageIds,
			"because every stage after the failing one is cascaded skipped in manifest order");
		skipped.Should().OnlyContain(e => e.Stage!.SkipReason == ClioStageEventContract.SkipReasons.AfterFailure,
			"because a failure-cascade skip is tagged after-failure");
		tail.Last().EventType.Should().Be(ClioStageEventContract.EventTypes.RunCompleted,
			"because the terminal run-completed event closes the failure cascade");
		tail.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure,
			"because a swallowed result-failure must now end the run in failure, not success");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-17: a deploy whose every result-based stage returns zero still ends in success with no failed and no after-failure skip — the honest-failure fix does not regress a good deploy.")]
	public void RunStage_ShouldEndInSuccess_WhenEveryResultStageReturnsZero() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();

		// Act
		foreach (StageDescriptor descriptor in DeployManifest) {
			emitter.RunStage(descriptor.StageId, () => 0);
		}

		emitter.CompleteSuccess("Deployment completed");

		// Assert
		events.Should().NotContain(e => e.Stage != null && e.Stage.Status == ClioStageEventContract.StageStatuses.Failed,
			"because no stage failed when every result-based stage returned zero");
		events.Should().NotContain(
			e => e.Stage != null && e.Stage.SkipReason == ClioStageEventContract.SkipReasons.AfterFailure,
			"because nothing is cascaded when no stage fails");
		events.Last().EventType.Should().Be(ClioStageEventContract.EventTypes.RunCompleted,
			"because a completed run emits a terminal event");
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Success,
			"because a fully successful deploy still ends in success");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-18: a result-based stage that returns zero emits running then done and returns zero, exactly like the void success path.")]
	public void RunStage_ShouldEmitDoneAndReturnZero_WhenResultStageReturnsZero() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();

		// Act
		int returned = emitter.RunStage(StageIds.RestoreDb, () => 0);

		// Assert
		returned.Should().Be(0, "because a zero return is a successful stage");
		List<ClioStageEvent> stageEvents = events
			.Where(e => e.EventType == ClioStageEventContract.EventTypes.Stage).ToList();
		stageEvents.Should().HaveCount(2, "because a successful stage emits running then done");
		stageEvents[0].Stage!.Status.Should().Be(ClioStageEventContract.StageStatuses.Running,
			"because the stage first transitions to running");
		stageEvents[1].Stage!.Status.Should().Be(ClioStageEventContract.StageStatuses.Done,
			"because a zero-returning stage transitions to done, not failed");
	}
}
