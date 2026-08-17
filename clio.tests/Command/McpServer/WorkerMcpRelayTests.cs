using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
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
		List<string> offenders = [
			.. relayTypes.SelectMany(SignatureTypes)
				.Where(pair => forbidden.Contains(pair.Type))
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
