using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.McpServer.Progress;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class StageEventProgressForwarderTests {

	private static readonly ProgressToken Token = new("progress-1");

	[Test]
	[Category("Unit")]
	[Description("Forwards each ClioStageEvent raised by the source as a notifications/progress carrying the serialized event in _meta.clioStageEvent, with the caller's progress token.")]
	public void Subscribe_ShouldForwardEachEventWithTypedMetaEnvelope_WhenProgressTokenIsPresent() {
		// Arrange
		FakeStageEventSource source = new();
		StageEventProgressForwarder forwarder = new();
		List<ProgressNotificationParams> captured = [];
		using IDisposable subscription = forwarder.Subscribe(source, Token, captured.Add);
		StageEventEmitter emitter = new();

		// Act — a clean run: manifest, unzip running/done, terminal success.
		emitter.Begin(ClioStageEventContract.Operations.Deploy, Descriptors(), source.Raise);
		emitter.RunStage(StageIds.Unzip, () => { });
		emitter.SkipStage(StageIds.CopyFiles, ClioStageEventContract.SkipReasons.NotApplicable);
		emitter.CompleteSuccess("Creatio deployed");

		// Assert
		captured.Should().HaveCount(5,
			because: "every raised stage event (manifest, running, done, skipped, run-completed) must be forwarded");
		captured.Should().OnlyContain(p => p.ProgressToken.Equals(Token),
			because: "every forwarded notification must carry the caller's progress token");
		captured.Should().OnlyContain(p => MetaEvent(p) != null,
			because: "every forwarded notification must carry the typed envelope in _meta.clioStageEvent");
		captured.Should().OnlyContain(p => MetaEvent(p)!.SchemaVersion == ClioStageEventContract.SchemaVersion,
			because: "the _meta envelope must be the versioned ClioStageEvent contract (SchemaVersion=1)");

		ClioStageEvent manifest = MetaEvent(captured[0])!;
		manifest.EventType.Should().Be(ClioStageEventContract.EventTypes.Manifest,
			because: "the first forwarded event must be the up-front manifest");
		captured[0].Progress!.Total.Should().Be(2,
			because: "the manifest establishes the stable progress-bar denominator (two stages)");

		ClioStageEvent terminal = MetaEvent(captured[^1])!;
		terminal.EventType.Should().Be(ClioStageEventContract.EventTypes.RunCompleted,
			because: "the last forwarded event must be the terminal run-completed");
		terminal.RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Success,
			because: "a clean run must complete with a success outcome");
		captured[^1].Progress!.Message.Should().Be("Creatio deployed",
			because: "the terminal progress message should carry the run summary");
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the full failed→skipped-cascade→run-completed sequence when a stage THROWS, with the stage-execution-failed error code in the terminal _meta envelope.")]
	public void Subscribe_ShouldForwardFailureCascade_WhenStageThrows() {
		// Arrange
		FakeStageEventSource source = new();
		StageEventProgressForwarder forwarder = new();
		List<ProgressNotificationParams> captured = [];
		using IDisposable subscription = forwarder.Subscribe(source, Token, captured.Add);
		StageEventEmitter emitter = new();

		// Act — the thrown-stage failure mode: RunStage(Action) rethrows after emitting failure + cascade.
		emitter.Begin(ClioStageEventContract.Operations.Deploy, Descriptors(), source.Raise);
		Action act = () => emitter.RunStage(StageIds.Unzip, () => throw new InvalidOperationException("bad archive"));
		act.Should().Throw<InvalidOperationException>(
			because: "the thrown-stage path must rethrow so the caller's control flow is unchanged");

		// Assert
		IReadOnlyList<ClioStageEvent> events = Envelopes(captured);
		events.Should().SatisfyRespectively(
			manifest => manifest.EventType.Should().Be(ClioStageEventContract.EventTypes.Manifest,
				because: "the manifest is forwarded first"),
			running => running.Stage!.Status.Should().Be(ClioStageEventContract.StageStatuses.Running,
				because: "the failing stage first transitions to running"),
			failed => failed.Stage!.Status.Should().Be(ClioStageEventContract.StageStatuses.Failed,
				because: "a thrown stage transitions to failed"),
			skipped => skipped.Stage!.SkipReason.Should().Be(ClioStageEventContract.SkipReasons.AfterFailure,
				because: "the remaining stage is cascaded as skipped after the failure"),
			completed => completed.RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure,
				because: "the run terminates in failure"));
		events[2].Stage!.ErrorCode.Should().Be("stage-execution-failed",
			because: "a thrown stage must carry the stage-execution-failed error code");
		events[^1].RunCompleted!.ErrorCode.Should().Be("stage-execution-failed",
			because: "the terminal run-completed must surface the failing stage's error code");
		events[^1].RunCompleted!.Summary.Should().NotBeNullOrWhiteSpace(
			because: "the terminal event must carry a friendly summary (AC-ERR)");
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the full failed→skipped-cascade→run-completed sequence when a result-based stage returns a non-zero exit code, with the stage-returned-nonzero error code in the terminal _meta envelope.")]
	public void Subscribe_ShouldForwardFailureCascade_WhenResultBasedStageReturnsNonZero() {
		// Arrange
		FakeStageEventSource source = new();
		StageEventProgressForwarder forwarder = new();
		List<ProgressNotificationParams> captured = [];
		using IDisposable subscription = forwarder.Subscribe(source, Token, captured.Add);
		StageEventEmitter emitter = new();

		// Act — the result-based failure mode: RunStage(Func<int>) returns non-zero WITHOUT throwing.
		emitter.Begin(ClioStageEventContract.Operations.Deploy, Descriptors(), source.Raise);
		int exitCode = emitter.RunStage(StageIds.Unzip, () => 5);

		// Assert
		exitCode.Should().Be(5,
			because: "the result-based path must return the real non-zero exit code instead of throwing");
		IReadOnlyList<ClioStageEvent> events = Envelopes(captured);
		events[2].Stage!.Status.Should().Be(ClioStageEventContract.StageStatuses.Failed,
			because: "a non-zero-returning stage is an honest failure");
		events[2].Stage!.ErrorCode.Should().Be("stage-returned-nonzero",
			because: "a non-zero-return stage must carry the stage-returned-nonzero error code");
		events[3].Stage!.SkipReason.Should().Be(ClioStageEventContract.SkipReasons.AfterFailure,
			because: "the remaining stage is cascaded as skipped after the non-zero return");
		events[^1].RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure,
			because: "a non-zero-return stage must terminate the run in failure, not success");
		events[^1].RunCompleted!.ErrorCode.Should().Be("stage-returned-nonzero",
			because: "the terminal run-completed must surface the non-zero-return error code");
	}

	[Test]
	[Category("Unit")]
	[Description("Sends nothing and does not subscribe to the source when the caller supplied no progress token, preserving byte-for-byte behavior for non-progress clients.")]
	public void Subscribe_ShouldForwardNothing_WhenProgressTokenIsNull() {
		// Arrange
		FakeStageEventSource source = new();
		StageEventProgressForwarder forwarder = new();
		List<ProgressNotificationParams> captured = [];

		// Act
		using IDisposable subscription = forwarder.Subscribe(source, progressToken: null, captured.Add);
		source.Raise(ManifestEvent());

		// Assert
		source.HandlerCount.Should().Be(0,
			because: "with no progress token the forwarder must not attach a handler to the source");
		captured.Should().BeEmpty(
			because: "with no progress token no progress notification may be sent");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops forwarding once the subscription is disposed, so no late event reaches the client after the run completes.")]
	public void Subscribe_ShouldStopForwarding_WhenSubscriptionDisposed() {
		// Arrange
		FakeStageEventSource source = new();
		StageEventProgressForwarder forwarder = new();
		List<ProgressNotificationParams> captured = [];
		IDisposable subscription = forwarder.Subscribe(source, Token, captured.Add);

		// Act
		source.Raise(ManifestEvent());
		subscription.Dispose();
		source.Raise(ManifestEvent());

		// Assert
		captured.Should().HaveCount(1,
			because: "events raised after disposal must not be forwarded (handler detached)");
		source.HandlerCount.Should().Be(0,
			because: "disposing the subscription must unsubscribe the handler from the source");
	}

	[Test]
	[Category("Unit")]
	[Description("Swallows a failure from the send callback so a broken progress channel never breaks the deploy/uninstall operation.")]
	public void Subscribe_ShouldSwallowSendFailures_WhenSendThrows() {
		// Arrange
		FakeStageEventSource source = new();
		StageEventProgressForwarder forwarder = new();
		using IDisposable subscription = forwarder.Subscribe(source, Token,
			_ => throw new InvalidOperationException("channel down"));

		// Act
		Action act = () => source.Raise(ManifestEvent());

		// Assert
		act.Should().NotThrow(
			because: "forwarding progress must never surface a fault into the deploy/uninstall operation");
	}

	private static StageDescriptor[] Descriptors() => [
		new StageDescriptor(StageIds.Unzip, "Unzip", false),
		new StageDescriptor(StageIds.CopyFiles, "Copy files", false)
	];

	private static ClioStageEvent ManifestEvent() => new(
		ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.Manifest,
		Guid.NewGuid(), 0, ClioStageEventContract.Operations.Deploy,
		Stages: [new ClioStageManifestEntry(StageIds.Unzip, "Unzip", 0, 1, false)]);

	private static ClioStageEvent MetaEvent(ProgressNotificationParams notification) =>
		notification.Meta!["clioStageEvent"].Deserialize<ClioStageEvent>(ClioStageEventContract.SerializerOptions);

	private static IReadOnlyList<ClioStageEvent> Envelopes(IEnumerable<ProgressNotificationParams> captured) {
		List<ClioStageEvent> events = [];
		foreach (ProgressNotificationParams notification in captured) {
			events.Add(MetaEvent(notification));
		}

		return events;
	}

	// Minimal IStageEventSource test double that lets a test raise the typed stream directly and
	// reports how many handlers are attached (to prove subscribe/unsubscribe behavior).
	private sealed class FakeStageEventSource : IStageEventSource {
		private EventHandler<ClioStageEvent> _handlers;

		public event EventHandler<ClioStageEvent> StageChanged {
			add => _handlers += value;
			remove => _handlers -= value;
		}

		public int HandlerCount => _handlers?.GetInvocationList().Length ?? 0;

		public void Raise(ClioStageEvent stageEvent) => _handlers?.Invoke(this, stageEvent);
	}
}
