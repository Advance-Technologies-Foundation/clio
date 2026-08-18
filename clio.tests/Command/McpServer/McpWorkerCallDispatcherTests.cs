using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Relay;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.UserEnvironment;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The Stage 6 worker dispatcher: the piece that turns a <see cref="McpExecutionDisposition.Worker"/>
/// decision into a leased, contained, bounded child process, and turns whatever happens to that child
/// into an answer the caller can act on.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is NOT covered here, and why.</b> The happy path and the budget kill both need a live
/// <c>WorkerRelaySession</c>, which is a sealed type over a real transport, so a mock relay could only
/// assert the mock. What this fixture pins is everything reachable WITHOUT a child: the budget parse, the
/// caller-defect guard, the spawn-failure path, and the wire shape of the two error envelopes an agent
/// branches on. The budget kill is proven end to end (TC-E-601…604).
/// </para>
/// <para>
/// <b>The happy path IS covered, one fixture over.</b> An earlier version of this remark said no substitute
/// could produce a live session; that reads as "the happy path is unreachable in a unit test", and it is
/// not — <c>WorkerProgressStreamingTests</c> drives the real dispatcher, transport owner, relay and SDK
/// stream transport against a scripted child speaking JSON-RPC over an ordinary pipe pair. It needs no
/// substitute for the session, only a pipe. Recorded here because the earlier sentence was load-bearing
/// enough to have kept the progress-streaming regression out of the unit suite.
/// </para>
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpWorkerCallDispatcherTests {

	private IWorkerProcessSupervisor _supervisor;
	private IWorkerChildTransportOwner _transportOwner;
	private IWorkerMcpRelay _relay;
	private ISettingsRepository _settingsRepository;
	private ILogger _logger;

	[SetUp]
	public void SetUp() {
		_supervisor = Substitute.For<IWorkerProcessSupervisor>();
		_transportOwner = Substitute.For<IWorkerChildTransportOwner>();
		_relay = Substitute.For<IWorkerMcpRelay>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_logger = Substitute.For<ILogger>();
	}

	[TearDown]
	public void TearDown() {
		_supervisor.ClearReceivedCalls();
		_transportOwner.ClearReceivedCalls();
		_relay.ClearReceivedCalls();
		_settingsRepository.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	private McpWorkerCallDispatcher CreateSut(TimeSpan? budget = null) =>
		new(_supervisor, _transportOwner, _relay, _settingsRepository, _logger,
			budget ?? TimeSpan.FromSeconds(5));

	// ---------------------------------------------------------------------------------------------
	// Budget resolution
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("The worker budget default is the 120 s the in-process read deadline already used, so moving a tool into a worker does not silently change how long a client waits.")]
	public void ResolveBudget_ShouldReturnTheDefault_WhenNoOverrideIsSet() {
		// Arrange & Act
		TimeSpan resolved = McpWorkerCallDispatcher.ResolveBudget(null);

		// Assert
		resolved.Should().Be(TimeSpan.FromSeconds(120),
			because: "the default must match the deadline it replaces; a shorter one would start bounding calls that used to succeed, on a platform where spawn alone costs 2.76 s (ADR §2.4)");
	}

	[Test]
	[Category("Unit")]
	[TestCase("12", 12)]
	[TestCase("0.5", 0)]
	[TestCase("3600", 3600)]
	[Description("A valid in-range seconds override is honoured, so an end-to-end fixture can bound a call without waiting two minutes for it.")]
	public void ResolveBudget_ShouldHonourInRangeOverrides(string rawValue, int expectedWholeSeconds) {
		// Arrange & Act
		TimeSpan resolved = McpWorkerCallDispatcher.ResolveBudget(rawValue);

		// Assert
		((int)resolved.TotalSeconds).Should().Be(expectedWholeSeconds,
			because: $"'{rawValue}' is inside the accepted range and must be taken literally rather than rounded to the default");
	}

	[Test]
	[Category("Unit")]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("not-a-number")]
	[TestCase("0")]
	[TestCase("-5")]
	[TestCase("3601")]
	[Description("An unparseable, zero, negative or out-of-range override falls back to the default rather than producing a budget that kills every call instantly or never at all.")]
	public void ResolveBudget_ShouldFallBackToTheDefault_WhenOverrideIsUnusable(string rawValue) {
		// Arrange & Act
		TimeSpan resolved = McpWorkerCallDispatcher.ResolveBudget(rawValue);

		// Assert
		resolved.Should().Be(TimeSpan.FromSeconds(120),
			because: $"'{rawValue}' cannot be honoured, and a zero-or-negative budget would kill every worker before it finished starting");
	}

	// ---------------------------------------------------------------------------------------------
	// The caller-defect guard
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("The dispatcher executes a routing decision and never re-makes one: handed an in-process route it throws instead of spawning, so a dispatch-site mistake surfaces at the seam rather than as an unexplained extra process.")]
	public async Task DispatchAsync_ShouldThrowWithoutSpawning_WhenRouteIsNotAWorkerRoute() {
		// Arrange
		McpWorkerCallDispatcher sut = CreateSut();
		McpExecutionRoute inProcessRoute = new(
			"list-pages",
			McpToolExecutionLocation.Worker,
			McpExecutionDisposition.InProcessOutsideCohort,
			Metadata: null);

		// Act
		Func<Task> act = async () => await sut.DispatchAsync(
			inProcessRoute,
			new CallToolRequestParams { Name = "list-pages" },
			Substitute.For<IParentMcpSession>(),
			CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>(
			because: "an in-process route reaching the worker dispatcher is a dispatch-site defect, and a dispatcher that quietly relayed anyway would make the router's decision unenforceable");
		await _supervisor.DidNotReceiveWithAnyArgs().SpawnContainedAsync(default, default);
	}

	// ---------------------------------------------------------------------------------------------
	// Spawn failure
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("A worker that cannot be started is answered with the RELAY-FAILURE class, never the timeout class — telling an agent to wait and retry would hide a clio defect behind a retry loop.")]
	public async Task DispatchAsync_ShouldReturnRelayFailure_WhenTheWorkerCannotBeStarted() {
		// Arrange
		_supervisor
			.SpawnContainedAsync(Arg.Any<WorkerSpawnRequest>(), Arg.Any<CancellationToken>())
			.Returns<Task<IWorkerLease>>(_ => throw new InvalidOperationException("no dotnet host"));
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		CallToolResult result = await sut.DispatchAsync(
			WorkerRoute("list-pages"),
			new CallToolRequestParams { Name = "list-pages" },
			Substitute.For<IParentMcpSession>(),
			CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "a call that never reached a worker did not happen, and saying otherwise would be a fabricated answer");
		ErrorClassOf(result).Should().Be(McpWorkerCallDispatcher.RelayFailureErrorClass,
			because: "a failure to START is a clio-side defect; classifying it as creatio-timeout would send the agent into a retry loop against a healthy stand");
		ErrorClassOf(result).Should().NotBe(McpWorkerCallDispatcher.BudgetExpiredErrorClass,
			because: "the two classes drive different agent behaviour and must stay distinguishable");
	}

	// ---------------------------------------------------------------------------------------------
	// The worker's working directory — where a cohort tool's files land
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("The spawn request states the HOST's working directory, because a null one does not mean 'the parent's': the supervisor then falls back to the clio installation directory, and a cohort get-page writes the user's .clio-pages tree into clio's own install tree while answering success.")]
	public async Task DispatchAsync_ShouldSpawnTheWorkerInTheHostsWorkingDirectory() {
		// Arrange
		WorkerSpawnRequest captured = null;
		_supervisor
			.SpawnContainedAsync(Arg.Any<WorkerSpawnRequest>(), Arg.Any<CancellationToken>())
			.Returns<Task<IWorkerLease>>(call => {
				captured = call.Arg<WorkerSpawnRequest>();
				throw new InvalidOperationException("spawn short-circuited: the request is what this test reads");
			});
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		await sut.DispatchAsync(
			WorkerRoute("get-page"),
			new CallToolRequestParams { Name = "get-page" },
			Substitute.For<IParentMcpSession>(),
			CancellationToken.None);

		// Assert
		captured.Should().NotBeNull(
			because: "the dispatcher must reach the supervisor for there to be a spawn request to state anything about");
		captured.WorkingDirectory.Should().Be(Environment.CurrentDirectory,
			because: "the child has to see the same 'here' the host does — .clio-pages/{schema}/ is anchored on the process current directory, so a worker started anywhere else relocates a user's files silently");
		captured.WorkingDirectory.Should().NotBeNull(
			because: "null is the specific value that reintroduces the defect: the supervisor's fallback chain then picks the directory the clio assembly lives in");
	}

	[Test]
	[Category("Unit")]
	[Description("The spawn-request composition used in production is the one a child-process test can drive, so the working directory, the worker verb and the budget are proven on the same object the dispatcher hands the supervisor.")]
	public void ComposeSpawnRequest_ShouldCarryTheHostDirectoryTheWorkerVerbAndTheBudget() {
		// Arrange
		Dictionary<string, string> childEnvironment = new(StringComparer.Ordinal) { ["CLIO_MCP_WORKER"] = "1" };

		// Act
		WorkerSpawnRequest request =
			McpWorkerCallDispatcher.ComposeSpawnRequest(childEnvironment, TimeSpan.FromSeconds(7));

		// Assert
		request.WorkingDirectory.Should().Be(Environment.CurrentDirectory,
			because: "this is the single place the host's directory is stated, and an end-to-end test spawns a real child from this very object to prove the child actually starts there");
		request.Arguments.Should().Equal(["mcp-server", "--worker"],
			because: "a worker child is clio's own MCP server in worker mode, and the flag is what the recursion guard reads");
		request.Budget.Should().Be(TimeSpan.FromSeconds(7),
			because: "the caller's budget is what the parent measures from spawn and kills on");
		request.EnvironmentVariables.Should().BeSameAs(childEnvironment,
			because: "the frozen feature payload must reach the child unchanged rather than being rebuilt here");
	}

	// ---------------------------------------------------------------------------------------------
	// Relayed params — the only place this feature does not forward verbatim
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("The four session-describing _meta keys are stripped from the relayed params: the parent's client negotiated one protocol revision and the relay's child leg another, so forwarding them made every worker call fail with 'The negotiated protocol version cannot change within a session'.")]
	public void WithoutParentSessionMetadata_ShouldRemoveEveryParentSessionKey() {
		// Arrange
		CallToolRequestParams parameters = new() {
			Name = "list-pages",
			Meta = new JsonObject {
				["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
				["io.modelcontextprotocol/clientInfo"] = new JsonObject { ["name"] = "some-client" },
				["io.modelcontextprotocol/clientCapabilities"] = new JsonObject { ["sampling"] = new JsonObject() },
				["io.modelcontextprotocol/sessionId"] = "session-1"
			}
		};

		// Act
		CallToolRequestParams relayed = McpWorkerCallDispatcher.WithoutParentSessionMetadata(parameters);

		// Assert
		relayed.Meta.Should().NotContainKey("io.modelcontextprotocol/protocolVersion",
			because: "the SDK's server-side check is right — a request carrying the OTHER session's negotiated version is genuinely contradictory and fails the call");
		relayed.Meta.Should().NotContainKey("io.modelcontextprotocol/clientInfo",
			because: "the client on the child leg is the relay, not the parent's client");
		relayed.Meta.Should().NotContainKey("io.modelcontextprotocol/clientCapabilities",
			because: "capabilities describe the parent's session and would misdescribe what the child leg can do");
		relayed.Meta.Should().NotContainKey("io.modelcontextprotocol/sessionId",
			because: "the child leg is a different session, and claiming the parent's id would make two sessions indistinguishable in a log");
	}

	[Test]
	[Category("Unit")]
	[Description("Everything the contract depends on rides through the strip untouched — the progress token ClioRing correlates on, the clioStageEvent payload, and any key neither leg knows about — because ADR rule 1 forwards raw and only the session descriptors may not travel.")]
	public void WithoutParentSessionMetadata_ShouldPreserveEveryOtherKeyByteIdentically() {
		// Arrange
		JsonObject stageEvent = new() { ["stage"] = "deploy", ["status"] = "running", ["sequence"] = 3 };
		JsonObject unknownToBothLegs = new() { ["nested"] = new JsonArray(1, 2, 3) };
		CallToolRequestParams parameters = new() {
			Name = "list-pages",
			Meta = new JsonObject {
				["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
				["progressToken"] = "token-42",
				["clioStageEvent"] = stageEvent.DeepClone(),
				["vendor/whatever"] = unknownToBothLegs.DeepClone()
			}
		};

		// Act
		CallToolRequestParams relayed = McpWorkerCallDispatcher.WithoutParentSessionMetadata(parameters);

		// Assert
		relayed.Meta["progressToken"].GetValue<string>().Should().Be("token-42",
			because: "ClioRing correlates progress on the token ordinally and fails SILENTLY on a mismatch, so it must survive the relay unchanged");
		JsonNode.DeepEquals(relayed.Meta["clioStageEvent"], stageEvent).Should().BeTrue(
			because: "stage events are read raw by ClioRing; rebuilding or reordering the payload breaks the consumer that never complains");
		JsonNode.DeepEquals(relayed.Meta["vendor/whatever"], unknownToBothLegs).Should().BeTrue(
			because: "a key neither leg understands is exactly what 'relay verbatim' has to protect — dropping it would make the relay lossy for every future extension");
	}

	[Test]
	[Category("Unit")]
	[Description("The caller's own params object is handed to the child UNCOPIED when there is no _meta at all — a deliberate optimisation for the ordinary call, which a later 'always clone for safety' simplification would quietly remove.")]
	public void WithoutParentSessionMetadata_ShouldReturnTheCallersOwnObject_WhenThereIsNoMeta() {
		// Arrange
		CallToolRequestParams parameters = new() { Name = "list-pages" };

		// Act
		CallToolRequestParams relayed = McpWorkerCallDispatcher.WithoutParentSessionMetadata(parameters);

		// Assert
		relayed.Should().BeSameAs(parameters,
			because: "with nothing to strip there is nothing to copy, and the ordinary call must not pay for a clone it does not need");
	}

	[Test]
	[Category("Unit")]
	[Description("A _meta that carries none of the four session keys is left alone entirely — the same object, not an equal copy — so only a request that actually contradicts the child's session is rewritten.")]
	public void WithoutParentSessionMetadata_ShouldReturnTheCallersOwnObject_WhenMetaCarriesNoSessionKey() {
		// Arrange
		JsonObject meta = new() { ["progressToken"] = 17 };
		CallToolRequestParams parameters = new() { Name = "list-pages", Meta = meta };

		// Act
		CallToolRequestParams relayed = McpWorkerCallDispatcher.WithoutParentSessionMetadata(parameters);

		// Assert
		relayed.Should().BeSameAs(parameters,
			because: "the strip is conditional on a session key being present; copying regardless would make every call allocate and would hide which calls are actually being rewritten");
		relayed.Meta.Should().BeSameAs(meta,
			because: "an untouched _meta must stay the caller's own node — a clone would silently break any identity the caller relies on");
	}

	[Test]
	[Category("Unit")]
	[Description("Stripping copies rather than mutates: the CALLER's params still carry all four session keys afterwards, because the parent's own request object is live on its session and an in-place Remove would corrupt it mid-call.")]
	public void WithoutParentSessionMetadata_ShouldNotMutateTheCallersParams() {
		// Arrange
		CallToolRequestParams parameters = new() {
			Name = "list-pages",
			Meta = new JsonObject {
				["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
				["io.modelcontextprotocol/clientInfo"] = new JsonObject { ["name"] = "some-client" },
				["io.modelcontextprotocol/clientCapabilities"] = new JsonObject(),
				["io.modelcontextprotocol/sessionId"] = "session-1"
			}
		};

		// Act
		CallToolRequestParams relayed = McpWorkerCallDispatcher.WithoutParentSessionMetadata(parameters);

		// Assert
		relayed.Should().NotBeSameAs(parameters,
			because: "a request that has to be rewritten must be rewritten on a COPY; the caller's object belongs to the parent's session");
		parameters.Meta.Should().ContainKey("io.modelcontextprotocol/protocolVersion",
			because: "an in-place strip would pass the 'keys removed' assertion while destroying the parent session's own request metadata");
		parameters.Meta.Should().ContainKey("io.modelcontextprotocol/sessionId",
			because: "the same in-place mutation would take every session key with it, and nothing downstream would report the loss");
	}

	[Test]
	[Category("Unit")]
	[Description("The copy carries the WHOLE params surface — name, arguments, input responses and request state — because CallToolRequestParams inherits four settable properties from RequestParams and a copy that rebuilds only three drops data on exactly the calls that carry session metadata.")]
	public void WithoutParentSessionMetadata_ShouldCarryEverySettablePropertyOntoTheCopy() {
		// Arrange
		Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal) {
			["schema-name"] = JsonSerializer.SerializeToElement("UsrPage")
		};
		Dictionary<string, InputResponse> inputResponses = new(StringComparer.Ordinal) {
			["confirm"] = new InputResponse { RawValue = JsonSerializer.SerializeToElement("yes") }
		};
		CallToolRequestParams parameters = new() {
			Name = "get-page",
			Arguments = arguments,
			InputResponses = inputResponses,
			RequestState = "opaque-state-echoed-back",
			Meta = new JsonObject { ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28" }
		};

		// Act
		CallToolRequestParams relayed = McpWorkerCallDispatcher.WithoutParentSessionMetadata(parameters);

		// Assert
		relayed.Name.Should().Be("get-page",
			because: "the child resolves the tool by name, so a copy that lost it would call nothing at all");
		relayed.Arguments.Should().BeSameAs(arguments,
			because: "the tool's own arguments are relayed by reference — copying them would be pointless work and rebuilding them would risk changing their JSON");
		relayed.InputResponses.Should().BeSameAs(inputResponses,
			because: "these are the client's answers to a previous input request, and a retry that arrives at the worker without them re-asks a question the user already answered");
		relayed.RequestState.Should().Be("opaque-state-echoed-back",
			because: "the protocol requires this value to be echoed back without modification, so dropping it on the copy breaks the retry it belongs to");
	}

	// ---------------------------------------------------------------------------------------------
	// Error envelope shapes — what an agent actually branches on
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("The budget-expired envelope carries the SAME creatio-timeout wire token the in-process read deadline used, so shipped agent guidance keeps applying as tools move into workers, plus a marker that says the work was terminated rather than abandoned.")]
	public void BudgetExpiredResult_ShouldCarryTheTimeoutContractAndTheWorkerMarker() {
		// Arrange & Act
		CallToolResult result = McpWorkerCallDispatcher.BudgetExpiredResult(
			"list-pages", TimeSpan.FromSeconds(12), standardErrorTail: null);

		// Assert
		result.IsError.Should().BeTrue(
			because: "a bounded call did not answer, and the caller has to be able to tell that from a successful empty result");
		JsonElement structured = StructuredOf(result);
		structured.GetProperty("success").ValueKind.Should().Be(JsonValueKind.False,
			because: "every clio MCP envelope is branched on `success` first");
		structured.GetProperty("error-class").GetString().Should().Be("creatio-timeout",
			because: "the token is deliberately shared with the deadline this replaces — from a client's point of view 'clio bounded this read' is one situation with one correct response");
		structured.GetProperty("worker-budget-expired").ValueKind.Should().Be(JsonValueKind.True,
			because: "the marker is what distinguishes a TERMINATED worker from the abandoned in-process read, which is the whole behavioural difference this feature delivers");
		structured.GetProperty("budget-seconds").GetInt32().Should().Be(12,
			because: "an agent cannot decide whether to narrow the query or raise the budget without knowing which budget expired");
	}

	[Test]
	[Category("Unit")]
	[Description("A worker's standard-error tail travels on the failure envelope, because without it a worker that died at startup yields only 'the worker closed its transport before answering'.")]
	public void RelayFailureResult_ShouldCarryTheWorkerStandardErrorTail() {
		// Arrange & Act
		CallToolResult result = McpWorkerCallDispatcher.RelayFailureResult(
			"get-schema", "the worker relay failed", detail: "pipe closed",
			standardErrorTail: new McpWorkerCallDispatcher.WorkerStandardErrorTail(
				"Unhandled exception: could not load appsettings", Truncated: false));

		// Assert
		JsonElement structured = StructuredOf(result);
		structured.GetProperty("error-class").GetString()
			.Should().Be(McpWorkerCallDispatcher.RelayFailureErrorClass,
			because: "a relay failure is not a timeout and must not be retried blindly");
		structured.GetProperty("worker-stderr").GetString()
			.Should().Contain("could not load appsettings",
			because: "the child's own diagnosis is the only evidence of why it died, and it is otherwise discarded with the process");
	}

	[Test]
	[Category("Unit")]
	[Description("A failure envelope with nothing on the worker's standard error omits the field entirely rather than carrying an empty string, so 'the worker said nothing' and 'the worker said this' stay different values.")]
	public void RelayFailureResult_ShouldOmitTheStandardErrorField_WhenTheWorkerSaidNothing() {
		// Arrange & Act
		CallToolResult result = McpWorkerCallDispatcher.RelayFailureResult(
			"get-schema", "the worker returned a null tool result", detail: null,
			standardErrorTail: new McpWorkerCallDispatcher.WorkerStandardErrorTail("   ", Truncated: false));

		// Assert
		StructuredOf(result).TryGetProperty("worker-stderr", out JsonElement _).Should().BeFalse(
			because: "an empty diagnostic reads as 'the worker explained itself and had nothing to say', which is a different claim from 'there was no output at all'");
	}

	// ---------------------------------------------------------------------------------------------
	// The worker's standard error — the bound, and saying so when it bites (TC-U-203)
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("A trimmed standard-error tail SAYS it was trimmed, and states the bound clio kept: handed the last characters of a 40 KB stack trace with nothing marking the cut, a reader sees text starting mid-frame, cannot see that the exception line is missing, and diagnoses the wrong layer.")]
	public void RelayFailureResult_ShouldMarkTheTailAsTruncated_WhenTheWorkerWroteMoreThanTheBound() {
		// Arrange
		McpWorkerCallDispatcher.WorkerStandardErrorTail tail = new(
			"rker.Startup.Run() — a tail that begins mid-frame", Truncated: true);

		// Act
		CallToolResult result = McpWorkerCallDispatcher.RelayFailureResult(
			"get-page", "the worker relay failed", detail: "pipe closed", standardErrorTail: tail);

		// Assert
		JsonElement structured = StructuredOf(result);
		structured.GetProperty("worker-stderr-truncated").ValueKind.Should().Be(JsonValueKind.True,
			because: "without the marker a partial diagnosis is indistinguishable from a whole one, and the reader believes the visible text is the whole failure");
		structured.GetProperty("worker-stderr-tail-chars").GetInt32()
			.Should().Be(McpWorkerCallDispatcher.StandardErrorTailLimit,
			because: "'something was cut' is only actionable with the size of what was kept, and the number must be READ from the constant so a change to the bound cannot leave the envelope lying");
		structured.GetProperty("error").GetString().Should().Contain(
			McpWorkerCallDispatcher.StandardErrorTailLimit.ToString(CultureInfo.InvariantCulture),
			because: "a person reads the sentence, not the JSON; the bound has to be in the text they actually see");
	}

	[Test]
	[Category("Unit")]
	[Description("The budget-expired envelope carries the same truncation contract as the relay-failure one, because a worker killed at its budget is exactly as likely to have flooded standard error as one that crashed.")]
	public void BudgetExpiredResult_ShouldMarkTheTailAsTruncated_WhenTheWorkerWroteMoreThanTheBound() {
		// Arrange
		McpWorkerCallDispatcher.WorkerStandardErrorTail tail = new(
			"udget, from a tail that begins mid-frame", Truncated: true);

		// Act
		CallToolResult result = McpWorkerCallDispatcher.BudgetExpiredResult(
			"get-page", TimeSpan.FromSeconds(12), standardErrorTail: tail);

		// Assert
		JsonElement structured = StructuredOf(result);
		structured.GetProperty("worker-stderr-truncated").ValueKind.Should().Be(JsonValueKind.True,
			because: "the two envelopes are read by the same agent and a marker present on one and absent on the other would read as 'this one was complete'");
		structured.GetProperty("worker-stderr-tail-chars").GetInt32()
			.Should().Be(McpWorkerCallDispatcher.StandardErrorTailLimit,
			because: "the bound is a property of the drain, not of the failure class that surfaced it");
		structured.GetProperty("error").GetString().Should().Contain(
			McpWorkerCallDispatcher.StandardErrorTailLimit.ToString(CultureInfo.InvariantCulture),
			because: "the human-readable sentence is what an operator pastes into a bug report, so it has to carry the caveat with it");
	}

	[Test]
	[Category("Unit")]
	[Description("A standard error that fitted inside the bound carries NO truncation marker at all, so 'the field is absent' stays a usable signal that the text is the worker's whole diagnosis rather than merely its end.")]
	public void RelayFailureResult_ShouldNotClaimTruncation_WhenTheWholeStandardErrorFits() {
		// Arrange
		McpWorkerCallDispatcher.WorkerStandardErrorTail tail = new(
			"Unhandled exception: could not load appsettings", Truncated: false);

		// Act
		CallToolResult result = McpWorkerCallDispatcher.RelayFailureResult(
			"get-page", "the worker relay failed", detail: null, standardErrorTail: tail);

		// Assert
		JsonElement structured = StructuredOf(result);
		structured.TryGetProperty("worker-stderr-truncated", out JsonElement _).Should().BeFalse(
			because: "a marker that is always present is not a marker; the agent has to be able to trust its absence");
		structured.TryGetProperty("worker-stderr-tail-chars", out JsonElement _).Should().BeFalse(
			because: "stating the bound on a complete diagnosis invites the reader to suspect a cut that never happened");
		structured.GetProperty("error").GetString().Should().NotContain(
			McpWorkerCallDispatcher.StandardErrorTailLimit.ToString(CultureInfo.InvariantCulture),
			because: "the caveat sentence must not appear on a message whose diagnosis is whole");
	}

	[Test]
	[Category("Unit")]
	[Description("The truncation marker is derived from what the DRAIN actually observed, not hand-set at the envelope: a worker that floods standard error through the real dispatch path produces an envelope that says it was trimmed.")]
	public async Task DispatchAsync_ShouldMarkTheTailAsTruncated_WhenTheWorkerFloodedItsStandardError() {
		// Arrange
		ArrangeWorkerThatFloodsStandardError(FloodChunkCount);
		McpWorkerCallDispatcher sut = CreateSut(TimeSpan.FromSeconds(30));

		// Act
		CallToolResult result = await DispatchAndAwaitTheAnswer(sut);

		// Assert
		JsonElement structured = StructuredOf(result);
		structured.GetProperty("worker-stderr-truncated").ValueKind.Should().Be(JsonValueKind.True,
			because: "the drain counts every character it read against the bound; a flag hand-set by a caller instead would keep passing while the count that produces it was wrong");
		structured.GetProperty("worker-stderr").GetString().Length
			.Should().BeLessThanOrEqualTo(McpWorkerCallDispatcher.StandardErrorTailLimit,
			because: "the marker has to describe a tail that really is bounded — an unbounded tail carrying a truncation flag would be the worst of both");
	}

	[Test]
	[Category("Unit")]
	[Description("PIN of the Stage 6 drain, not of the truncation marker: a worker writing far more standard error than any pipe buffer holds is drained continuously and the call still ANSWERS. An undrained redirected pipe blocks the child mid-write, which the parent sees as a call that never returns and blames on the stand.")]
	public async Task DispatchAsync_ShouldStillAnswer_WhenTheWorkerWritesMoreThanAPipeBufferHolds() {
		// Arrange
		ScriptedStandardErrorStream standardError = ArrangeWorkerThatFloodsStandardError(FloodChunkCount);
		McpWorkerCallDispatcher sut = CreateSut(TimeSpan.FromSeconds(30));

		// Act
		Task<CallToolResult> dispatch = sut.DispatchAsync(
			WorkerRoute("get-page"),
			new CallToolRequestParams { Name = "get-page" },
			Substitute.For<IParentMcpSession>(),
			CancellationToken.None).AsTask();
		Task finished = await Task.WhenAny(dispatch, Task.Delay(TimeSpan.FromSeconds(30)));

		// Assert
		standardError.Length.Should().BeGreaterThan(64 * 1024,
			because: "the flood has to exceed the largest ordinary pipe buffer, or it would not reach the blocking write this drain exists to prevent");
		finished.Should().BeSameAs(dispatch,
			because: "a dispatcher that stopped consuming the child's standard error would leave the child blocked on its write and the caller waiting forever, so the answer arriving at all is the property under test");
		(await dispatch).IsError.Should().BeTrue(
			because: "the scripted relay failed, and a flood of diagnostics must not turn a failure into a fabricated success");
	}

	[Test]
	[Category("Unit")]
	[Description("PIN of the Stage 6 drain, not of the truncation marker: the tail the pump collected reaches the caller through the real dispatch path. The other envelope tests hand a tail straight to the builder and never run the pump, so nothing else proves the two are connected.")]
	public async Task DispatchAsync_ShouldCarryTheDrainedStandardErrorOntoTheFailureEnvelope() {
		// Arrange
		ArrangeWorkerThatFloodsStandardError(FloodChunkCount);
		McpWorkerCallDispatcher sut = CreateSut(TimeSpan.FromSeconds(30));

		// Act
		CallToolResult result = await DispatchAndAwaitTheAnswer(sut);

		// Assert
		JsonElement structured = StructuredOf(result);
		structured.GetProperty("error-class").GetString()
			.Should().Be(McpWorkerCallDispatcher.RelayFailureErrorClass,
			because: "a child that closed its transport is a clio-side failure, and the tail is attached to that envelope rather than to a timeout");
		structured.GetProperty("worker-stderr").GetString().Should().Contain(StandardErrorFramePhrase,
			because: "the child's own diagnosis is the only evidence of why it died, and it is discarded with the process unless the pump's buffer is what the envelope reads");
	}

	[Test]
	[Category("Unit")]
	[Description("PIN of TC-U-505 (R-7) on the worker-stderr passthrough: a credential written by the child to standard error does not survive into ANY part of the tool result. The MCP result is copied verbatim into a model transcript and is routinely forwarded to a third-party LLM.")]
	public async Task DispatchAsync_ShouldRedactTheWorkersStandardError_BeforeItReachesTheCaller() {
		// Arrange
		ArrangeWorkerThatFloodsStandardError(FloodChunkCount);
		McpWorkerCallDispatcher sut = CreateSut(TimeSpan.FromSeconds(30));

		// Act
		CallToolResult result = await DispatchAndAwaitTheAnswer(sut);

		// Assert
		JsonElement structured = StructuredOf(result);
		structured.GetRawText().Should().NotContain(StandardErrorSecretMarker,
			because: "R-7 is a claim about the WHOLE envelope, not about one field: a marker that moved to a neighbouring key would still be forwarded to whatever reads the result");
		string renderedText = string.Join(
			"\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
		renderedText.Should().NotContain(StandardErrorSecretMarker,
			because: "the text block is what a human and a model both actually read, so a secret surviving there leaks by the shortest possible route");
		structured.GetProperty("worker-stderr").GetString().Should().Contain("password=",
			because: "redaction is surgical by design — the key stays so the message still reads sensibly, and its presence proves the secret was removed rather than the whole line never arriving");
	}

	[Test]
	[Category("Unit")]
	[Description("Story 21 (T-6/R-7): a bound that cuts the word 'password' in half must not hand the caller the value behind it. The redactor's credential pattern needs the KEY, so a tail beginning 'word=<value>' matches nothing and would otherwise be copied verbatim onto the failure envelope the client reads.")]
	public async Task DispatchAsync_ShouldNotLeakACredential_WhenTheBoundCutItsKeyInHalf() {
		// Arrange
		string standardErrorText = StandardErrorTextCutInsideACredentialKey();
		string boundedTail = standardErrorText[^McpWorkerCallDispatcher.StandardErrorTailLimit..];
		boundedTail.Should().StartWith(OrphanedCredentialValueFragment,
			because: "the orphan has to be MANUFACTURED — a payload padded so the cut lands in filler is exactly how this hole stayed invisible, so the fixture asserts its own precondition instead of assuming it");
		SensitiveErrorTextRedactor.Redact(boundedTail).Should().Contain(OrphanedSecretMarker,
			because: "the premise of the test is that the redactor alone cannot save a value whose key was trimmed away; if it could, the drain would have nothing left to guarantee");
		ArrangeWorkerWritingStandardError(standardErrorText);
		McpWorkerCallDispatcher sut = CreateSut(TimeSpan.FromSeconds(30));

		// Act
		CallToolResult result = await DispatchAndAwaitTheAnswer(sut);

		// Assert
		JsonElement structured = StructuredOf(result);
		structured.GetRawText().Should().NotContain(OrphanedSecretMarker,
			because: "R-7 is a claim about the whole envelope, and truncation upstream of the redactor must not be able to defeat it");
		string renderedText = string.Join(
			"\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
		renderedText.Should().NotContain(OrphanedSecretMarker,
			because: "the text block is what a human and a model both read, so a secret surviving there leaks by the shortest possible route");
		structured.GetProperty("worker-stderr").GetString().Should().Contain(OrphanedTailSurvivingPhrase,
			because: "only the partial FIRST line may be paid for the guarantee — a fix that discarded the whole tail would trade a leak for a blind operator");
	}

	[Test]
	[Category("Unit")]
	[Description("Story 21 design call, stated as behaviour: when the bound leaves not one complete line, the tail is withheld behind an explicit notice rather than surfaced or silently dropped — the caller still learns that clio kept something it would not show.")]
	public async Task DispatchAsync_ShouldWithholdTheTailBehindANotice_WhenNoCompleteLineSurvivedTheBound() {
		// Arrange
		string standardErrorText = UnbrokenStandardErrorTextCutInsideACredentialKey();
		standardErrorText.Should().NotContain("\n",
			because: "a worker emitting one unbroken line is the case where dropping the partial first line drops everything, and that is the case under test");
		ArrangeWorkerWritingStandardError(standardErrorText);
		McpWorkerCallDispatcher sut = CreateSut(TimeSpan.FromSeconds(30));

		// Act
		CallToolResult result = await DispatchAndAwaitTheAnswer(sut);

		// Assert
		JsonElement structured = StructuredOf(result);
		structured.GetRawText().Should().NotContain(OrphanedSecretMarker,
			because: "an unbroken line cut mid-key is the same leak as the line-oriented one, and withholding it is why the notice exists");
		structured.GetProperty("worker-stderr").GetString()
			.Should().Be(McpWorkerCallDispatcher.StandardErrorNoCompleteLineNotice,
			because: "an empty string here would take worker-stderr, the truncation marker and the caveat sentence off the envelope together, telling the reader the worker said nothing when clio in fact withheld what it kept");
		structured.GetProperty("worker-stderr-truncated").ValueKind.Should().Be(JsonValueKind.True,
			because: "the notice describes a tail that WAS trimmed, and the marker is the only thing that tells the reader the worker wrote more than this");
	}

	[Test]
	[Category("Unit")]
	[Description("The story 21 drop is keyed on truncation, not applied unconditionally: a worker whose whole standard error fits inside the bound reaches the caller with its first line intact — that line is usually the one naming the cause.")]
	public async Task DispatchAsync_ShouldKeepTheFirstLine_WhenTheWholeStandardErrorFitsInsideTheBound() {
		// Arrange
		const string firstLine = "Unhandled exception. Clio.Worker.StartupException: the frozen feature map was empty";
		ArrangeWorkerWritingStandardError(
			firstLine + "\n   " + StandardErrorFramePhrase + "\n   at Clio.Worker.Program.Main()\n");
		McpWorkerCallDispatcher sut = CreateSut(TimeSpan.FromSeconds(30));

		// Act
		CallToolResult result = await DispatchAndAwaitTheAnswer(sut);

		// Assert
		JsonElement structured = StructuredOf(result);
		structured.GetProperty("worker-stderr").GetString().Should().StartWith(firstLine,
			because: "nothing was cut, so no line can be partial and there is nothing for the guarantee to protect the reader from");
		structured.TryGetProperty("worker-stderr-truncated", out JsonElement _).Should().BeFalse(
			because: "a diagnosis that arrived whole must not be marked as trimmed, or the reader discounts evidence that is complete");
	}

	// ---------------------------------------------------------------------------------------------
	// Helpers
	// ---------------------------------------------------------------------------------------------

	private static McpExecutionRoute WorkerRoute(string toolName) =>
		new(toolName, McpToolExecutionLocation.Worker, McpExecutionDisposition.Worker, Metadata: null);

	private static JsonElement StructuredOf(CallToolResult result) =>
		result.StructuredContent is JsonElement structured
			? structured
			: throw new InvalidOperationException("The result carries no structured content.");

	private static string ErrorClassOf(CallToolResult result) =>
		StructuredOf(result).GetProperty("error-class").GetString();

	/// <summary>Chunks of scripted standard error: 96 KB, comfortably past any ordinary 64 KB pipe buffer.</summary>
	private const int FloodChunkCount = 96;

	/// <summary>A credential value TC-U-505 requires to be absent from every surfaced byte.</summary>
	private const string StandardErrorSecretMarker = "S3CRET-MARKER-DO-NOT-LEAK";

	/// <summary>The part of a scripted stack frame that must SURVIVE redaction and reach the caller.</summary>
	private const string StandardErrorFramePhrase = "at Clio.Worker.Startup.Run()";

	/// <summary>A credential value story 21 requires to be absent even when the bound cut its key in half.</summary>
	private const string OrphanedSecretMarker = "ORPHANED-TAIL-MARKER-DO-NOT-LEAK";

	/// <summary>
	/// What the bound leaves of <c>password=&lt;value&gt;</c> when it cuts between <c>pass</c> and
	/// <c>word</c>: a value with no key in front of it, which every redaction pattern misses.
	/// </summary>
	private const string OrphanedCredentialValueFragment = "word=" + OrphanedSecretMarker;

	/// <summary>The frame that must SURVIVE the drop of the partial first line, proving the drop is minimal.</summary>
	private const string OrphanedTailSurvivingPhrase = "at Clio.Worker.Session.Open()";

	/// <summary>
	/// Builds scripted worker standard error whose bound cuts the word <c>password</c> in half, so the
	/// kept tail begins <c>word=&lt;marker&gt;</c> — a credential value with its key on the other side of
	/// the cut.
	/// </summary>
	/// <returns>The scripted text.</returns>
	/// <remarks>
	/// The last <see cref="McpWorkerCallDispatcher.StandardErrorTailLimit"/> characters are laid out
	/// EXACTLY: the orphaned fragment, a line break, and one complete surviving line padded to fill the
	/// rest. Everything before them ends in <c>pass</c>, so the full stream carries an ordinary
	/// <c>password=…</c> pair that the redactor would have caught had nothing been cut. The tail is sized
	/// from the dispatcher's own constant rather than from a literal, so a change to the bound moves the
	/// cut with it instead of quietly padding the orphan back into filler.
	/// </remarks>
	private static string StandardErrorTextCutInsideACredentialKey() {
		int limit = McpWorkerCallDispatcher.StandardErrorTailLimit;
		string orphanedLine = OrphanedCredentialValueFragment + " while opening the worker session\n";
		string survivingLine =
			("   " + OrphanedTailSurvivingPhrase + " ").PadRight(limit - orphanedLine.Length - 1, '.') + "\n";
		return "   at Clio.Worker.Session.Bind() frame 0000 pass" + orphanedLine + survivingLine;
	}

	/// <summary>
	/// Builds the same mid-key cut over standard error containing NO line break at all — the case where
	/// dropping the partial first line drops the whole tail.
	/// </summary>
	/// <returns>The scripted text.</returns>
	private static string UnbrokenStandardErrorTextCutInsideACredentialKey() {
		int limit = McpWorkerCallDispatcher.StandardErrorTailLimit;
		string keptTail = (OrphanedCredentialValueFragment + " ").PadRight(limit, '.');
		return "   at Clio.Worker.Session.Bind() wrote one unbroken line pass" + keptTail;
	}

	/// <summary>
	/// Builds scripted worker standard error as <paramref name="chunkCount"/> blocks of exactly
	/// <see cref="ScriptedStandardErrorStream.ChunkSize"/> ASCII characters.
	/// </summary>
	/// <param name="chunkCount">How many blocks to write.</param>
	/// <returns>The scripted text.</returns>
	/// <remarks>
	/// <para>
	/// Every block is ONE line: padding, then the frame and the credential pair, then a line break. Two
	/// properties fall out of that and both are load-bearing: whatever the bound cuts, the surviving tail
	/// still contains at least one COMPLETE <c>password=…</c> pair, so a content assertion cannot pass or
	/// fail on where the cut happened; and the cut can never leave a bare secret whose key was trimmed
	/// away, which would be a redaction hole this fixture must not manufacture for itself. The fixture
	/// that DOES manufacture it deliberately is
	/// <see cref="StandardErrorTextCutInsideACredentialKey"/> — read the two together.
	/// </para>
	/// <para>
	/// <b>The line breaks are not decoration.</b> Real worker standard error is line-oriented, and the
	/// drain now drops the partial FIRST line of a trimmed tail (story 21). A payload with no line breaks
	/// at all would exercise the withheld-notice path instead of the ordinary flood these tests describe.
	/// </para>
	/// </remarks>
	private static string ScriptedStandardErrorText(int chunkCount) {
		StringBuilder text = new(chunkCount * ScriptedStandardErrorStream.ChunkSize);
		for (int ordinal = 0; ordinal < chunkCount; ordinal++) {
			string frame = "   " + StandardErrorFramePhrase + " frame "
				+ ordinal.ToString("D4", CultureInfo.InvariantCulture)
				+ " password=" + StandardErrorSecretMarker;
			text.Append(frame.PadLeft(ScriptedStandardErrorStream.ChunkSize - 1, '.')).Append('\n');
		}
		return text.ToString();
	}

	/// <summary>
	/// Arranges a worker that starts, floods its standard error and then closes its transport without
	/// answering — the shape of a child that dies during startup.
	/// </summary>
	/// <param name="chunkCount">How many blocks the child writes before the stream ends.</param>
	/// <returns>The scripted standard-error stream, so a test can state how much was written.</returns>
	private ScriptedStandardErrorStream ArrangeWorkerThatFloodsStandardError(int chunkCount) =>
		ArrangeWorkerWritingStandardError(ScriptedStandardErrorText(chunkCount));

	/// <summary>
	/// Arranges the same dying worker as <see cref="ArrangeWorkerThatFloodsStandardError"/> over an
	/// explicit standard-error payload, so a test can state exactly where the bound will cut.
	/// </summary>
	/// <param name="text">The scripted standard error the child writes before its stream ends.</param>
	/// <returns>The scripted standard-error stream.</returns>
	private ScriptedStandardErrorStream ArrangeWorkerWritingStandardError(string text) {
		ScriptedStandardErrorStream standardError = new(text);
		IWorkerLease lease = Substitute.For<IWorkerLease>();
		lease.ProcessId.Returns(4242);
		lease.StandardInput.Returns(Stream.Null);
		lease.StandardOutput.Returns(Stream.Null);
		lease.StandardError.Returns(standardError);
		// Without a future expiry the remaining budget is negative, the linked source cancels immediately,
		// and the dispatch leaves through the budget path instead of the relay failure under test.
		lease.BudgetExpiresAtUtc.Returns(DateTimeOffset.UtcNow.AddSeconds(30));
		_supervisor
			.SpawnContainedAsync(Arg.Any<WorkerSpawnRequest>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(lease));
		_relay
			.OpenAsync(Arg.Any<ITransport>(), Arg.Any<IParentMcpSession>(), Arg.Any<WorkerRelayOptions>(),
				Arg.Any<CancellationToken>())
			.Returns(_ => FailOnceTheStandardErrorIsDrained(standardError));
		return standardError;
	}

	/// <summary>
	/// Fails the relay only after the scripted stream has been read to its end, so the envelope a test
	/// inspects is built from a buffer the pump has finished filling rather than from whatever it had
	/// managed to append by then.
	/// </summary>
	/// <param name="standardError">The scripted stream to wait on.</param>
	/// <returns>A task that always faults.</returns>
	private static async Task<WorkerRelaySession> FailOnceTheStandardErrorIsDrained(
		ScriptedStandardErrorStream standardError) {
		await standardError.Drained.ConfigureAwait(false);
		throw new IOException("the worker closed its transport before answering");
	}

	/// <summary>
	/// Dispatches one worker call and refuses to wait forever for it — a drain that stopped consuming
	/// would otherwise hang the whole run instead of failing one test.
	/// </summary>
	/// <param name="sut">The dispatcher under test.</param>
	/// <returns>The tool result.</returns>
	private static async Task<CallToolResult> DispatchAndAwaitTheAnswer(McpWorkerCallDispatcher sut) =>
		await sut.DispatchAsync(
			WorkerRoute("get-page"),
			new CallToolRequestParams { Name = "get-page" },
			Substitute.For<IParentMcpSession>(),
			CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(30));

	/// <summary>
	/// Stands in for a worker's redirected standard error: it hands out a fixed payload in reads of at most
	/// <see cref="ChunkSize"/> bytes that complete SYNCHRONOUSLY, then reports end of stream.
	/// </summary>
	/// <remarks>
	/// <b><see cref="Drained"/> is what makes the content assertions deterministic.</b> It completes on the
	/// end-of-stream read — the first read the drain issues after the whole payload was handed over — and
	/// because every earlier read completed synchronously, the pump has already appended what it was given
	/// by then. Without that signal a test would be racing a background pump and would fail intermittently
	/// on a loaded machine rather than on a real defect.
	/// </remarks>
	private sealed class ScriptedStandardErrorStream : Stream {

		/// <summary>Largest block handed over per read, matching the drain's own buffer size.</summary>
		internal const int ChunkSize = 1024;

		private readonly byte[] _payload;
		private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _position;

		/// <summary>Initializes a new instance of the <see cref="ScriptedStandardErrorStream"/> class.</summary>
		/// <param name="text">The ASCII text the scripted worker writes to standard error.</param>
		internal ScriptedStandardErrorStream(string text) {
			_payload = Encoding.ASCII.GetBytes(text);
		}

		/// <summary>Gets a task completing when the reader has consumed the payload and reached end of stream.</summary>
		internal Task Drained => _drained.Task;

		/// <inheritdoc/>
		public override bool CanRead => true;

		/// <inheritdoc/>
		public override bool CanSeek => false;

		/// <inheritdoc/>
		public override bool CanWrite => false;

		/// <inheritdoc/>
		public override long Length => _payload.Length;

		/// <inheritdoc/>
		public override long Position {
			get => _position;
			set => throw new NotSupportedException();
		}

		/// <inheritdoc/>
		public override int Read(byte[] buffer, int offset, int count) =>
			ReadCore(buffer.AsSpan(offset, count));

		/// <inheritdoc/>
		public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
			CancellationToken cancellationToken) =>
			Task.FromResult(ReadCore(buffer.AsSpan(offset, count)));

		/// <inheritdoc/>
		public override ValueTask<int> ReadAsync(Memory<byte> buffer,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(ReadCore(buffer.Span));

		/// <inheritdoc/>
		public override void Flush() {
		}

		/// <inheritdoc/>
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		/// <inheritdoc/>
		public override void SetLength(long value) => throw new NotSupportedException();

		/// <inheritdoc/>
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		private int ReadCore(Span<byte> destination) {
			if (_position >= _payload.Length) {
				_drained.TrySetResult();
				return 0;
			}
			int take = Math.Min(Math.Min(destination.Length, ChunkSize), _payload.Length - _position);
			_payload.AsSpan(_position, take).CopyTo(destination);
			_position += take;
			return take;
		}
	}
}
