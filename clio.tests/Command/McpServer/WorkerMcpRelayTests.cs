using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Clio.Command.McpServer.Progress;
using Clio.Command.McpServer.Relay;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit coverage for the full-duplex worker relay (ENG-95262 Stage 4a). Everything here runs against a
/// FAKE child transport, because the properties under test fail SILENTLY in production: a dropped sampling
/// round trip only makes the page review quietly worse, and reordered notifications are dropped by
/// ClioRing's ordinal correlation with no error anywhere. A live Creatio could not distinguish either.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public class WorkerMcpRelayTests {

	private const string ProgressNotificationMethod = "notifications/progress";
	private const string CanonicalRunId = "8a1b0c2d-3e4f-4a6b-8c8d-9e0f1a2b3c4d";
	private static readonly TimeSpan AssertionTimeout = TimeSpan.FromSeconds(10);

	/// <summary>
	/// Every IL opcode by its numeric value, so instruction lengths are decoded rather than guessed.
	/// </summary>
	private static readonly IReadOnlyDictionary<short, OpCode> KnownOpCodes = BuildOpCodeTable();

	private ILogger _logger;

	[SetUp]
	public void SetUp() => _logger = Substitute.For<ILogger>();

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: the relay does not reference the SDK client — no McpClient, no client notification handlers — because that dispatch layer is what reorders notifications.")]
	public void RelayTypes_ShouldNotReferenceTheSdkClient_WhenInspectedStructurally() {
		// Arrange
		Type[] relayTypes = [
			.. typeof(IWorkerMcpRelay).Assembly.GetTypes()
				.Where(type => type.Namespace == typeof(IWorkerMcpRelay).Namespace)
		];
		Type[] forbidden = [
			typeof(ModelContextProtocol.Client.McpClient),
			typeof(ModelContextProtocol.Client.McpClientHandlers)
		];

		// Act
		// Matched through the type GRAPH rather than by exact equality: an awaited
		// `McpClient.CreateAsync(...)` whose result never survives an await leaves no McpClient anywhere in a
		// signature — only a `TaskAwaiter<McpClient>` field on the async state machine — and exact equality
		// walks straight past it.
		List<string> offenders = [
			.. relayTypes.SelectMany(SignatureTypes)
				.Where(pair => ReferencesForbiddenType(pair.Type, forbidden))
				.Select(pair => pair.Member)
		];

		// Assert
		relayTypes.Should().NotBeEmpty("because the assertion is worthless if it inspects nothing");
		offenders.Should().BeEmpty(
			"because owning the transport read loop is the whole point: McpClient installs the concurrent "
			+ "notification dispatch that reordered 0..5 into [5,4,2,3,0,1], and no parent-side queue fixes it");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-404: the guard reads METHOD BODIES, so a relay type that only CALLS McpClient — a local that is never stored, never returned and never a parameter — fails it. The signature scan alone cannot see that shape, and it is the exact shape a future implementer would write.")]
	public void RelayMethodBodies_ShouldNotCallTheSdkClient_WhenTheirIlIsScanned() {
		// Arrange
		Type[] relayTypes = [
			.. typeof(IWorkerMcpRelay).Assembly.GetTypes()
				.Where(type => type.Namespace == typeof(IWorkerMcpRelay).Namespace)
		];
		Type[] forbidden = [
			typeof(ModelContextProtocol.Client.McpClient),
			typeof(ModelContextProtocol.Client.McpClientHandlers)
		];

		// Act
		IReadOnlyList<string> relayOffenders = MethodBodyReferencesTo(relayTypes, forbidden);
		// The planted offender is a SEPARATE scan on purpose: the production guard is scoped to clio's relay
		// namespace, so it cannot see a fixture declared in clio.tests — and scoping it wider would turn a
		// specific rule into a repository-wide ban that someone will suppress.
		IReadOnlyList<string> plantedOffenders =
			MethodBodyReferencesTo([typeof(SdkClientBodyOnlyOffender)], forbidden);

		// Assert
		relayTypes.Should().NotBeEmpty("because the assertion is worthless if it inspects nothing");
		plantedOffenders.Should().NotBeEmpty(
			"because a guard nobody has ever seen fail is a guard nobody knows the shape of: this fixture "
			+ "contains one non-hoisted `_ = McpClient.CreateAsync(transport)` inside an async method, which "
			+ "leaves NOTHING in any field, property, parameter or return type — so it is precisely what the "
			+ "signature scan misses, and the evidence stays in the tree instead of in a throwaway commit");
		relayOffenders.Should().BeEmpty(
			"because owning the transport read loop is the whole point: McpClient installs the concurrent "
			+ "notification dispatch that reordered 0..5 into [5,4,2,3,0,1], and no parent-side queue fixes it");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: the relay is the single consumer of the child's MessageReader, taking one channel reader for one loop.")]
	public async Task OpenAsync_ShouldDrainTheChildMessageReaderExactlyOnce_WhenTheSessionIsOpened() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);

		// Act
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);

		// Assert
		transport.MessageReaderReads.Should().Be(1,
			"because MessageReader is a ChannelReader and therefore effectively single-consumer — a second "
			+ "reader would steal messages from the relay's loop");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: the handshake goes straight to initialize and then notifications/initialized, never probing server/discover and never pinging.")]
	public async Task OpenAsync_ShouldSendInitializeAndInitialized_WhenTheWorkerAnswersTheHandshake() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);

		// Act
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);

		// Assert
		transport.SentMethods.Should().Equal(["initialize", "notifications/initialized"],
			"because a hand-rolled client leg owns the handshake itself");
		transport.SentMethods.Should().NotContain("server/discover",
			"because a child answering that probe with a success result of the wrong shape stalls the "
			+ "handshake for the full 5 s discover timeout and then hard-fails instead of falling back");
		transport.SentMethods.Should().NotContain("ping",
			"because ping is not served on protocol revision 2026-07-28");
		session.NegotiatedProtocolVersion.Should().Be(WorkerRelayOptions.MeasuredProtocolVersion,
			"because the negotiated revision is read from the worker's own initialize result");
		JsonNode initializeParams = transport.SentRequests.Single(request => request.Method == "initialize").Params;
		initializeParams["capabilities"]["sampling"].Should().NotBeNull(
			"because the child must be told the real client can serve sampling, or it will not ask");
		initializeParams["protocolVersion"].GetValue<string>()
			.Should().Be(WorkerRelayOptions.MeasuredProtocolVersion,
				"because the requested revision is the one the relay measurements were taken on");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: sampling is advertised to the worker only when the real client advertised it, so the child fails fast instead of asking for a capability nobody can serve.")]
	public async Task OpenAsync_ShouldNotAdvertiseSampling_WhenTheClientHasNoSamplingCapability() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new() { SupportsSampling = false };
		WorkerMcpRelay relay = new(_logger);

		// Act
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);

		// Assert
		JsonNode initializeParams = transport.SentRequests.Single(request => request.Method == "initialize").Params;
		initializeParams["capabilities"]["sampling"].Should().BeNull(
			"because mirroring a capability the client does not have buys a wasted round trip and an error");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: notifications reach the client in the worker's own wire order, even when the earliest event is the slowest to send.")]
	public async Task ReadLoop_ShouldRelayNotificationsInWireOrder_WhenTheEarliestEventIsTheSlowestToSend() {
		// Arrange
		const int eventCount = 6;
		FakeChildTransport transport = new();
		// Delays in REVERSE sequence order: a relay that forwarded concurrently (which is what the SDK's
		// client notification dispatch does) would record the LAST event first. Serial forwarding off one
		// read loop cannot be reordered by any delay, so this is a discriminator and not a smoke test.
		RecordingParentSession parent = new() {
			BeforeSend = async notification => await Task.Delay(
				TimeSpan.FromMilliseconds((eventCount - SequenceOf(notification)) * 20), CancellationToken.None)
		};
		WorkerMcpRelay relay = new(_logger);
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);

		// Act
		for (int sequence = 0; sequence < eventCount; sequence++) {
			transport.EmitFromChild(StageProgressNotification(sequence, new ProgressToken("run-token")));
		}
		await WaitUntilAsync(() => parent.Notifications.Count == eventCount);

		// Assert
		parent.Notifications.Select(SequenceOf).Should().Equal([0, 1, 2, 3, 4, 5],
			"because the relay takes messages off the pipe serially and awaits each forward, so the client "
			+ "observes the worker's own order — ordered replay is part of the stage-event contract and "
			+ "clients other than ClioRing have no reorder buffer");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: a forwarder-produced stage event arrives byte-identical, with its _meta.clioStageEvent subtree never rebuilt.")]
	public async Task ReadLoop_ShouldRelayStageEventMetaByteIdentically_WhenTheForwarderProducedTheEnvelope() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);
		ClioStageEvent stageEvent = CanonicalStageEvent(3);
		// The expectation is produced INDEPENDENTLY by the reference producer —
		// StageEventProgressForwarder.ToProgressNotification is the only builder of Meta["clioStageEvent"]
		// in clio — and rendered by the same JsonNode writer as the relayed value, so the comparison is
		// about content rather than about which serializer escaped what.
		JsonNode expectedPayload = ForwarderProducedPayload(stageEvent);
		string expectedJson = expectedPayload.ToJsonString();
		string expectedStageEventJson = expectedPayload["_meta"]["clioStageEvent"].ToJsonString();
		JsonNode payload = ForwarderProducedPayload(stageEvent);
		JsonRpcNotification fromChild = new() { Method = ProgressNotificationMethod, Params = payload };

		// Act
		transport.EmitFromChild(fromChild);
		await WaitUntilAsync(() => parent.Notifications.Count == 1);

		// Assert
		JsonRpcNotification relayed = parent.Notifications[0];
		relayed.Method.Should().Be(ProgressNotificationMethod,
			"because ClioRing matches the method name before it looks at anything else");
		relayed.Params.ToJsonString().Should().Be(expectedJson,
			"because a rebuild from typed DTOs silently drops whatever _meta keys the DTO does not know");
		relayed.Params["_meta"]["clioStageEvent"].ToJsonString().Should().Be(expectedStageEventJson,
			"because the typed stage-event envelope is the cross-repo contract the Ring deserializes");
		JsonSerializer.Serialize(
				JsonSerializer.Deserialize<ClioStageEvent>(relayed.Params["_meta"]["clioStageEvent"],
					ClioStageEventContract.SerializerOptions),
				ClioStageEventContract.SerializerOptions)
			.Should().Be(JsonSerializer.Serialize(stageEvent, ClioStageEventContract.SerializerOptions),
				"because what the Ring finally deserializes must be the event the worker emitted, field for field");
		relayed.Params.Should().BeSameAs(payload,
			"because the Params subtree is handed over as-is; anything else is a rebuild by another name");
		relayed.Should().NotBeSameAs(fromChild,
			"because the envelope is fresh — the incoming message's context points at the CHILD transport "
			+ "and must not travel up to the real client");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: a numeric progressToken stays a JSON number, because ClioRing compares the token ordinally and a retyped token is dropped silently.")]
	public async Task ReadLoop_ShouldPreserveANumericProgressToken_WhenTheClientIssuedOne() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);

		// Act
		transport.EmitFromChild(StageProgressNotification(0, new ProgressToken(42L)));
		await WaitUntilAsync(() => parent.Notifications.Count == 1);

		// Assert
		string relayedJson = parent.Notifications[0].Params.ToJsonString();
		relayedJson.Should().Contain("\"progressToken\":42",
			"because the token the client issued was a number and the correlation is ordinal");
		relayedJson.Should().NotContain("\"progressToken\":\"42\"",
			"because a number retyped as a string no longer matches the client's own token");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: tools/call carries the caller's _meta verbatim, so the caller's progress token — not one of the parent's making — reaches the worker.")]
	public async Task CallToolAsync_ShouldCarryTheCallerMetaVerbatim_WhenTheCallerIssuedAProgressToken() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);
		JsonObject callerMeta = new() {
			["progressToken"] = 7,
			["clioRunOrigin"] = "clio-run"
		};
		CallToolRequestParams parameters = new() { Name = "deploy-creatio", Meta = callerMeta };

		// Act
		await session.CallToolAsync(parameters, CancellationToken.None);

		// Assert
		JsonNode relayedParams = transport.SentRequests.Single(request => request.Method == "tools/call").Params;
		relayedParams["_meta"].ToJsonString().Should().Be(callerMeta.ToJsonString(),
			"because RequestParams.ProgressToken is a read-only view over Meta[\"progressToken\"] — if the "
			+ "parent re-issues its own token every Ring stage event of the run is dropped silently");
		relayedParams["name"].GetValue<string>().Should().Be("deploy-creatio",
			"because the tool name travels with the call");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: a child sampling/createMessage reaches the real client through the parent session and the client's answer returns into the child's pending request.")]
	public async Task ReadLoop_ShouldBridgeChildSamplingToTheParent_WhenTheChildAsksForAModelReview() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);
		RequestId childRequestId = new("child-1");

		// Act
		transport.EmitFromChild(new JsonRpcRequest {
			Id = childRequestId,
			Method = WorkerRelaySession.SamplingCreateMessageMethod,
			Params = SamplingRequestPayload()
		});
		await WaitUntilAsync(() => transport.SentResponses.Any(response => response.Id.Equals(childRequestId)));

		// Assert
		parent.SamplingRequests.Should().HaveCount(1,
			"because update-page and sync-pages call SampleAsync mid-tool: a relay that drops it degrades "
			+ "the semantic review to Skipped=true with no error anywhere");
		JsonRpcResponse answer = transport.SentResponses.Single(response => response.Id.Equals(childRequestId));
		answer.Result["model"].GetValue<string>().Should().Be(RecordingParentSession.SampledModel,
			"because the client's own answer must come back down to the child that asked");
		transport.SentErrors.Should().BeEmpty("because the bridge succeeded");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: a child sampling request is refused when the real client has no sampling capability, instead of being left unanswered.")]
	public async Task ReadLoop_ShouldRefuseChildSampling_WhenTheClientHasNoSamplingCapability() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new() { SupportsSampling = false };
		WorkerMcpRelay relay = new(_logger);
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);
		RequestId childRequestId = new("child-2");

		// Act
		transport.EmitFromChild(new JsonRpcRequest {
			Id = childRequestId,
			Method = WorkerRelaySession.SamplingCreateMessageMethod,
			Params = SamplingRequestPayload()
		});
		await WaitUntilAsync(() => transport.SentErrors.Any(error => error.Id.Equals(childRequestId)));

		// Assert
		parent.SamplingRequests.Should().BeEmpty(
			"because the client never advertised the capability, so there is nobody to ask");
		transport.SentErrors.Single(error => error.Id.Equals(childRequestId)).Error.Code.Should().Be(-32601,
			"because an explicit method-not-found lets the worker give up now rather than at the budget kill");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: a child→parent request the relay does not bridge is answered with method-not-found rather than silence.")]
	public async Task ReadLoop_ShouldRefuseUnbridgedChildRequests_WhenTheChildAsksForRoots() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);
		RequestId childRequestId = new("child-3");

		// Act
		transport.EmitFromChild(new JsonRpcRequest {
			Id = childRequestId, Method = "roots/list", Params = new JsonObject()
		});
		await WaitUntilAsync(() => transport.SentErrors.Any(error => error.Id.Equals(childRequestId)));

		// Assert
		transport.SentErrors.Single(error => error.Id.Equals(childRequestId)).Error.Code.Should().Be(-32601,
			"because an unbridged request must fail fast: a worker waiting on an answer that never comes "
			+ "burns the parent's whole budget before it is killed");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: a request pending when the worker closes its pipe faults instead of hanging forever.")]
	public async Task RequestAsync_ShouldFaultThePendingCall_WhenTheWorkerClosesItsPipe() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);
		transport.AnswerRequests = false;
		Task<CallToolResult> pending =
			session.CallToolAsync(new CallToolRequestParams { Name = "compile-creatio" }, CancellationToken.None);

		// Act
		await WaitUntilAsync(() => transport.SentRequests.Any(request => request.Method == "tools/call"));
		transport.CloseChildPipe();
		Func<Task> awaitPending = async () => await pending;

		// Assert
		await awaitPending.Should().ThrowAsync<WorkerRelayException>(
			"because the pipe closing IS the completion signal: a worker killed at its budget must fault "
			+ "its caller, never leave a task awaiting an answer that can no longer arrive");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: a JSON-RPC error from the worker faults the matching call and carries its code.")]
	public async Task RequestAsync_ShouldFaultThePendingCall_WhenTheWorkerAnswersWithAnError() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);
		transport.AnswerRequests = false;
		Task<ListToolsResult> pending = session.ListToolsAsync(CancellationToken.None);
		await WaitUntilAsync(() => transport.SentRequests.Any(request => request.Method == "tools/list"));
		RequestId issued = transport.SentRequests.Single(request => request.Method == "tools/list").Id;

		// Act
		transport.EmitFromChild(new JsonRpcError {
			Id = issued,
			Error = new JsonRpcErrorDetail { Code = -32000, Message = "worker refused" }
		});
		Func<Task> awaitPending = async () => await pending;

		// Assert
		(await awaitPending.Should().ThrowAsync<WorkerRelayException>(
				"because the worker's failure belongs to the call that caused it"))
			.Which.ErrorCode.Should().Be(-32000, "because the worker's own error code is preserved");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: the liveness probe uses tools/list and never ping, which protocol revision 2026-07-28 does not serve.")]
	public async Task ProbeLivenessAsync_ShouldUseToolsList_WhenProbingAWorker() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);

		// Act
		bool alive = await session.ProbeLivenessAsync(CancellationToken.None);

		// Assert
		alive.Should().BeTrue("because the fake worker answered the probe");
		transport.SentMethods.Should().Contain("tools/list",
			"because ClioRing moved its health probe to tools/list in the same SDK upgrade, for the same reason");
		transport.SentMethods.Should().NotContain("ping",
			"because ping is not served on protocol revision 2026-07-28, so a ping probe would report a "
			+ "perfectly healthy worker as dead");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-705: a worker whose pipe stays open and which answers nothing makes the liveness probe return false inside the probe's OWN bound, with no caller token involved — the one worker state the probe exists to catch.")]
	public async Task ProbeLivenessAsync_ShouldReturnFalse_WhenTheWorkerNeverAnswersInsideItsOwnBound() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		WorkerRelayOptions options = new() { LivenessProbeTimeout = TimeSpan.FromMilliseconds(200) };
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, options, CancellationToken.None);
		// The wedge, one process down: the transport accepts the request, the pipe never closes, and no
		// answer ever arrives. Nothing in the request path has a timer of its own.
		transport.AnswerRequests = false;

		// Act
		Task<bool> probe = session.ProbeLivenessAsync(CancellationToken.None);
		Task finished = await Task.WhenAny(probe, Task.Delay(AssertionTimeout, CancellationToken.None));

		// Assert
		new WorkerRelayOptions().LivenessProbeTimeout.Should().BeGreaterThan(TimeSpan.Zero,
			"because the SHIPPED default has to be a real bound: this test supplies its own, so an infinite "
			+ "or missing default would leave it green while every production probe still hung");
		new WorkerRelayOptions().LivenessProbeTimeout.Should().BeLessThan(TimeSpan.FromSeconds(2.763),
			"because probing exists only because reusing a live worker beats spawning one, and spawn plus "
			+ "initialize is p50 2.763 s on Windows Server 2022 (ADR §2.4) — a probe allowed to cost more "
			+ "than a respawn has no reason to exist");
		finished.Should().BeSameAs(probe,
			"because the probe must answer from its own bound: awaiting the worker's response is what hangs, "
			+ "and the thread that was meant to order the kill is then the stuck one");
		(await probe).Should().BeFalse(
			"because 'it did not answer in time' is a verdict about the worker, and the supervisor cannot act "
			+ "on a question that never returns");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-705: the pending slot of a timed-out probe is removed, so the worker's late tools/list answer resolves nothing and the session still serves the next call.")]
	public async Task ProbeLivenessAsync_ShouldLeaveTheSessionUsable_WhenTheWorkerAnswersAfterTheProbeTimedOut() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		WorkerRelayOptions options = new() { LivenessProbeTimeout = TimeSpan.FromMilliseconds(200) };
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, options, CancellationToken.None);
		transport.AnswerRequests = false;
		Task<bool> probe = session.ProbeLivenessAsync(CancellationToken.None);
		await Task.WhenAny(probe, Task.Delay(AssertionTimeout, CancellationToken.None));
		bool probeGaveUp = probe.IsCompleted;
		RequestId abandoned = transport.SentRequests.Last(request => request.Method == "tools/list").Id;

		// Act
		transport.EmitFromChild(new JsonRpcResponse {
			Id = abandoned,
			Result = JsonSerializer.SerializeToNode(new ListToolsResult { Tools = [] },
				McpJsonUtilities.DefaultOptions)
		});
		transport.AnswerRequests = true;
		ListToolsResult afterwards = await session.ListToolsAsync(CancellationToken.None);

		// Assert
		probeGaveUp.Should().BeTrue(
			"because a late answer is only reachable once the probe has given up on its own bound — without "
			+ "that, this test would be asserting nothing about the abandoned slot");
		afterwards.Should().NotBeNull(
			"because the answer to a request nobody is waiting on is dropped, exactly as the read loop already "
			+ "drops a response whose pending slot was taken: it must not fault the session or steal the next "
			+ "caller's slot");
	}

	[Test]
	[Description("TC-U-705: a caller that needs a tighter bound than the session default passes one per call, so a probe is never bounded only by whatever its caller happened to bring.")]
	[Category("Unit")]
	public async Task ProbeLivenessAsync_ShouldHonourThePerCallBound_WhenTheCallerOverridesTheSessionDefault() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		// A session bound far longer than the assertion window: only the per-call override can end this probe
		// inside it, which is what makes the override the discriminator rather than the default.
		WorkerRelayOptions options = new() { LivenessProbeTimeout = TimeSpan.FromMinutes(5) };
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, options, CancellationToken.None);
		transport.AnswerRequests = false;

		// Act
		Task<bool> probe =
			session.ProbeLivenessAsync(CancellationToken.None, TimeSpan.FromMilliseconds(200));
		Task finished = await Task.WhenAny(probe, Task.Delay(AssertionTimeout, CancellationToken.None));

		// Assert
		finished.Should().BeSameAs(probe,
			"because a supervisor whose remaining budget is smaller than the session default must be able to "
			+ "say so at the call site instead of editing a shipped default");
		(await probe).Should().BeFalse(
			"because the per-call bound reports the same verdict the default one does");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-705 (regression pin): a fired CALLER token still throws OperationCanceledException even when the probe's own bound expired too, because a cancelled probe learned nothing about the worker.")]
	public async Task ProbeLivenessAsync_ShouldThrow_WhenTheCallersOwnTokenFired() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		// Zero, so BOTH exits are live at once: the internal bound has expired before the call starts and the
		// caller's token is already cancelled. The caller must win, deterministically.
		WorkerRelayOptions options = new() { LivenessProbeTimeout = TimeSpan.Zero };
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, options, CancellationToken.None);
		transport.AnswerRequests = false;
		using CancellationTokenSource caller = new();
		await caller.CancelAsync();

		// Act
		Func<Task<bool>> probe = async () => await session.ProbeLivenessAsync(caller.Token);

		// Assert
		await probe.Should().ThrowAsync<OperationCanceledException>(
			"because a caller token that fires is a CANCELLATION, not a verdict: reporting it as false would "
			+ "make a shutdown indistinguishable from a dead worker, and the two lead to opposite actions");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-704: cancelling a tools/call TELLS THE WORKER, by writing notifications/cancelled on the child leg with the same request id the relay used, so a worker that outlives the response stops working on an answer nobody will read.")]
	public async Task CallToolAsync_ShouldTellTheWorkerTheCallWasAbandoned_WhenTheCallerCancels() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);
		transport.AnswerRequests = false;
		using CancellationTokenSource caller = new();
		Task<CallToolResult> call = session.CallToolAsync(
			new CallToolRequestParams { Name = "compile-creatio" }, caller.Token);
		await WaitUntilAsync(() => transport.SentRequests.Any(request => request.Method == "tools/call"));
		RequestId abandoned = transport.SentRequests.Single(request => request.Method == "tools/call").Id;

		// Act
		await caller.CancelAsync();
		Exception callFailure = null;
		try {
			await call;
		}
		catch (Exception exception) {
			callFailure = exception;
		}
		// The notification is emitted fire-and-forget so the caller's cancellation is never delayed by a pipe
		// write, which is exactly why it cannot be asserted on the line after the throw.
		await WaitUntilAsync(() => transport.SentToChild.OfType<JsonRpcNotification>()
			.Any(notification => notification.Method == NotificationMethods.CancelledNotification));

		// Assert
		callFailure.Should().BeAssignableTo<OperationCanceledException>(
			"because the caller's own await must still complete as cancelled — telling the worker is extra, "
			+ "not a replacement for releasing the caller");
		JsonRpcNotification cancelled = transport.SentToChild.OfType<JsonRpcNotification>()
			.SingleOrDefault(notification => notification.Method == NotificationMethods.CancelledNotification);
		cancelled.Should().NotBeNull(
			"because nothing at all is written on the child leg today: the worker keeps executing the "
			+ "abandoned tool, keeps holding its Creatio session, and is then handed the next call on the "
			+ "same transport while the old one is still in flight");
		cancelled.Params["requestId"].Deserialize<RequestId>(McpJsonUtilities.DefaultOptions)
			.Should().Be(abandoned,
				"because the worker correlates the cancellation on the id the relay issued for that call — a "
				+ "notification naming any other id cancels nothing and looks like it worked");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-704 (regression pin): a response that arrives after its call was cancelled is dropped without faulting the session, so the next caller still gets its own answer.")]
	public async Task ReadLoop_ShouldDropTheLateResponse_WhenTheCallItAnswersWasAlreadyCancelled() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);
		transport.AnswerRequests = false;
		using CancellationTokenSource caller = new();
		Task<CallToolResult> call = session.CallToolAsync(
			new CallToolRequestParams { Name = "compile-creatio" }, caller.Token);
		await WaitUntilAsync(() => transport.SentRequests.Any(request => request.Method == "tools/call"));
		RequestId abandoned = transport.SentRequests.Single(request => request.Method == "tools/call").Id;
		await caller.CancelAsync();
		Func<Task> awaitCall = async () => await call;
		await awaitCall.Should().ThrowAsync<OperationCanceledException>();

		// Act
		transport.EmitFromChild(new JsonRpcResponse {
			Id = abandoned,
			Result = JsonSerializer.SerializeToNode(new CallToolResult { Content = [] },
				McpJsonUtilities.DefaultOptions)
		});
		transport.AnswerRequests = true;
		ListToolsResult afterwards = await session.ListToolsAsync(CancellationToken.None);

		// Assert
		afterwards.Should().NotBeNull(
			"because a worker that ignores the cancellation still answers eventually, and that answer belongs "
			+ "to nobody: it must be dropped exactly as it is today, never faulting the session and never "
			+ "resolving the next caller's slot");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: disposal completes within the shutdown grace even when the client blocks a forward and ignores cancellation, so a stuck read loop can never wedge teardown and leak the worker.")]
	public async Task DisposeAsync_ShouldCompleteWithinTheShutdownGrace_WhenTheClientBlocksIgnoringCancellation() {
		// Arrange
		FakeChildTransport transport = new();
		// BLOCKS rather than throws, and ignores the token: the exact client behaviour the delay-only
		// BeforeSend hook cannot express, and the one that used to make disposal never return at all.
		BlockingParentSession parent = new(honoursCancellation: false);
		WorkerMcpRelay relay = new(_logger);
		WorkerRelayOptions options = new() { ReadLoopShutdownGrace = TimeSpan.FromMilliseconds(250) };
		WorkerRelaySession session = await relay.OpenAsync(transport, parent, options, CancellationToken.None);
		transport.EmitFromChild(StageProgressNotification(0, new ProgressToken("run-token")));
		await WaitUntilAsync(() => parent.SendsStarted == 1);

		// Act
		Task dispose = session.DisposeAsync().AsTask();
		Task finished = await Task.WhenAny(dispose, Task.Delay(AssertionTimeout, CancellationToken.None));

		// Assert
		new WorkerRelayOptions().ReadLoopShutdownGrace.Should().BeGreaterThan(TimeSpan.Zero,
			"because the SHIPPED default has to be a real bound: Timeout.InfiniteTimeSpan is negative, and an "
			+ "infinite grace would restore exactly the wedge these two tests exist to prevent while leaving "
			+ "both of them green, since each supplies its own grace");
		finished.Should().BeSameAs(dispose,
			"because forwarding is awaited inside the read loop, so a client that blocks stalls that loop head "
			+ "of line — and an unbounded join during disposal would turn that stall into a teardown that never "
			+ "finishes, wedging the relay and leaking the worker this boundary exists to contain");
		parent.SendsCompleted.Should().Be(0,
			"because the client never unblocked: disposal returned by ABANDONING the stuck loop, not by "
			+ "waiting for the send that will never come back");
		await dispose;
		parent.Release();
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: a forward blocked in a client that honours cancellation is released by disposal itself, without waiting out the shutdown grace, because the send observes the session lifetime token.")]
	public async Task DisposeAsync_ShouldReleaseTheBlockedForward_WhenTheClientHonoursTheSessionLifetimeToken() {
		// Arrange
		FakeChildTransport transport = new();
		BlockingParentSession parent = new(honoursCancellation: true);
		WorkerMcpRelay relay = new(_logger);
		// A grace far longer than the assertion window on purpose: if the forward were sent under
		// CancellationToken.None, disposal could only finish by waiting this out, and the race below would time
		// out. So this bound is what makes the test discriminate the token from the grace.
		WorkerRelayOptions options = new() { ReadLoopShutdownGrace = TimeSpan.FromSeconds(30) };
		WorkerRelaySession session = await relay.OpenAsync(transport, parent, options, CancellationToken.None);
		transport.EmitFromChild(StageProgressNotification(0, new ProgressToken("run-token")));
		await WaitUntilAsync(() => parent.SendsStarted == 1);

		// Act
		Task dispose = session.DisposeAsync().AsTask();
		Task finished = await Task.WhenAny(dispose, Task.Delay(AssertionTimeout, CancellationToken.None));

		// Assert
		finished.Should().BeSameAs(dispose,
			"because the forward is sent under the session lifetime token, so disposal cancels a co-operative "
			+ "client immediately instead of waiting out the 30 s shutdown grace");
		parent.SendsCompleted.Should().Be(0,
			"because the blocked send was cancelled rather than allowed to complete");
		await dispose;
		parent.Release();
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: disposal delivers what the worker had already emitted — the forward in flight and the one still unread — instead of cancelling it, because the notification in that position is the authoritative terminal stage event.")]
	public async Task DisposeAsync_ShouldDeliverWhatTheWorkerAlreadyEmitted_WhenDisposalRacesTheTerminalStage() {
		// Arrange
		FakeChildTransport transport = new();
		// HONOURS the token, which is the whole discriminator: a fake that ignored cancellation would be
		// delivered either way, so it could not tell a drained teardown from one that cancelled the send.
		DelayingParentSession parent = new(TimeSpan.FromMilliseconds(250));
		WorkerMcpRelay relay = new(_logger);
		WorkerRelayOptions options = new() {
			NotificationDrainGrace = TimeSpan.FromSeconds(5),
			ReadLoopShutdownGrace = TimeSpan.FromSeconds(5)
		};
		WorkerRelaySession session = await relay.OpenAsync(transport, parent, options, CancellationToken.None);
		// Both emitted up front, then disposal is issued the moment the FIRST send starts: event 0 is in
		// flight and event 1 is still sitting unread in the transport channel — the two positions a terminal
		// stage event can occupy when a caller disposes right after reading the tool result.
		transport.EmitFromChild(StageProgressNotification(0, new ProgressToken("run-token")));
		transport.EmitFromChild(StageProgressNotification(1, new ProgressToken("run-token")));
		await WaitUntilAsync(() => parent.SendsStarted == 1);

		// Act
		await session.DisposeAsync();

		// Assert
		parent.Delivered.Select(SequenceOf).Should().Equal([0, 1],
			"because ADR rule 4 has the parent wait for the authoritative terminal stage and the deploy family "
			+ "bound itself on it: cancelling those forwards at the first line of disposal drops them, and the "
			+ "forward swallows the cancellation, so the loss leaves no trace at all — a dropped terminal stage "
			+ "is worse than the visible wedge that cancelling was introduced to fix");
		session.NotificationDrainTimedOut.Should().BeFalse(
			"because the client accepted everything inside the drain window, so there is nothing to report");
		// And nothing is reported: a warning on a teardown that lost nothing would train an operator to ignore
		// the one warning that means a stage event was discarded. (NSubstitute carries no because-text.)
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: a teardown that abandons a stuck read loop and gives up on an undelivered notification says so through the logger and through the session, because that is the exact event these two bounds exist to survive.")]
	public async Task DisposeAsync_ShouldReportTheAbandonment_WhenTheClientNeverReleasesTheForward() {
		// Arrange
		FakeChildTransport transport = new();
		BlockingParentSession parent = new(honoursCancellation: false);
		WorkerMcpRelay relay = new(_logger);
		WorkerRelayOptions options = new() {
			NotificationDrainGrace = TimeSpan.FromMilliseconds(150),
			ReadLoopShutdownGrace = TimeSpan.FromMilliseconds(250)
		};
		WorkerRelaySession session = await relay.OpenAsync(transport, parent, options, CancellationToken.None);
		transport.EmitFromChild(StageProgressNotification(0, new ProgressToken("run-token")));
		await WaitUntilAsync(() => parent.SendsStarted == 1);

		// Act
		await session.DisposeAsync();

		// Assert
		session.NotificationDrainTimedOut.Should().BeTrue(
			"because the client never accepted the notification the worker had already emitted, and a caller "
			+ "bounding itself on the terminal stage has to be able to tell 'never arrived' from 'thrown away "
			+ "during teardown'");
		session.ReadLoopAbandoned.Should().BeTrue(
			"because the supervisor reclaims a worker whose relay abandoned its loop, and it can only act on "
			+ "that if the session records it");
		// Both events reach the OPERATOR too, and through the seam the MCP host already uses — a counter nobody
		// prints leaves the same investigation nowhere the missing log line did. (NSubstitute carries no
		// because-text, hence these comments.)
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("gave up draining")));
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("abandoned")));
		// Disposal stays idempotent: a second call must not re-report a teardown that already happened.
		await session.DisposeAsync();
		_logger.Received(2).WriteWarning(Arg.Any<string>());
		parent.Release();
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: the relay requests protocol revision 2024-11-05 spelled as a literal, so raising the measured constant fails a test instead of silently changing what was measured.")]
	public async Task OpenAsync_ShouldRequestTheLiteralMeasuredRevision_WhenTheHandshakeSucceeds() {
		// Arrange
		FakeChildTransport transport = new();
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);

		// Act
		await using WorkerRelaySession session =
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);

		// Assert
		WorkerRelayOptions.MeasuredProtocolVersion.Should().Be("2024-11-05",
			"because every other protocol assertion reads the constant, so only a literal can fail when the "
			+ "constant moves — and the revision is load-bearing: sampling, which ADR rule 1 depends on, is "
			+ "deprecated as of 2026-07-28 and raising this needs a re-measurement, not an edit");
		JsonNode initializeParams = transport.SentRequests.Single(request => request.Method == "initialize").Params;
		initializeParams["protocolVersion"].GetValue<string>().Should().Be("2024-11-05",
			"because what goes on the wire is what was measured, not what a constant happens to say today");
		session.NegotiatedProtocolVersion.Should().Be("2024-11-05",
			"because the session reports the revision it actually agreed with the worker");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: a worker that counter-offers a different protocol revision fails the handshake instead of having its counter-offer stored unchecked.")]
	public async Task OpenAsync_ShouldFail_WhenTheWorkerCounterOffersADifferentProtocolRevision() {
		// Arrange
		FakeChildTransport transport = new() { ProtocolVersionToNegotiate = "2026-07-28" };
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);

		// Act
		Func<Task> open = async () =>
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);

		// Assert
		(await open.Should().ThrowAsync<WorkerRelayException>(
				"because both legs are clio: a differing revision means the parent and the worker are different "
				+ "builds, and storing the counter-offer unchecked is how the relay's measured properties stop "
				+ "applying with nothing anywhere saying so"))
			.Which.Message.Should().Contain("2026-07-28",
				"because the failure has to name what the worker offered and what was asked for, or the "
				+ "operator cannot tell which side is the wrong build")
			.And.Contain("2024-11-05",
				"because the requested revision is the other half of that comparison");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: a handshake the worker cannot complete is reported, and the session is not handed back half-open.")]
	public async Task OpenAsync_ShouldFail_WhenTheWorkerReturnsAnInitializeResultWithoutAProtocolVersion() {
		// Arrange
		FakeChildTransport transport = new() { ProtocolVersionToNegotiate = null };
		RecordingParentSession parent = new();
		WorkerMcpRelay relay = new(_logger);

		// Act
		Func<Task> open = async () =>
			await relay.OpenAsync(transport, parent, null, CancellationToken.None);

		// Assert
		await open.Should().ThrowAsync<WorkerRelayException>(
			"because a hand-rolled handshake gets no SDK validation: an unusable initialize result would "
			+ "otherwise surface much later as an inexplicable tool failure");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: the transport owner attaches to a worker's existing streams and both directions frame correctly, so the supervisor keeps ownership of the process.")]
	public async Task ConnectAsync_ShouldFrameBothDirections_WhenAttachedToTheWorkerStreams() {
		// Arrange
		using AnonymousPipeServerStream workerOutputWriter = new(PipeDirection.Out, HandleInheritability.None);
		using AnonymousPipeClientStream workerOutputReader =
			new(PipeDirection.In, workerOutputWriter.GetClientHandleAsString());
		using AnonymousPipeServerStream workerInputReader = new(PipeDirection.In, HandleInheritability.None);
		using AnonymousPipeClientStream workerInputWriter =
			new(PipeDirection.Out, workerInputReader.GetClientHandleAsString());
		WorkerChildTransportOwner owner = new();

		// Act
		await using ITransport transport =
			await owner.ConnectAsync(workerInputWriter, workerOutputReader, CancellationToken.None);
		await using (StreamWriter fromWorker = new(workerOutputWriter, leaveOpen: true)) {
			fromWorker.NewLine = "\n";
			await fromWorker.WriteLineAsync(
				"{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progressToken\":42}}");
			await fromWorker.FlushAsync();
		}
		JsonRpcMessage received = await transport.MessageReader.ReadAsync(CancellationToken.None);
		await transport.SendMessageAsync(
			new JsonRpcNotification { Method = "notifications/initialized" }, CancellationToken.None);
		using StreamReader toWorker = new(workerInputReader);
		string writtenLine = await toWorker.ReadLineAsync();

		// Assert
		received.Should().BeOfType<JsonRpcNotification>(
			"because the shared StreamClientSessionTransport base — the very type the stdio transport also "
			+ "returns — does the newline framing and deserialization")
			.Which.Params["progressToken"].GetValue<int>().Should().Be(42,
				"because a numeric token must arrive off the wire as a number");
		writtenLine.Should().Contain("\"method\":\"notifications/initialized\"",
			"because attaching to the supervisor's streams must relay in both directions — the transport is "
			+ "never allowed to create or kill the worker, which containment and ADR rule 4 both require");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-401: swapped worker streams are rejected up front, because a wrong-way transport is indistinguishable from a worker that failed to start.")]
	public async Task ConnectAsync_ShouldRejectStreams_WhenTheyAreOrientedTheWrongWay() {
		// Arrange
		using AnonymousPipeServerStream readable = new(PipeDirection.In, HandleInheritability.None);
		using AnonymousPipeClientStream writable =
			new(PipeDirection.Out, readable.GetClientHandleAsString());
		WorkerChildTransportOwner owner = new();

		// Act
		Func<Task> swapped = async () =>
			await owner.ConnectAsync(readable, writable, CancellationToken.None);
		Func<Task> missing = async () =>
			await owner.ConnectAsync(null, readable, CancellationToken.None);

		// Assert
		await swapped.Should().ThrowAsync<ArgumentException>(
			"because a transport attached to the wrong ends simply never yields a message, which reads as a "
			+ "dead worker and sends the investigation to the wrong place");
		await missing.Should().ThrowAsync<ArgumentNullException>(
			"because a missing stream is a caller defect, not a worker failure");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-405 (SDK pin, NOT evidence of a clio fix): SDK 2.2.0 serialises concurrent sends behind its own _sendLock, so racing writers still produce whole newline-framed lines. This is what a future SDK bump that drops the guarantee would break, and the relay would then need a gate of its own.")]
	public async Task SendMessageAsync_ShouldFrameEveryMessageWhole_WhenWritersRaceOnOneChildTransport() {
		// Arrange
		using AnonymousPipeServerStream workerOutputWriter = new(PipeDirection.Out, HandleInheritability.None);
		using AnonymousPipeClientStream workerOutputReader =
			new(PipeDirection.In, workerOutputWriter.GetClientHandleAsString());
		using AnonymousPipeServerStream workerInputReader = new(PipeDirection.In, HandleInheritability.None);
		using AnonymousPipeClientStream workerInputWriter =
			new(PipeDirection.Out, workerInputReader.GetClientHandleAsString());
		WorkerChildTransportOwner owner = new();
		await using ITransport transport =
			await owner.ConnectAsync(workerInputWriter, workerOutputReader, CancellationToken.None);
		const int racingWriters = 32;

		// Act
		await Task.WhenAll(Enumerable.Range(0, racingWriters).Select(index => Task.Run(async () =>
			await transport.SendMessageAsync(new JsonRpcNotification {
				Method = ProgressNotificationMethod,
				Params = new JsonObject { ["progressToken"] = index }
			}, CancellationToken.None))));
		using StreamReader toWorker = new(workerInputReader);
		List<string> lines = [];
		using CancellationTokenSource readBound = new(AssertionTimeout);
		try {
			for (int index = 0; index < racingWriters; index++) {
				string line = await toWorker.ReadLineAsync(readBound.Token);
				if (line is null) {
					break;
				}
				lines.Add(line);
			}
		}
		catch (OperationCanceledException) {
			// Deliberately swallowed: a missing line is the failure this test reports, and the count assertion
			// below says what actually arrived. Letting the cancellation escape would replace that with a
			// stack trace that names the reader instead of the framing.
		}

		// Assert
		lines.Should().HaveCount(racingWriters,
			"because every racing write must end as its own complete line — a lost or merged line is a worker "
			+ "that gets one frame it cannot parse and then answers nothing, which reads as a sick environment "
			+ "rather than a client defect");
		lines.Should().OnlyContain(line => IsWholeJsonMessage(line),
			"because newline-delimited JSON is the framing: two interleaved writes produce one line carrying "
			+ "two objects, and the worker's parser is where that surfaces");
		lines.Select(ProgressTokenOf).Should().BeEquivalentTo(Enumerable.Range(0, racingWriters),
			"because every writer's own message must survive whole, not merely some 32 well-formed lines");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-405: a send that was cancelled between the payload write and the newline write RETIRES the session, because the SDK's lock guarantees a completed send and not an atomic one — a transport that may hold half a frame must never be written to again.")]
	public async Task RequestAsync_ShouldRetireTheSession_WhenASendWasCancelledMidFrame() {
		// Arrange
		using AnonymousPipeServerStream workerOutputWriter = new(PipeDirection.Out, HandleInheritability.None);
		using AnonymousPipeClientStream workerOutputReader =
			new(PipeDirection.In, workerOutputWriter.GetClientHandleAsString());
		using AnonymousPipeServerStream workerInputReader = new(PipeDirection.In, HandleInheritability.None);
		using AnonymousPipeClientStream workerInputWriter =
			new(PipeDirection.Out, workerInputReader.GetClientHandleAsString());
		// The real SDK transport, because the fake records TYPED messages and never serialises: a framing
		// defect cannot exist there, so a test written against it would be worthless.
		using HalfFrameChildInputStream childInput = new(workerInputWriter);
		WorkerChildTransportOwner owner = new();
		ITransport childTransport =
			await owner.ConnectAsync(childInput, workerOutputReader, CancellationToken.None);
		ScriptedChildOverPipes child = new(workerInputReader, workerOutputWriter);
		child.Start();
		WorkerMcpRelay relay = new(_logger);
		RecordingParentSession parent = new();
		await using WorkerRelaySession session =
			await relay.OpenAsync(childTransport, parent, null, CancellationToken.None);
		// Armed only now: both handshake writes are complete, so the half frame belongs to the call below.
		childInput.ArmHalfFrame();

		// Act
		using CancellationTokenSource caller = new();
		Task<ListToolsResult> firstCall = session.ListToolsAsync(caller.Token);
		await WaitUntilAsync(() => childInput.HalfFrameWritten);
		await caller.CancelAsync();
		Exception firstFailure = null;
		try {
			await firstCall;
		}
		catch (Exception exception) {
			firstFailure = exception;
		}
		Task<ListToolsResult> secondCall = session.ListToolsAsync(CancellationToken.None);
		// Observed rather than abandoned: while the session is still writable this call simply waits for an
		// answer that never comes, and disposal later faults it — an unobserved faulted task would surface as
		// an unrelated failure much later.
		_ = secondCall.ContinueWith(static abandoned => abandoned.Exception,
			CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
		Task finished = await Task.WhenAny(secondCall, Task.Delay(TimeSpan.FromSeconds(2)));
		bool sessionRetired = ReferenceEquals(finished, secondCall)
			&& secondCall.IsFaulted
			&& secondCall.Exception?.InnerException is WorkerRelayException;

		// Assert
		firstFailure.Should().BeAssignableTo<OperationCanceledException>(
			"because the caller cancelled: it must see its own cancellation, not a relay failure");
		child.Lines.Should().OnlyContain(line => IsWholeJsonMessage(line),
			"because the SDK passes the caller's token to the payload write, the newline write and the flush "
			+ "separately: a token that fires between them releases the lock with an unterminated line on the "
			+ "child's stdin, and the next writer's JSON is then appended to it — the worker gets one corrupt "
			+ "frame, answers nothing, and the wedge reappears one process down looking like a sick stand");
		sessionRetired.Should().BeTrue(
			"because a session whose send did not complete has to be retired rather than reused: the fix is "
			+ "never writing to that transport again and letting the supervisor's lease reclaim the process, "
			+ "not passing CancellationToken.None to the send — an uncancellable write against a full pipe "
			+ "would hang the caller past its own budget and reintroduce the wedge one layer up");
	}

	private static IEnumerable<(string Member, Type Type)> SignatureTypes(Type type) {
		foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic
			| BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
			yield return ($"{type.Name}.{field.Name}", field.FieldType);
		}
		foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic
			| BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
			yield return ($"{type.Name}.{property.Name}", property.PropertyType);
		}
		foreach (MethodBase method in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic
				| BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
			.OfType<MethodBase>()) {
			if (method is MethodInfo typed) {
				yield return ($"{type.Name}.{method.Name}", typed.ReturnType);
			}
			foreach (ParameterInfo parameter in method.GetParameters()) {
				yield return ($"{type.Name}.{method.Name}({parameter.Name})", parameter.ParameterType);
			}
		}
	}

	/// <summary>
	/// Whether one signature type reaches a forbidden type anywhere in its type graph.
	/// </summary>
	/// <remarks>
	/// Exact equality is not enough. A local whose value survives an await is hoisted onto the async state
	/// machine as a field of its own type — which exact equality does catch — but an awaited call whose result
	/// does NOT survive an await leaves only a <c>TaskAwaiter&lt;McpClient&gt;</c>, and equality walks past it.
	/// Unwrapping generic arguments and element types closes that half of the gap; the IL scan closes the
	/// other half, where nothing reaches a signature at all.
	/// </remarks>
	/// <param name="candidate">A field, property, return or parameter type.</param>
	/// <param name="forbidden">The types that must not be reachable.</param>
	/// <returns><c>true</c> when a forbidden type is reachable from <paramref name="candidate"/>.</returns>
	private static bool ReferencesForbiddenType(Type candidate, IReadOnlyCollection<Type> forbidden) {
		HashSet<Type> visited = [];
		Stack<Type> pending = new();
		pending.Push(candidate);
		while (pending.Count > 0) {
			Type current = pending.Pop();
			if (current is null || !visited.Add(current)) {
				continue;
			}
			if (forbidden.Contains(current)) {
				return true;
			}
			foreach (Type argument in current.GenericTypeArguments) {
				pending.Push(argument);
			}
			if (current.HasElementType) {
				pending.Push(current.GetElementType());
			}
		}
		return false;
	}

	/// <summary>
	/// Scans the IL BODY of every method and constructor of the given types and reports each member reference
	/// whose declaring type is forbidden.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Runtime reflection over the IL (<see cref="MethodBody.GetILAsByteArray"/> plus
	/// <see cref="Module.ResolveMember(int, Type[], Type[])"/>), not a source-text search: a grep is defeated
	/// by a <c>using</c> alias or a fully-qualified name split across lines, and would flag this test's own
	/// forbidden array. Instruction lengths are DECODED rather than the bytes scanned for a pattern, because
	/// an operand byte can look exactly like an opcode.
	/// </para>
	/// <para>
	/// Matched on the DECLARING TYPE rather than a method name, so every entry point comes for free —
	/// <c>CreateAsync</c>, <c>ResumeSessionAsync</c>, <c>new McpClientHandlers()</c>, a <c>typeof</c> — and
	/// nested types are expanded here so an async method's state machine, which is where the call actually
	/// lives, is always reached.
	/// </para>
	/// </remarks>
	/// <param name="types">The types to scan.</param>
	/// <param name="forbidden">The declaring types that must not be referenced.</param>
	/// <returns>One entry per offending reference, naming the method and the member it reached.</returns>
	private static IReadOnlyList<string> MethodBodyReferencesTo(IEnumerable<Type> types,
		IEnumerable<Type> forbidden) {
		HashSet<Type> banned = [.. forbidden];
		List<string> offenders = [];
		foreach (Type type in types.SelectMany(WithNestedTypes).Distinct()) {
			foreach (MethodBase method in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic
					| BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
				.OfType<MethodBase>()) {
				foreach (MemberInfo referenced in ReferencedMembers(method)) {
					Type declaring = referenced as Type ?? referenced.DeclaringType;
					if (declaring is not null && banned.Contains(declaring)) {
						offenders.Add($"{type.Name}.{method.Name} -> {declaring.Name}.{referenced.Name}");
					}
				}
			}
		}
		return offenders;
	}

	private static IEnumerable<Type> WithNestedTypes(Type type) =>
		[type, .. type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(WithNestedTypes)];

	private static List<MemberInfo> ReferencedMembers(MethodBase method) {
		List<MemberInfo> referenced = [];
		byte[] il = MethodIl(method);
		if (il is null) {
			return referenced;
		}
		Type[] typeArguments = method.DeclaringType is { IsGenericType: true } declaring
			? declaring.GetGenericArguments()
			: null;
		Type[] methodArguments = method.IsGenericMethodDefinition ? method.GetGenericArguments() : null;
		int position = 0;
		while (position < il.Length) {
			if (!TryReadInstruction(il, ref position, out OperandType operandType, out int token)) {
				// An opcode this decoder does not know means every following byte offset is a guess, so the
				// scan stops rather than reporting invented references.
				break;
			}
			if (operandType is not (OperandType.InlineMethod or OperandType.InlineTok)) {
				continue;
			}
			try {
				MemberInfo member = method.Module.ResolveMember(token, typeArguments, methodArguments);
				if (member is not null) {
					referenced.Add(member);
				}
			}
			catch (Exception) {
				// A token that cannot be resolved in this generic context says nothing about the SDK client.
			}
		}
		return referenced;
	}

	private static IReadOnlyDictionary<short, OpCode> BuildOpCodeTable() {
		Dictionary<short, OpCode> table = [];
		foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
			OpCode instruction = (OpCode)field.GetValue(null);
			table[instruction.Value] = instruction;
		}
		return table;
	}

	private static byte[] MethodIl(MethodBase method) {
		try {
			// Abstract, interface, extern and runtime-provided methods have no body at all.
			return method.GetMethodBody()?.GetILAsByteArray();
		}
		catch (Exception) {
			return null;
		}
	}

	private static bool TryReadInstruction(byte[] il, ref int position, out OperandType operandType,
		out int token) {
		operandType = OperandType.InlineNone;
		token = 0;
		short code = il[position++];
		if (code == 0xFE) {
			code = (short)(0xFE00 | il[position++]);
		}
		if (!KnownOpCodes.TryGetValue(code, out OpCode instruction)) {
			return false;
		}
		operandType = instruction.OperandType;
		switch (operandType) {
			case OperandType.InlineNone:
				break;
			case OperandType.ShortInlineBrTarget:
			case OperandType.ShortInlineI:
			case OperandType.ShortInlineVar:
				position += 1;
				break;
			case OperandType.InlineVar:
				position += 2;
				break;
			case OperandType.InlineBrTarget:
			case OperandType.InlineI:
			case OperandType.ShortInlineR:
				position += 4;
				break;
			case OperandType.InlineI8:
			case OperandType.InlineR:
				position += 8;
				break;
			case OperandType.InlineField:
			case OperandType.InlineMethod:
			case OperandType.InlineSig:
			case OperandType.InlineString:
			case OperandType.InlineTok:
			case OperandType.InlineType:
				token = BitConverter.ToInt32(il, position);
				position += 4;
				break;
			case OperandType.InlineSwitch:
				int branches = BitConverter.ToInt32(il, position);
				position += 4 + (4 * branches);
				break;
			default:
				return false;
		}
		return true;
	}

	private static int SequenceOf(JsonRpcNotification notification) =>
		notification.Params?["_meta"]?["clioStageEvent"]?["sequence"]?.GetValue<int>() ?? -1;

	private static JsonNode ForwarderProducedPayload(ClioStageEvent stageEvent) =>
		JsonSerializer.SerializeToNode(
			StageEventProgressForwarder.ToProgressNotification(stageEvent, new ProgressToken("run-token"),
				new StageEventProgressForwarder.ProgressCursor()),
			McpJsonUtilities.DefaultOptions);

	private static JsonRpcNotification StageProgressNotification(int sequence, ProgressToken token) {
		ProgressNotificationParams parameters = StageEventProgressForwarder.ToProgressNotification(
			CanonicalStageEvent(sequence), token, new StageEventProgressForwarder.ProgressCursor());
		return new JsonRpcNotification {
			Method = ProgressNotificationMethod,
			Params = JsonSerializer.SerializeToNode(parameters, McpJsonUtilities.DefaultOptions)
		};
	}

	private static ClioStageEvent CanonicalStageEvent(int sequence) =>
		new(
			ClioStageEventContract.SchemaVersion,
			ClioStageEventContract.EventTypes.Stage,
			Guid.Parse(CanonicalRunId),
			sequence,
			ClioStageEventContract.Operations.Deploy,
			Stage: new ClioStageDetail(
				StageIds.RestoreDb,
				"Restore database",
				sequence,
				8,
				ClioStageEventContract.StageStatuses.Done,
				StartedAtUtc: new DateTimeOffset(2026, 8, 17, 10, 15, 30, TimeSpan.Zero),
				DurationMs: 42123,
				Message: "Restore database"));

	private static JsonNode SamplingRequestPayload() =>
		new JsonObject {
			["maxTokens"] = 500,
			["messages"] = new JsonArray {
				new JsonObject {
					["role"] = "user",
					["content"] = new JsonObject { ["type"] = "text", ["text"] = "review this page" }
				}
			}
		};

	private static async Task WaitUntilAsync(Func<bool> condition) {
		Stopwatch elapsed = Stopwatch.StartNew();
		while (!condition() && elapsed.Elapsed < AssertionTimeout) {
			await Task.Delay(10, CancellationToken.None);
		}
	}

	/// <summary>
	/// Whether one line off the child's stdin is exactly ONE whole JSON-RPC message.
	/// </summary>
	/// <remarks>
	/// Parsed rather than pattern-matched on purpose: <c>JsonNode.Parse</c> rejects trailing content, so it
	/// catches both halves of a framing defect — a truncated object and two objects sharing one line — while a
	/// <c>}{</c> search would also fire on a string value that legally contains those characters.
	/// </remarks>
	/// <param name="line">One newline-delimited frame.</param>
	/// <returns><c>true</c> when the whole line parses as a single JSON value.</returns>
	private static bool IsWholeJsonMessage(string line) {
		try {
			return JsonNode.Parse(line) is not null;
		}
		catch (JsonException) {
			return false;
		}
	}

	private static int ProgressTokenOf(string line) =>
		JsonNode.Parse(line)["params"]["progressToken"].GetValue<int>();

	/// <summary>
	/// A scripted child worker: an <see cref="ITransport"/> whose channel the test writes into, so ordering,
	/// <c>_meta</c> fidelity and the sampling round trip are observable without a process or a stand.
	/// </summary>
	private sealed class FakeChildTransport : ITransport {

		private readonly Channel<JsonRpcMessage> _fromChild =
			Channel.CreateUnbounded<JsonRpcMessage>(new UnboundedChannelOptions { SingleReader = true });
		private readonly List<JsonRpcMessage> _sentToChild = [];
		private readonly object _sentLock = new();
		private int _messageReaderReads;

		public bool AnswerRequests { get; set; } = true;

		public string ProtocolVersionToNegotiate { get; init; } = WorkerRelayOptions.MeasuredProtocolVersion;

		public int MessageReaderReads => _messageReaderReads;

		public string SessionId => "fake-worker";

		public ChannelReader<JsonRpcMessage> MessageReader {
			get {
				Interlocked.Increment(ref _messageReaderReads);
				return _fromChild.Reader;
			}
		}

		public IReadOnlyList<JsonRpcMessage> SentToChild {
			get {
				lock (_sentLock) {
					return [.. _sentToChild];
				}
			}
		}

		public IReadOnlyList<JsonRpcRequest> SentRequests => [.. SentToChild.OfType<JsonRpcRequest>()];

		public IReadOnlyList<JsonRpcResponse> SentResponses => [.. SentToChild.OfType<JsonRpcResponse>()];

		public IReadOnlyList<JsonRpcError> SentErrors => [.. SentToChild.OfType<JsonRpcError>()];

		public IReadOnlyList<string> SentMethods => [
			.. SentToChild.Select(message => message switch {
				JsonRpcRequest request => request.Method,
				JsonRpcNotification notification => notification.Method,
				_ => null
			}).Where(method => method is not null)
		];

		public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken) {
			lock (_sentLock) {
				_sentToChild.Add(message);
			}
			if (message is JsonRpcRequest request && AnswerRequests) {
				Answer(request);
			}
			return Task.CompletedTask;
		}

		public void EmitFromChild(JsonRpcMessage message) => _fromChild.Writer.TryWrite(message);

		public void CloseChildPipe() => _fromChild.Writer.TryComplete();

		public ValueTask DisposeAsync() {
			CloseChildPipe();
			return default;
		}

		private void Answer(JsonRpcRequest request) {
			JsonNode result = request.Method switch {
				"initialize" => InitializeResultNode(),
				"tools/list" => JsonSerializer.SerializeToNode(new ListToolsResult { Tools = [] },
					McpJsonUtilities.DefaultOptions),
				"tools/call" => JsonSerializer.SerializeToNode(new CallToolResult { Content = [] },
					McpJsonUtilities.DefaultOptions),
				_ => null
			};
			if (result is null) {
				return;
			}
			EmitFromChild(new JsonRpcResponse { Id = request.Id, Result = result });
		}

		private JsonNode InitializeResultNode() {
			JsonObject result = new() {
				["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
				["serverInfo"] = new JsonObject { ["name"] = "fake-worker", ["version"] = "1" }
			};
			if (ProtocolVersionToNegotiate is not null) {
				result["protocolVersion"] = ProtocolVersionToNegotiate;
			}
			return result;
		}
	}

	/// <summary>
	/// A PLANTED offender, kept permanently so the body guard's fail-first evidence lives in the tree instead
	/// of in a throwaway commit.
	/// </summary>
	/// <remarks>
	/// The shape that matters: the SDK client is only CALLED. Its result is discarded, so nothing is hoisted
	/// onto the async state machine, nothing lands on a field, a property, a parameter or a return type, and
	/// every signature stays clean — while the concurrent notification dispatch ADR rule 12 forbids would be
	/// fully installed. Declared in clio.tests on purpose: the production scan is scoped to clio's relay
	/// namespace and must stay that way, so this fixture is scanned as its own explicit case.
	/// </remarks>
	private sealed class SdkClientBodyOnlyOffender {

		internal static async Task PlantedOffenceAsync(ModelContextProtocol.Client.IClientTransport transport) {
			await Task.Yield();
			_ = ModelContextProtocol.Client.McpClient.CreateAsync(transport);
		}
	}

	/// <summary>
	/// The worker's standard input, wrapped so ONE send can be stopped between its payload write and its
	/// newline write — the state the SDK's <c>_sendLock</c> does not protect against.
	/// </summary>
	/// <remarks>
	/// Blocking the FIRST write would prove nothing: nothing reaches the child, so no line is ever corrupt and
	/// the test is born green. The SDK writes payload, then newline, then flush, each taking the caller's own
	/// token (read off the shipped 2.2.0 IL), so the newline write is where a fired token leaves an
	/// unterminated line behind and releases the lock to the next writer.
	/// </remarks>
	private sealed class HalfFrameChildInputStream : Stream {

		private readonly Stream _inner;
		private readonly TaskCompletionSource _halfFrameWritten =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _armed;
		private int _writesSinceArmed;

		internal HalfFrameChildInputStream(Stream inner) => _inner = inner;

		/// <summary>Gets a value indicating whether the payload landed and the newline write is blocked.</summary>
		internal bool HalfFrameWritten => _halfFrameWritten.Task.IsCompleted;

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => throw new NotSupportedException();

		public override long Position {
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		/// <summary>Blocks the NEXT newline write, so the send after this call leaves half a frame behind.</summary>
		internal void ArmHalfFrame() => Volatile.Write(ref _armed, 1);

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
			CancellationToken cancellationToken = default) {
			if (Volatile.Read(ref _armed) == 1 && Interlocked.Increment(ref _writesSinceArmed) == 2) {
				_halfFrameWritten.TrySetResult();
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				return;
			}
			await _inner.WriteAsync(buffer, cancellationToken);
		}

		public override void Write(byte[] buffer, int offset, int count) =>
			_inner.Write(buffer, offset, count);

		public override void Flush() => _inner.Flush();

		public override Task FlushAsync(CancellationToken cancellationToken) =>
			_inner.FlushAsync(cancellationToken);

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		protected override void Dispose(bool disposing) {
			if (disposing) {
				_inner.Dispose();
			}
			base.Dispose(disposing);
		}
	}

	/// <summary>
	/// A worker scripted over REAL pipes: it reads whatever the relay framed onto its stdin, records every
	/// line verbatim, and answers <c>initialize</c> so a session can be opened over the SDK transport.
	/// </summary>
	/// <remarks>
	/// Deliberately dumb about anything it cannot parse — that IS the production failure being observed: a
	/// worker handed a corrupt frame answers nothing at all, which the parent can only see as a worker that
	/// went quiet.
	/// </remarks>
	private sealed class ScriptedChildOverPipes {

		private readonly List<string> _lines = [];
		private readonly object _linesLock = new();
		private readonly StreamReader _fromParent;
		private readonly StreamWriter _toParent;

		internal ScriptedChildOverPipes(Stream parentToChild, Stream childToParent) {
			_fromParent = new StreamReader(parentToChild);
			_toParent = new StreamWriter(childToParent) { AutoFlush = true, NewLine = "\n" };
		}

		/// <summary>Gets every line the relay has framed onto this worker's stdin.</summary>
		internal IReadOnlyList<string> Lines {
			get {
				lock (_linesLock) {
					return [.. _lines];
				}
			}
		}

		/// <summary>Starts reading the worker's stdin.</summary>
		internal void Start() => _ = Task.Run(PumpAsync, CancellationToken.None);

		private async Task PumpAsync() {
			try {
				string line;
				while ((line = await _fromParent.ReadLineAsync()) is not null) {
					lock (_linesLock) {
						_lines.Add(line);
					}
					await AnswerAsync(line);
				}
			}
			catch (Exception) {
				// The parent closing its end IS how this worker ends; a torn-down pipe is not a failure here.
			}
		}

		private async Task AnswerAsync(string line) {
			JsonNode request;
			try {
				request = JsonNode.Parse(line);
			}
			catch (JsonException) {
				// A frame the worker cannot parse is answered with silence, exactly as a real one would.
				return;
			}
			if (request?["method"]?.GetValue<string>() != "initialize") {
				return;
			}
			JsonObject response = new() {
				["jsonrpc"] = "2.0",
				["id"] = request["id"]?.DeepClone(),
				["result"] = new JsonObject {
					["protocolVersion"] = WorkerRelayOptions.MeasuredProtocolVersion,
					["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
					["serverInfo"] = new JsonObject { ["name"] = "scripted-worker", ["version"] = "1" }
				}
			};
			await _toParent.WriteLineAsync(response.ToJsonString());
		}
	}

	/// <summary>
	/// A parent leg that BLOCKS a forward instead of delaying or throwing it — the client behaviour that used
	/// to wedge the relay: the read loop awaits each forward in place, so a send that never returns stalls the
	/// loop, and disposal joining that loop never completed either.
	/// </summary>
	/// <remarks>
	/// A separate fake rather than a longer <see cref="RecordingParentSession.BeforeSend"/> delay, and the
	/// difference is the point: a delay always finishes, so no delay length can reach the unbounded-join
	/// defect. The two modes separate the two halves of the fix — <c>honoursCancellation: true</c> proves the
	/// forward observes the session lifetime token, <c>false</c> proves disposal is bounded even when it does
	/// not.
	/// </remarks>
	private sealed class BlockingParentSession : IParentMcpSession {

		private readonly TaskCompletionSource _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly bool _honoursCancellation;
		private int _sendsStarted;
		private int _sendsCompleted;

		internal BlockingParentSession(bool honoursCancellation) =>
			_honoursCancellation = honoursCancellation;

		public bool SupportsSampling => true;

		internal int SendsStarted => Volatile.Read(ref _sendsStarted);

		internal int SendsCompleted => Volatile.Read(ref _sendsCompleted);

		public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken) {
			Interlocked.Increment(ref _sendsStarted);
			if (_honoursCancellation) {
				await _blocked.Task.WaitAsync(cancellationToken);
			} else {
				await _blocked.Task;
			}
			// Counted AFTER the await on purpose: a cancelled send must not read as a completed one.
			Interlocked.Increment(ref _sendsCompleted);
		}

		// MCP9005: CreateMessageRequestParams / CreateMessageResult are deprecated in SDK 2.2.0 (SEP-2577).
		// Suppressed with this justification rather than silently, matching the production adapter: the
		// interface still carries sampling because ADR rule 1 depends on it, and OQ-6 tracks the migration to
		// InputRequest / ResolveInputRequestsAsync.
#pragma warning disable MCP9005
		public ValueTask<CreateMessageResult> SampleAsync(CreateMessageRequestParams requestParams,
			CancellationToken cancellationToken) =>
			throw new NotSupportedException("This fake exists to block notification forwarding, not to sample.");
#pragma warning restore MCP9005

		/// <summary>Unblocks every pending send, so an abandoned read loop can finish after the assertions.</summary>
		internal void Release() => _blocked.TrySetResult();
	}

	/// <summary>
	/// A parent leg whose send takes a measurable moment and OBSERVES the token it is given: a notification is
	/// recorded only if the send was allowed to finish.
	/// </summary>
	/// <remarks>
	/// The discriminator for the drain window. <see cref="RecordingParentSession"/> delays with
	/// <see cref="CancellationToken.None"/>, so its sends complete whether or not the session token was
	/// cancelled — which is exactly the blind spot that let a cancelled terminal stage event look delivered.
	/// </remarks>
	private sealed class DelayingParentSession : IParentMcpSession {

		private readonly TimeSpan _delay;
		private readonly List<JsonRpcNotification> _delivered = [];
		private readonly object _deliveredLock = new();
		private int _sendsStarted;

		internal DelayingParentSession(TimeSpan delay) => _delay = delay;

		public bool SupportsSampling => true;

		internal int SendsStarted => Volatile.Read(ref _sendsStarted);

		internal IReadOnlyList<JsonRpcNotification> Delivered {
			get {
				lock (_deliveredLock) {
					return [.. _delivered];
				}
			}
		}

		public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken) {
			Interlocked.Increment(ref _sendsStarted);
			await Task.Delay(_delay, cancellationToken);
			if (message is JsonRpcNotification notification) {
				lock (_deliveredLock) {
					_delivered.Add(notification);
				}
			}
		}

		// MCP9005: CreateMessageRequestParams / CreateMessageResult are deprecated in SDK 2.2.0 (SEP-2577).
		// Suppressed with this justification rather than silently, matching the production adapter.
#pragma warning disable MCP9005
		public ValueTask<CreateMessageResult> SampleAsync(CreateMessageRequestParams requestParams,
			CancellationToken cancellationToken) =>
			throw new NotSupportedException("This fake exists to time notification delivery, not to sample.");
#pragma warning restore MCP9005
	}

	/// <summary>
	/// The parent leg, recording. It records a notification only AFTER its (optionally delayed) send
	/// completes, which is what makes the ordering assertion meaningful: concurrent forwarding would record
	/// the events in the delays' order rather than the wire's.
	/// </summary>
	private sealed class RecordingParentSession : IParentMcpSession {

		internal const string SampledModel = "fake-client-model";

		// MCP9005: the sampling payload types are deprecated in SDK 2.2.0 (SEP-2577). Suppressed with this
		// justification, matching the production adapter: sampling still works and ADR rule 1 depends on
		// it, so it must stay covered until OQ-6 migrates to InputRequest / ResolveInputRequestsAsync.
#pragma warning disable MCP9005
		private readonly List<JsonRpcNotification> _notifications = [];
		private readonly List<CreateMessageRequestParams> _samplingRequests = [];
		private readonly object _recordLock = new();

		public bool SupportsSampling { get; init; } = true;

		public Func<JsonRpcNotification, Task> BeforeSend { get; init; }

		public IReadOnlyList<JsonRpcNotification> Notifications {
			get {
				lock (_recordLock) {
					return [.. _notifications];
				}
			}
		}

		public IReadOnlyList<CreateMessageRequestParams> SamplingRequests {
			get {
				lock (_recordLock) {
					return [.. _samplingRequests];
				}
			}
		}

		public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken) {
			if (message is not JsonRpcNotification notification) {
				return;
			}
			if (BeforeSend is not null) {
				await BeforeSend(notification);
			}
			lock (_recordLock) {
				_notifications.Add(notification);
			}
		}

		public ValueTask<CreateMessageResult> SampleAsync(CreateMessageRequestParams requestParams,
			CancellationToken cancellationToken) {
			lock (_recordLock) {
				_samplingRequests.Add(requestParams);
			}
			return new ValueTask<CreateMessageResult>(new CreateMessageResult {
				Model = SampledModel,
				Role = Role.Assistant,
				Content = [new TextContentBlock { Text = "{\"verdict\":\"ok\"}" }]
			});
		}
#pragma warning restore MCP9005
	}
}
