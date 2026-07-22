using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClioRing.Ipc;
using FluentAssertions;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>
/// Unit tests for the Ring-side structured progress path (story 6): the <see cref="ClioStageEvent"/>
/// mirror and the <see cref="ClioStageEventAdapter"/> that decodes <c>notifications/progress</c>
/// <c>_meta</c> payloads. The centrepiece is the cross-repo contract anchor (TC-U-30): clio's committed
/// NDJSON fixture, copied byte-identically here, must round-trip through the mirror without drift.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class ClioStageEventAdapterTests {
	// A synchronous sink so assertions never race a posted callback (unlike Progress<T>).
	private sealed class RecordingSink : IProgress<ClioStageEvent> {
		public List<ClioStageEvent> Events { get; } = new();
		public void Report(ClioStageEvent value) => Events.Add(value);
	}

	private static string FixturePath =>
		Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ClioStageEvent.contract.ndjson");

	private static IReadOnlyList<string> ReadFixtureLines() {
		string[] raw = File.ReadAllLines(FixturePath);
		var lines = new List<string>();
		foreach (string line in raw) {
			if (!string.IsNullOrWhiteSpace(line)) {
				lines.Add(line);
			}
		}
		return lines;
	}

	// Wraps an envelope JSON string into a realistic notifications/progress params object:
	// { "progressToken": <token>, "progress": .., "total": .., "_meta": { "clioStageEvent": {..} } }.
	private static JsonObject BuildProgressParams(string envelopeJson, string? progressToken) {
		var envelope = (JsonObject)JsonNode.Parse(envelopeJson)!;
		var meta = new JsonObject { ["clioStageEvent"] = envelope };
		var paramsObject = new JsonObject {
			["progress"] = 1,
			["total"] = 8,
			["message"] = "working",
			["_meta"] = meta
		};
		if (progressToken is not null) {
			paramsObject["progressToken"] = progressToken;
		}
		return paramsObject;
	}

	[Test]
	[Description("Each line of clio's committed NDJSON fixture deserializes into the Ring mirror and re-serializes to the exact same bytes, anchoring cross-repo contract parity (TC-U-30 / AC-01).")]
	public void Serialize_ShouldMatchFixtureBytes_WhenRoundTrippingEachContractLine() {
		// Arrange — the byte-identical copy of clio's canonical fixture (3 NDJSON envelopes).
		IReadOnlyList<string> lines = ReadFixtureLines();
		lines.Should().HaveCount(3, because: "the contract fixture carries one manifest, one stage, and one run-completed line");

		// Act + Assert — every line round-trips through the mirror with the canonical options.
		foreach (string line in lines) {
			ClioStageEvent? decoded = JsonSerializer.Deserialize<ClioStageEvent>(line, ClioStageEventContract.SerializerOptions);
			decoded.Should().NotBeNull(because: "every fixture line is a valid ClioStageEvent envelope");

			string reserialized = JsonSerializer.Serialize(decoded!, ClioStageEventContract.SerializerOptions);
			reserialized.Should().Be(line,
				because: "the mirror must serialize byte-for-byte identically to clio so the contract cannot silently drift");
		}
	}

	[Test]
	[Description("The manifest fixture line maps every field correctly into the mirror (schemaVersion, discriminator, runId, sequence, operation and the ordered stage list with the conditional flag).")]
	public void Deserialize_ShouldMapAllManifestFields_WhenGivenManifestFixtureLine() {
		// Arrange — the manifest line is the first NDJSON envelope.
		string manifestLine = ReadFixtureLines()[0];

		// Act — decode it into the mirror.
		ClioStageEvent evt = JsonSerializer.Deserialize<ClioStageEvent>(manifestLine, ClioStageEventContract.SerializerOptions)!;

		// Assert — the discriminator, scalar fields and manifest entries are all present and typed.
		evt.SchemaVersion.Should().Be(1, because: "the fixture pins schemaVersion 1");
		evt.EventType.Should().Be(ClioStageEventContract.EventTypes.Manifest, because: "the first line is the manifest event");
		evt.Operation.Should().Be(ClioStageEventContract.Operations.Deploy, because: "the fixture describes a deploy run");
		evt.Sequence.Should().Be(0, because: "the manifest is the first event in the run");
		evt.Stages.Should().NotBeNull(because: "a manifest event carries the stage list").And.HaveCount(8, because: "the fixture deploy manifest has eight stages");
		evt.Stages![0].StageId.Should().Be("stage-build", because: "the first stage is the conditional build stage");
		evt.Stages[0].Conditional.Should().BeTrue(because: "stage-build is inert for a non-network source and is flagged conditional");
	}

	[Test]
	[Description("A progress notification carrying a populated _meta.clioStageEvent is decoded and raised as a typed event via the raw handler path, not the _meta-dropping IProgress<ProgressNotificationValue> path (AC-02).")]
	public void Consume_ShouldRaiseTypedEvent_WhenMetaCarriesClioStageEvent() {
		// Arrange — an adapter with no token filter and a notification wrapping the stage fixture line.
		var sink = new RecordingSink();
		var adapter = new ClioStageEventAdapter(sink);
		string manifestJson = ReadFixtureLines()[0];
		ClioStageEvent manifest = JsonSerializer.Deserialize<ClioStageEvent>(
			manifestJson, ClioStageEventContract.SerializerOptions)!;
		JsonObject manifestParams = BuildProgressParams(manifestJson, progressToken: null);
		JsonObject progressParams = StageParams(manifest.RunId, sequence: 1);

		// Act — establish the ordered run with its manifest, then feed the stage notification.
		adapter.Consume(manifestParams);
		ClioStageEvent? raised = adapter.Consume(progressParams);

		// Assert — the typed stage event surfaces on the sink.
		raised.Should().NotBeNull(because: "a populated _meta.clioStageEvent yields a typed event");
		sink.Events.Should().HaveCount(2, because: "the manifest and following stage form a contiguous ordered stream");
		sink.Events[1].EventType.Should().Be(ClioStageEventContract.EventTypes.Stage, because: "the fixture line 2 is a stage transition");
		sink.Events[1].Stage!.StageId.Should().Be("restore-db", because: "the stage envelope names the restore-db stage");
	}

	[Test]
	[Description("An envelope carrying an unknown extra field is tolerated: the known fields still decode and the event is raised with no throw (FR-12 / AC-03 / AC-11).")]
	public void Consume_ShouldTolerateUnknownField_WhenEnvelopeHasExtraProperty() {
		// Arrange — a stage envelope with a bogus extra field a future clio version might add.
		var envelope = (JsonObject)JsonNode.Parse(ReadFixtureLines()[1])!;
		envelope["futureFieldNotInMirror"] = "ignore-me";
		var sink = new RecordingSink();
		var adapter = new ClioStageEventAdapter(sink);
		var meta = new JsonObject { ["clioStageEvent"] = envelope };
		var progressParams = new JsonObject { ["_meta"] = meta };

		// Act — consuming must not throw on the unknown member.
		ClioStageEvent? raised = null;
		Action act = () => raised = adapter.Consume(progressParams);

		// Assert — the event is decoded despite the unknown field.
		act.Should().NotThrow(because: "System.Text.Json skips unknown members so the contract is forward-compatible");
		raised.Should().NotBeNull(because: "the known fields still form a valid envelope");
		raised!.Stage!.StageId.Should().Be("restore-db", because: "the mapped fields are unaffected by the extra member");
	}

	[Test]
	[Description("Concurrent out-of-order callbacks are de-duplicated and delivered to the sink in producer sequence beginning with the manifest (AC-04 / AC-11).")]
	public void Consume_ShouldDropExactDuplicateAndRestoreProducerOrder_WhenCallbacksArriveOutOfOrder() {
		// Arrange — three events for one run: seq 3, a duplicate seq 3, then the late seq 0 manifest slot.
		var sink = new RecordingSink();
		var adapter = new ClioStageEventAdapter(sink);
		var runId = Guid.NewGuid();
		JsonObject First = StageParams(runId, sequence: 3);
		JsonObject Duplicate = StageParams(runId, sequence: 3);
		JsonObject OutOfOrder = StageParams(runId, sequence: 0);
		JsonObject Second = StageParams(runId, sequence: 1);
		JsonObject Third = StageParams(runId, sequence: 2);

		// Act — feed them in arrival order; none must throw.
		Action act = () => {
			adapter.Consume(First);
			adapter.Consume(Duplicate);
			adapter.Consume(OutOfOrder);
			adapter.Consume(Second);
			adapter.Consume(Third);
		};

		// Assert — the duplicate is dropped and the sink observes producer order, not callback order.
		act.Should().NotThrow(because: "a noisy or reordered stream must never crash the consumer");
		sink.Events.Select(e => e.Sequence).Should().Equal(new[] { 0, 1, 2, 3 },
			because: "the pipeline must receive the manifest, stages, and terminal in producer order exactly once");
	}

	[Test]
	[Description("A sequence gap is buffered until the missing event arrives so a later terminal cannot overtake an earlier stage (AC-04).")]
	public void Consume_ShouldBufferLaterEvent_UntilSequenceGapIsFilled() {
		// Arrange — two events for one run with a hole between them (0 then 3).
		var sink = new RecordingSink();
		var adapter = new ClioStageEventAdapter(sink);
		var runId = Guid.NewGuid();

		// Act — the second event skips ahead past missing sequences.
		adapter.Consume(StageParams(runId, sequence: 0));
		adapter.Consume(StageParams(runId, sequence: 3));

		// Assert — only the manifest is released until the gap is filled.
		sink.Events.Select(e => e.Sequence).Should().Equal(new[] { 0 },
			because: "seq=3 must not overtake the missing seq=1 and seq=2 events");

		// Act — fill the gap.
		adapter.Consume(StageParams(runId, sequence: 1));
		adapter.Consume(StageParams(runId, sequence: 2));

		// Assert — the buffered event is released after the contiguous prefix catches up.
		sink.Events.Select(e => e.Sequence).Should().Equal(new[] { 0, 1, 2, 3 },
			because: "all unique events must reach the sink in producer order once the gap closes");
	}

	[Test]
	[Description("When a progressToken is expected, a notification carrying a foreign token is ignored so concurrent runs never cross-contaminate (AC-05).")]
	public void Consume_ShouldIgnoreForeignRun_WhenProgressTokenDoesNotMatch() {
		// Arrange — an adapter bound to one token, fed a notification stamped with a different token.
		var sink = new RecordingSink();
		var adapter = new ClioStageEventAdapter(sink, expectedProgressToken: "run-mine");
		JsonObject foreign = BuildProgressParams(ReadFixtureLines()[1], progressToken: "run-other");

		// Act — consume the foreign-run notification.
		ClioStageEvent? raised = adapter.Consume(foreign);

		// Assert — nothing is raised for a token that is not ours.
		raised.Should().BeNull(because: "events are correlated strictly by progressToken to isolate concurrent calls");
		sink.Events.Should().BeEmpty(because: "a foreign run must not reach this call's sink");
	}

	[Test]
	[Description("A notification whose _meta is absent, or whose clioStageEvent is malformed, is skipped safely with no throw and no fabricated event (AC-ERR).")]
	public void Consume_ShouldSkipSafely_WhenMetaIsAbsentOrMalformed() {
		// Arrange — three degenerate notifications and an adapter with no token filter.
		var sink = new RecordingSink();
		var adapter = new ClioStageEventAdapter(sink);
		var noMeta = new JsonObject { ["progress"] = 1, ["message"] = "no meta here" };
		var emptyMeta = new JsonObject { ["_meta"] = new JsonObject() };
		var malformed = new JsonObject { ["_meta"] = new JsonObject { ["clioStageEvent"] = new JsonObject { ["sequence"] = "not-a-number" } } };

		// Act — consume all three plus a null params node.
		ClioStageEvent? r1 = null, r2 = null, r3 = null, r4 = null;
		Action act = () => {
			r1 = adapter.Consume(noMeta);
			r2 = adapter.Consume(emptyMeta);
			r3 = adapter.Consume(malformed);
			r4 = adapter.Consume(null);
		};

		// Assert — every degenerate input is a silent skip, never a throw or a fabricated event.
		act.Should().NotThrow(because: "a bad or empty progress beat must never crash a deploy");
		new[] { r1, r2, r3, r4 }.Should().AllSatisfy(
			r => r.Should().BeNull(because: "no valid envelope means no event is raised"));
		sink.Events.Should().BeEmpty(because: "nothing valid was decoded from any of the degenerate notifications");
	}

	// Builds a minimal valid stage-event notification params for a given run + sequence.
	private static JsonObject StageParams(Guid runId, int sequence) {
		var envelope = new JsonObject {
			["schemaVersion"] = 1,
			["eventType"] = ClioStageEventContract.EventTypes.Stage,
			["runId"] = runId.ToString(),
			["sequence"] = sequence,
			["operation"] = ClioStageEventContract.Operations.Deploy,
			["stage"] = new JsonObject {
				["stageId"] = "restore-db",
				["name"] = "Restore database",
				["index"] = 3,
				["total"] = 8,
				["status"] = ClioStageEventContract.StageStatuses.Running,
				["message"] = "Restore database"
			}
		};
		return new JsonObject { ["_meta"] = new JsonObject { ["clioStageEvent"] = envelope } };
	}
}
