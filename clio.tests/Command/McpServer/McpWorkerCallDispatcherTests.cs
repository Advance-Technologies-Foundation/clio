using System;
using System.Collections.Generic;
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
/// <b>What is NOT covered here, and why.</b> The happy path and the budget kill both require a live
/// <c>WorkerRelaySession</c> — a sealed type over a real transport that no container and no substitute can
/// produce — so they are proven end to end (TC-E-601…604) rather than here. Pretending otherwise with a
/// mock relay would assert the mock. What this fixture pins is everything reachable WITHOUT a child: the
/// budget parse, the caller-defect guard, the spawn-failure path, and the wire shape of the two error
/// envelopes an agent branches on.
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
			standardErrorTail: "Unhandled exception: could not load appsettings");

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
			"get-schema", "the worker returned a null tool result", detail: null, standardErrorTail: "   ");

		// Assert
		StructuredOf(result).TryGetProperty("worker-stderr", out JsonElement _).Should().BeFalse(
			because: "an empty diagnostic reads as 'the worker explained itself and had nothing to say', which is a different claim from 'there was no output at all'");
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
}
