using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClioLauncher.Ipc;
using ClioLauncher.ViewModels;
using FluentAssertions;
using NUnit.Framework;

namespace ClioLauncher.Tests;

/// <summary>
/// Unit tests for <see cref="DeploymentReceipt"/> (story 10, ADR D6): the per-run NDJSON receipt written
/// from the same authoritative <see cref="ClioStageEvent"/> stream the pipeline UI renders. Covers the
/// one-line-per-event shape plus the rolled-up summary (TC-U-58), byte-for-byte replay equality with the
/// UI model (TC-U-59, SM-03), the no-secret-on-disk invariant (TC-U-60, AC-12), and a failed run recording
/// the failed + skipped-after-failure stages and a non-success terminal outcome (TC-U-61, AC-ERR). Uses a
/// temp folder (no writes to the real logs directory) and never runs a live clio.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class DeploymentReceiptTests {
	private const int V = ClioStageEventContract.SchemaVersion;
	private static readonly string Deploy = ClioStageEventContract.Operations.Deploy;

	private string _logsFolder = string.Empty;
	private Guid _runId;
	private int _seq;

	[SetUp]
	public void SetUp() {
		_logsFolder = Path.Combine(Path.GetTempPath(), "clio-ring-receipt-" + Guid.NewGuid().ToString("N"));
		_runId = Guid.NewGuid();
		_seq = 0;
	}

	[TearDown]
	public void TearDown() {
		try {
			if (Directory.Exists(_logsFolder)) {
				Directory.Delete(_logsFolder, recursive: true);
			}
		}
		catch (IOException) {
			// Best-effort cleanup; a locked temp file must not fail the suite.
		}
	}

	// ---- synthetic event builders (mirror the pipeline VM tests) ----

	private ClioStageEvent Manifest(params ClioStageManifestEntry[] stages) =>
		new(V, ClioStageEventContract.EventTypes.Manifest, _runId, _seq++, Deploy, Stages: stages);

	private ClioStageEvent Stage(string stageId, string status, long? durationMs = null,
		string? message = null, string? detail = null, string? errorCode = null, string? skipReason = null) =>
		new(V, ClioStageEventContract.EventTypes.Stage, _runId, _seq++, Deploy,
			Stage: new ClioStageDetail(stageId, stageId, 0, 3, status,
				DurationMs: durationMs, Message: message ?? stageId, Detail: detail, ErrorCode: errorCode, SkipReason: skipReason));

	private ClioStageEvent RunCompleted(string outcome, string summary, string? detail = null,
		string? errorCode = null, string? derivedUrl = null) =>
		new(V, ClioStageEventContract.EventTypes.RunCompleted, _runId, _seq++, Deploy,
			RunCompleted: new ClioRunCompleted(outcome, summary, detail, errorCode, derivedUrl));

	private static ClioStageManifestEntry[] ThreeStageManifest() => new[] {
		new ClioStageManifestEntry("unzip", "Unzip distribution", 0, 3, false),
		new ClioStageManifestEntry("restore-db", "Restore database", 1, 3, false),
		new ClioStageManifestEntry("deploy-app", "Deploy application", 2, 3, false)
	};

	private List<ClioStageEvent> SuccessStream() => new() {
		Manifest(ThreeStageManifest()),
		Stage("unzip", ClioStageEventContract.StageStatuses.Running, message: "Unpacking…"),
		Stage("unzip", ClioStageEventContract.StageStatuses.Done, durationMs: 3200, message: "Unpacked"),
		Stage("restore-db", ClioStageEventContract.StageStatuses.Done, durationMs: 42000, message: "Restored"),
		Stage("deploy-app", ClioStageEventContract.StageStatuses.Done, durationMs: 5400, message: "Deployed"),
		RunCompleted(ClioStageEventContract.RunOutcomes.Success, "Deployment completed", derivedUrl: "http://localhost:40001")
	};

	private static string ReceiptPath(string folder, Guid runId) =>
		Path.Combine(folder, $"deploy-{runId:D}.ndjson");

	// ---- TC-U-58: one NDJSON line per event + a final rolled-up summary ----

	[Test]
	[Description("TC-U-58: the receipt is a per-run NDJSON file with one line per ClioStageEvent plus a final rolled-up summary.")]
	public void Observe_ShouldWriteOneNdjsonLinePerEventPlusSummary_WhenRunCompletes() {
		// Arrange — a receipt over a temp folder and a full success stream.
		var receipt = new DeploymentReceipt(_logsFolder);
		List<ClioStageEvent> stream = SuccessStream();

		// Act — feed the whole stream through the observer.
		foreach (ClioStageEvent evt in stream) {
			receipt.Observe(evt);
		}

		// Assert — the file exists, has exactly one event line per event plus one summary line, and the
		// summary rolls up the terminal outcome + per-stage outcomes.
		string path = ReceiptPath(_logsFolder, _runId);
		File.Exists(path).Should().BeTrue(because: "a per-run NDJSON receipt is created for the run");
		string[] lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
		lines.Length.Should().Be(stream.Count + 1,
			because: "one line is appended per event plus one final rolled-up summary line");

		IReadOnlyList<ClioStageEvent> replayed = DeploymentReceiptReader.ReadEvents(path);
		replayed.Should().HaveCount(stream.Count, because: "every event line replays back to a typed event, and the summary line is skipped");

		ReceiptSummary? summary = DeploymentReceiptReader.ReadSummary(path);
		summary.Should().NotBeNull(because: "run-completed flushes a rolled-up summary line");
		summary!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Success, because: "the terminal outcome is rolled up");
		summary.TotalDurationMs.Should().Be(3200 + 42000 + 5400, because: "the summary sums the per-stage durations");
		summary.Stages.Should().OnlyContain(s => s.Status == ClioStageEventContract.StageStatuses.Done,
			because: "every stage in this run finished successfully");
	}

	// ---- TC-U-59: replay-from-file equals the UI model byte-for-byte (SM-03) ----

	[Test]
	[Description("TC-U-59 (SM-03): a pipeline rebuilt from the receipt NDJSON matches the UI model per-stage outcome and duration, because both derive from the same stream.")]
	public void Replay_ShouldEqualUiModel_WhenReceiptReplayed() {
		// Arrange — a live UI pipeline with the receipt attached as a side-observer, driven by the stream.
		var uiPipeline = new DeployPipelineViewModel();
		var receipt = new DeploymentReceipt(_logsFolder);
		uiPipeline.StageEventObserved += receipt.Observe;
		foreach (ClioStageEvent evt in SuccessStream()) {
			uiPipeline.Ingest(evt);
		}

		// Act — replay the on-disk receipt into a fresh pipeline.
		var replayPipeline = new DeployPipelineViewModel();
		foreach (ClioStageEvent evt in DeploymentReceiptReader.ReadEvents(ReceiptPath(_logsFolder, _runId))) {
			replayPipeline.Ingest(evt);
		}

		// Assert — the replayed model matches the UI model step-for-step (id, state, duration, message).
		replayPipeline.RunState.Should().Be(uiPipeline.RunState, because: "the terminal outcome is reconstructed from the same stream");
		var ui = uiPipeline.Steps.Select(s => (s.StageId, s.State, s.DurationMs, s.Message)).ToList();
		var replay = replayPipeline.Steps.Select(s => (s.StageId, s.State, s.DurationMs, s.Message)).ToList();
		replay.Should().Equal(ui, because: "the receipt is the same stream, not a second derivation that could disagree (SM-03)");
	}

	// ---- TC-U-60: no secret material on disk (AC-12) ----

	[Test]
	[Description("TC-U-60 (AC-12): no connection string, credential, or token appears anywhere in the receipt on disk.")]
	public void Observe_ShouldContainNoSecret_WhenInspected() {
		// Arrange — a receipt and a normal (secret-free-at-source) stream.
		var receipt = new DeploymentReceipt(_logsFolder);

		// Act — write the run.
		foreach (ClioStageEvent evt in SuccessStream()) {
			receipt.Observe(evt);
		}

		// Assert — the on-disk text carries no credential-shaped markers (the Ring adds no secret material).
		string body = File.ReadAllText(ReceiptPath(_logsFolder, _runId));
		foreach (string marker in new[] { "password", "secret", "token", "credential", "pwd=", "user id=" }) {
			body.ToLowerInvariant().Should().NotContain(marker,
				because: $"the receipt must never carry the credential marker '{marker}' (redaction inherited from source, D3)");
		}
	}

	// ---- TC-U-61: a failed run records the failed + skipped-after-failure stages + non-success outcome ----

	[Test]
	[Description("TC-U-61 (AC-ERR): a failed run's receipt records the failed stage, the skipped-after-failure stages, and a run-completed outcome=failure.")]
	public void Observe_ShouldRecordNonSuccessOutcome_WhenRunFails() {
		// Arrange — a run that fails at restore-db, cascading the remaining stages to skipped(after-failure).
		var receipt = new DeploymentReceipt(_logsFolder);
		var stream = new List<ClioStageEvent> {
			Manifest(ThreeStageManifest()),
			Stage("unzip", ClioStageEventContract.StageStatuses.Done, durationMs: 3200, message: "Unpacked"),
			Stage("restore-db", ClioStageEventContract.StageStatuses.Failed, durationMs: 8800,
				message: "Could not restore the database", errorCode: "DB_RESTORE_FAILED"),
			Stage("deploy-app", ClioStageEventContract.StageStatuses.Skipped,
				skipReason: ClioStageEventContract.SkipReasons.AfterFailure, message: "Skipped after failure"),
			RunCompleted(ClioStageEventContract.RunOutcomes.Failure, "Deployment failed while restoring the database.",
				detail: "Stage 'restore-db' failed after 8.8s.", errorCode: "DB_RESTORE_FAILED")
		};

		// Act — write the failed run.
		foreach (ClioStageEvent evt in stream) {
			receipt.Observe(evt);
		}

		// Assert — the rolled-up summary records the failure, the failed stage and the skipped-after-failure stage.
		ReceiptSummary? summary = DeploymentReceiptReader.ReadSummary(ReceiptPath(_logsFolder, _runId));
		summary.Should().NotBeNull(because: "a failed run still flushes a summary");
		summary!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure, because: "the terminal outcome is a failure");
		summary.Stages.Single(s => s.StageId == "restore-db").Status.Should().Be(ClioStageEventContract.StageStatuses.Failed,
			because: "the failed stage is recorded as failed");
		summary.Stages.Single(s => s.StageId == "deploy-app").Status.Should().Be(ClioStageEventContract.StageStatuses.Skipped,
			because: "stages after the failure are recorded as skipped");
	}

	// ---- replay/summary separation: the summary line is never fed into replay ----

	[Test]
	[Description("Replay reads back ONLY the event lines and round-trips them; the trailing receiptSummary line is excluded from replay and read separately.")]
	public void ReadEvents_ShouldExcludeSummaryLineAndRoundTripEventsOnly_WhenReceiptHasSummary() {
		// Arrange — a full success run written to disk (event lines + one trailing receiptSummary line).
		var receipt = new DeploymentReceipt(_logsFolder);
		List<ClioStageEvent> stream = SuccessStream();
		foreach (ClioStageEvent evt in stream) {
			receipt.Observe(evt);
		}
		string path = ReceiptPath(_logsFolder, _runId);

		// Act — read the replayable events and the rolled-up summary as separate parts.
		IReadOnlyList<ClioStageEvent> replayed = DeploymentReceiptReader.ReadEvents(path);
		ReceiptSummary? summary = DeploymentReceiptReader.ReadSummary(path);

		// Assert — replay returns exactly the events (the summary line is not one of them) and every line
		// replayed carries an eventType, proving the summary is never fed into replay.
		replayed.Should().HaveCount(stream.Count, because: "only the event lines replay; the receiptSummary line is excluded");
		replayed.Select(e => e.EventType).Should().OnlyContain(t => !string.IsNullOrEmpty(t),
			because: "every replayed line is a typed event, never the summary wrapper");
		replayed[0].EventType.Should().Be(ClioStageEventContract.EventTypes.Manifest, because: "the first replayable line is the manifest");
		replayed[^1].EventType.Should().Be(ClioStageEventContract.EventTypes.RunCompleted, because: "the last replayable line is run-completed, not the summary");

		// Assert — the summary is still readable on its own from the same file.
		summary.Should().NotBeNull(because: "the trailing receiptSummary line is read by the summary reader, not the replay reader");
		summary!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Success, because: "the summary rolls up the terminal outcome");

		// Assert — the raw file genuinely contains a receiptSummary line (so exclusion is meaningful, not vacuous).
		File.ReadAllLines(path).Where(l => l.Length > 0)
			.Count(l => l.Contains("receiptSummary", StringComparison.Ordinal)).Should().Be(1,
				because: "exactly one trailing summary line exists on disk and it is the one replay excludes");
	}

	// ---- runId path containment (defence in depth): a receipt can never escape the logs folder ----

	[Test]
	[Description("A normal Guid run key resolves to a receipt path directly inside the logs folder.")]
	public void ResolveContainedReceiptPath_ShouldReturnPathInsideFolder_WhenRunKeyIsGuid() {
		// Arrange — a normal Guid run key (the real-world case).
		string runKey = Guid.NewGuid().ToString("D");

		// Act — resolve the contained receipt path.
		string? resolved = DeploymentReceipt.ResolveContainedReceiptPath(_logsFolder, Deploy, runKey);

		// Assert — the path is non-null and its parent directory is exactly the logs folder.
		resolved.Should().NotBeNull(because: "a Guid run key always resolves to a path inside the logs folder");
		Path.GetDirectoryName(Path.GetFullPath(resolved!)).Should().Be(Path.GetFullPath(_logsFolder),
			because: "the receipt is written directly inside the logs folder, never in a sub-path");
	}

	[Test]
	[Description("A run key that would traverse above the logs folder is rejected (null), and any non-null resolution stays directly inside the folder — a hostile key can never escape.")]
	public void ResolveContainedReceiptPath_ShouldRejectEscapeAndNeverResolveOutsideFolder_WhenRunKeyIsHostile() {
		// Arrange — adversarial run keys that a normal Guid could never be. Each has a real leading segment so
		// the '..' segments genuinely pop past the logs folder (no reliance on OS-specific dot-trimming).
		string[] escapingKeys = {
			@"x\..\..\evil",
			"x/../../evil",
			Path.Combine("x", "..", "..", "..", "escape")
		};
		// Keys with a separator that resolve to a SUB-path (not the folder itself) are also rejected as
		// "not directly inside".
		string[] subPathKeys = {
			@"x\evil",
			"x/evil"
		};
		string folderFull = Path.GetFullPath(_logsFolder);

		// Act + Assert — an escaping key is refused outright (null), never resolving above the logs folder.
		foreach (string key in escapingKeys) {
			DeploymentReceipt.ResolveContainedReceiptPath(_logsFolder, Deploy, key).Should().BeNull(
				because: $"a run key '{key}' that traverses above the logs folder must be refused, not written outside it");
		}

		// Act + Assert — a sub-path key is refused (the receipt must live DIRECTLY in the logs folder).
		foreach (string key in subPathKeys) {
			DeploymentReceipt.ResolveContainedReceiptPath(_logsFolder, Deploy, key).Should().BeNull(
				because: $"a run key '{key}' resolving to a sub-path is not directly inside the logs folder and is refused");
		}

		// Act + Assert — invariant: for ANY key, a non-null result is ALWAYS directly inside the logs folder.
		foreach (string key in escapingKeys.Concat(subPathKeys)) {
			string? resolved = DeploymentReceipt.ResolveContainedReceiptPath(_logsFolder, Deploy, key);
			if (resolved is not null) {
				Path.GetDirectoryName(Path.GetFullPath(resolved)).Should().Be(folderFull,
					because: $"a non-null resolution for '{key}' must never point outside the logs folder");
			}
		}
	}

	// ---- best-effort contract: a hostile call never throws ----

	[Test]
	[Description("Observe is a safe no-op for a null event and never throws for a degenerate stream — a diagnostic must never break a run.")]
	public void Observe_ShouldNotThrow_WhenEventIsNullOrDegenerate() {
		// Arrange — a receipt with no run started.
		var receipt = new DeploymentReceipt(_logsFolder);

		// Act — feed a null then a lone terminal event with no manifest.
		Action act = () => {
			receipt.Observe(null);
			receipt.Observe(RunCompleted(ClioStageEventContract.RunOutcomes.Success, "orphan terminal"));
		};

		// Assert — best-effort: never throws.
		act.Should().NotThrow(because: "the receipt is best-effort and must never break a run");
	}
}
