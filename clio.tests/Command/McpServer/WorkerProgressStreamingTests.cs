using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Relay;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.UserEnvironment;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Whether a worker-cohort tool that declares
/// <see cref="McpToolClientRequests.Progress"/> actually streams <c>notifications/progress</c> to the
/// REAL client once its execution moved into a child process.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this fixture exists separately from <c>WorkerMcpRelayTests</c>.</b> That fixture proves the relay
/// forwards what it is given, against a fake child transport that records TYPED messages. It cannot see the
/// two things that decide whether an operator observes a beat at all: whether the worker was launched with
/// the configuration that makes it beat, and whether the beat survives serialisation, the pipe, the
/// dispatcher and the relay together. Those are the halves that failed independently — the end-to-end
/// symptom (<c>ApplicationTool_Should_Stream_Progress_For_LongRunning_Call</c>, zero notifications observed)
/// was produced by the FIRST half while the second was intact, and a fixture that exercises only one of them
/// cannot tell those two defects apart.
/// </para>
/// <para>
/// <b>What is real here.</b> The dispatcher, the transport owner, the relay and the SDK's stream transport
/// are all the production types; only the supervisor is substituted, because a real one spawns a real clio.
/// In its place a scripted child speaks JSON-RPC over an ordinary pipe pair, so the beat is serialised,
/// framed, read off a pipe and forwarded exactly as a worker's would be. This also corrects the remark on
/// <c>McpWorkerCallDispatcherTests</c> that the happy path "requires a live <c>WorkerRelaySession</c> … that
/// no container and no substitute can produce": it needs no substitute — it needs a pipe.
/// </para>
/// <para>
/// <b>The one leg not covered, stated rather than implied.</b> The client end stops at
/// <see cref="IParentMcpSession"/>. The production implementation behind it,
/// <c>McpServerParentSession</c>, forwards to <c>McpServer.SendMessageAsync</c> — a sealed SDK type with no
/// interface — so the final hop is covered by the end-to-end suite, not here.
/// </para>
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class WorkerProgressStreamingTests {

	/// <summary>The cohort tool the end-to-end regression drives: read-only, worker-located, Progress.</summary>
	private const string CohortToolName = "list-app-sections";

	/// <summary>The caller's progress token, distinctive so a rebuilt or re-issued one is visible.</summary>
	private const string CallerProgressToken = "caller-progress-token-7";

	/// <summary>An operator's tuned beat cadence, as it would appear in the host's environment.</summary>
	private const string TunedHeartbeatInterval = "0.05";

	/// <summary>
	/// Ceiling on every wait in this fixture. Generous, because it must never be the thing that fails: a
	/// scripted child answers in milliseconds, so reaching this bound means nothing answered at all.
	/// </summary>
	private static readonly TimeSpan AssertionTimeout = TimeSpan.FromSeconds(30);

	private ILogger _logger;
	private ISettingsRepository _settingsRepository;
	private IWorkerProcessSupervisor _supervisor;

	[SetUp]
	public void SetUp() {
		_logger = Substitute.For<ILogger>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_supervisor = Substitute.For<IWorkerProcessSupervisor>();
	}

	[TearDown]
	public void TearDown() {
		_logger.ClearReceivedCalls();
		_settingsRepository.ClearReceivedCalls();
		_supervisor.ClearReceivedCalls();
	}

	[Test]
	[Category("Unit")]
	[Description("A cohort tool declaring RequiresClientRequests = Progress streams at least one notifications/progress to the client THROUGH a worker, carrying the caller's own progress token, when the host was tuned for a short heartbeat — the exact end-to-end scenario that observed zero notifications after execution moved into a child process.")]
	public async Task DispatchAsync_ShouldStreamTheWorkersProgressToTheClient_WhenTheHostWasTunedForAShortHeartbeat() {
		// Arrange — the operator tunes the HOST, which is the only place a person can tune it.
		Dictionary<string, string> hostEnvironment = new(StringComparer.Ordinal) {
			[McpProgressHeartbeat.IntervalOverrideEnvVar] = TunedHeartbeatInterval
		};
		using ScriptedWorkerChild worker = ArrangeWorker(hostEnvironment, BeatsWhenTheChildWasTunedToBeat);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		CallToolResult result = await DispatchAsync(sut, client);

		// Assert
		result.Should().NotBeNull(
			because: "the scripted worker answered the call, so the dispatcher must return that answer rather than a relay-failure envelope");
		result.IsError.Should().NotBeTrue(
			because: "a worker that answered normally must not be reported as an error, or the progress assertion below would be measuring a failed call");
		worker.ObservedHeartbeatInterval.Should().Be(TunedHeartbeatInterval,
			because: "McpProgressHeartbeat.DefaultInterval is captured at TYPE LOAD, so a worker that did not receive the tuned cadence at spawn can never be told afterwards — this is the half of the defect that lives in the environment allowlist, not in the relay");
		client.ProgressNotifications.Should().NotBeEmpty(
			because: "a long-running cohort tool must stream at least one progress notification so the client resets its inactivity timeout instead of timing out mid-call — the assertion the end-to-end regression makes");
		client.ProgressTokens.Should().OnlyContain(token => token == $"\"{CallerProgressToken}\"",
			because: "the client matches an incoming beat to the call by comparing the progress token ORDINALLY, so a token that was re-issued or retyped anywhere along the relay is dropped silently and the caller observes nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("Every notification a worker emits reaches the client, in the worker's own order and with the caller's token, INDEPENDENTLY of how the worker was configured — the arm that separates 'the child never beat' from 'the child beat and the beat was lost'.")]
	public async Task DispatchAsync_ShouldRelayEveryNotificationTheWorkerEmits_WhenTheWorkerBeatsRegardless() {
		// Arrange — a worker that beats whatever its environment says, so this arm measures the relay only.
		const int emittedBeats = 3;
		using ScriptedWorkerChild worker = ArrangeWorker(
			new Dictionary<string, string>(StringComparer.Ordinal), _ => emittedBeats);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		CallToolResult result = await DispatchAsync(sut, client);

		// Assert
		result.IsError.Should().NotBeTrue(
			because: "the scripted worker answered normally, and a failed call would make the notification counts below meaningless");
		client.ProgressNotifications.Should().HaveCount(emittedBeats,
			because: "the parent relays what the worker emitted — losing beats here would mean the forwarding path itself is broken, which is a far worse defect than a worker that was never told to beat");
		client.ProgressSequence.Should().Equal(Enumerable.Range(1, emittedBeats),
			because: "the relay owns the child's transport read loop precisely so notifications reach the client in the worker's own wire order (ADR rule 12)");
		client.ProgressTokens.Should().OnlyContain(token => token == $"\"{CallerProgressToken}\"",
			because: "every relayed beat has to carry the caller's own token, or a client correlating ordinally treats them as belonging to some other call");
		worker.CallCount.Should().Be(1,
			because: "one tool call must produce exactly one worker call — a retried call would inflate the beat count and make this assertion pass for the wrong reason");
	}

	[Test]
	[Category("Unit")]
	[Description("A worker that emits nothing produces no client notifications, so the two assertions above are measuring the worker's beats rather than incidental relay traffic such as the handshake.")]
	public async Task DispatchAsync_ShouldStreamNoProgress_WhenTheWorkerNeverBeats() {
		// Arrange — the host is NOT tuned, so a worker honouring its environment stays silent for a fast call.
		using ScriptedWorkerChild worker = ArrangeWorker(
			new Dictionary<string, string>(StringComparer.Ordinal), BeatsWhenTheChildWasTunedToBeat);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		CallToolResult result = await DispatchAsync(sut, client);

		// Assert
		result.IsError.Should().NotBeTrue(
			because: "a silent worker is a normal fast call, not a failure");
		worker.ObservedHeartbeatInterval.Should().BeNull(
			because: "nothing tuned the host, so nothing may appear in the child's environment — an inherited value here would mean the allowlist is leaking rather than carrying");
		client.ProgressNotifications.Should().BeEmpty(
			because: "the handshake and the tool response must not be counted as progress; without this control the two assertions above could pass on traffic the worker never emitted");
	}

	[Test]
	[Category("Unit")]
	[Description("The heartbeat cadence an operator sets on the host reaches the worker's environment through the shipped allowlist, because the worker resolves that cadence at type load and can never be told afterwards.")]
	public void ComposeEffectiveEnvironment_ShouldCarryTheHostHeartbeatCadence_WhenTheOperatorTunedIt() {
		// Arrange
		Dictionary<string, string> hostEnvironment = new(StringComparer.Ordinal) {
			[McpProgressHeartbeat.IntervalOverrideEnvVar] = TunedHeartbeatInterval
		};
		WorkerSpawnRequest request = McpWorkerCallDispatcher.ComposeSpawnRequest(
			McpWorkerEnvironment.ComposeChildEnvironment(
				new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase), McpWorkerLifetime.PerCall),
			TimeSpan.FromSeconds(30));

		// Act
		IReadOnlyDictionary<string, string> childEnvironment = WorkerProcessSupervisor.ComposeEffectiveEnvironment(
			request, name => hostEnvironment.GetValueOrDefault(name));

		// Assert
		childEnvironment.Should().ContainKey(McpProgressHeartbeat.IntervalOverrideEnvVar,
			because: "an operator tunes clio, not one process of it: a cadence that lands in the parent only gives a host whose parent beats and whose workers do not, and the difference is invisible because the parent is the process an operator can watch");
		childEnvironment[McpProgressHeartbeat.IntervalOverrideEnvVar].Should().Be(TunedHeartbeatInterval,
			because: "the value has to arrive verbatim — a cadence rewritten on the way down is a second, quieter version of the same defect");
	}

	[Test]
	[Category("Unit")]
	[Description("The read-deadline override is still withheld from a worker even though the sibling heartbeat variable is now inherited, because a second in-child deadline abandons work while holding the per-tenant monitor — the wedge this execution boundary exists to remove (ADR rule 11).")]
	public void ComposeEffectiveEnvironment_ShouldWithholdTheReadDeadline_WhenTheHostSetOne() {
		// Arrange
		Dictionary<string, string> hostEnvironment = new(StringComparer.Ordinal) {
			[McpWorkerEnvironment.ReadDeadlineVariableName] = "5",
			[McpProgressHeartbeat.IntervalOverrideEnvVar] = TunedHeartbeatInterval
		};
		WorkerSpawnRequest request = McpWorkerCallDispatcher.ComposeSpawnRequest(
			McpWorkerEnvironment.ComposeChildEnvironment(
				new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase), McpWorkerLifetime.PerCall),
			TimeSpan.FromSeconds(30));

		// Act
		IReadOnlyDictionary<string, string> childEnvironment = WorkerProcessSupervisor.ComposeEffectiveEnvironment(
			request, name => hostEnvironment.GetValueOrDefault(name));

		// Assert
		childEnvironment.Should().NotContainKey(McpWorkerEnvironment.ReadDeadlineVariableName,
			because: "the parent bounds an ordinary worker by KILLING it; an inherited read deadline would abandon the work inside the child while keeping the monitor, which is exactly the wedge this feature removes");
		childEnvironment.Should().ContainKey(McpProgressHeartbeat.IntervalOverrideEnvVar,
			because: "widening the allowlist for the heartbeat must not be read as licence to widen it for the deadline — the two variables are neighbours in name and opposites in policy");
	}

	[Test]
	[Category("Unit")]
	[Description("The RESPONSE deadline is absent from the allowlist because it is delegated, not excluded: a sticky worker keeps the parent's value verbatim while a per-call worker gets none, and only a per-lifetime composer can say that — an allowlist can only say 'always'.")]
	public void ComposeEffectiveEnvironment_ShouldDeliverTheResponseDeadlineToAStickyWorkerOnly() {
		// Arrange
		const string tunedResponseDeadline = "45";
		Dictionary<string, string> hostEnvironment = new(StringComparer.Ordinal) {
			[McpWorkerEnvironment.ResponseDeadlineVariableName] = tunedResponseDeadline
		};
		Dictionary<string, bool> noFeatures = new(StringComparer.OrdinalIgnoreCase);
		string ReadHost(string name) => hostEnvironment.GetValueOrDefault(name);

		// Act
		IReadOnlyDictionary<string, string> perCallEnvironment =
			WorkerProcessSupervisor.ComposeEffectiveEnvironment(
				McpWorkerCallDispatcher.ComposeSpawnRequest(
					McpWorkerEnvironment.ComposeChildEnvironment(
						noFeatures, McpWorkerLifetime.PerCall, ReadHost),
					TimeSpan.FromSeconds(30)),
				ReadHost);
		IReadOnlyDictionary<string, string> stickyEnvironment =
			WorkerProcessSupervisor.ComposeEffectiveEnvironment(
				McpWorkerCallDispatcher.ComposeSpawnRequest(
					McpWorkerEnvironment.ComposeChildEnvironment(
						noFeatures, McpWorkerLifetime.Sticky, ReadHost),
					TimeSpan.FromSeconds(30)),
				ReadHost);

		// Assert
		perCallEnvironment.Should().NotContainKey(McpWorkerEnvironment.ResponseDeadlineVariableName,
			because: "a per-call worker is bounded by the parent killing it, so a second in-child response budget has nothing to add and everything to confuse");
		stickyEnvironment.Should().Contain(
			McpWorkerEnvironment.ResponseDeadlineVariableName, tunedResponseDeadline,
			because: "a sticky worker's in-progress envelope is what RETURNS the call, and stripping the deadline turned a 25 s backend call into a 77 s block in the prototype (ADR rule 11) — so 'absent from the allowlist' must never be read as 'withheld from every worker'");
	}

	// ---------------------------------------------------------------------------------------------
	// Helpers
	// ---------------------------------------------------------------------------------------------

	/// <summary>
	/// How many beats a worker that honours its own environment emits: one when the host's cadence reached
	/// it, none otherwise. Deliberately a single beat rather than a timed pump — the assertion under test is
	/// "at least one reached the client", and a real cadence would make the test a stopwatch race.
	/// </summary>
	/// <param name="childEnvironment">The environment the worker was launched with.</param>
	/// <returns>The number of beats to emit.</returns>
	private static int BeatsWhenTheChildWasTunedToBeat(IReadOnlyDictionary<string, string> childEnvironment) =>
		childEnvironment.ContainsKey(McpProgressHeartbeat.IntervalOverrideEnvVar) ? 1 : 0;

	private McpWorkerCallDispatcher CreateSut() =>
		new(_supervisor, new WorkerChildTransportOwner(), new WorkerMcpRelay(_logger), _settingsRepository,
			_logger, TimeSpan.FromSeconds(30));

	private static async Task<CallToolResult> DispatchAsync(
		McpWorkerCallDispatcher sut, IParentMcpSession client) =>
		await sut.DispatchAsync(
			new McpExecutionRoute(CohortToolName, McpToolExecutionLocation.Worker,
				McpExecutionDisposition.Worker, Metadata: null),
			new CallToolRequestParams {
				Name = CohortToolName,
				Meta = new JsonObject { ["progressToken"] = CallerProgressToken }
			},
			client,
			CancellationToken.None).AsTask().WaitAsync(AssertionTimeout);

	/// <summary>
	/// Arranges the substituted supervisor to hand out a lease over a scripted child's pipes, with the
	/// child launched under the environment the PRODUCTION composition rule derives from
	/// <paramref name="hostEnvironment"/>.
	/// </summary>
	/// <param name="hostEnvironment">The parent process's environment, as an operator would have set it.</param>
	/// <param name="beatPlan">How many beats the child emits, given the environment it was launched with.</param>
	/// <returns>The scripted child, so a test can state what it saw and what it did.</returns>
	/// <remarks>
	/// The environment is composed by <see cref="WorkerProcessSupervisor.ComposeEffectiveEnvironment"/> itself rather
	/// than restated here. That is what closes the loop: if the allowlist stops carrying the cadence, this
	/// harness stops beating, and the failure appears where the operator would feel it instead of in a list
	/// asserting its own contents.
	/// </remarks>
	private ScriptedWorkerChild ArrangeWorker(
		IReadOnlyDictionary<string, string> hostEnvironment,
		Func<IReadOnlyDictionary<string, string>, int> beatPlan) {
		ScriptedWorkerChild worker = new(beatPlan);
		_supervisor
			.SpawnContainedAsync(Arg.Any<WorkerSpawnRequest>(), Arg.Any<CancellationToken>())
			.Returns(call => {
				WorkerSpawnRequest request = call.Arg<WorkerSpawnRequest>();
				worker.Start(WorkerProcessSupervisor.ComposeEffectiveEnvironment(
					request, name => hostEnvironment.GetValueOrDefault(name)));
				return Task.FromResult(worker.Lease);
			});
		return worker;
	}

	/// <summary>
	/// The client leg: records the raw notifications the relay hands upward, the way an MCP client's
	/// progress sink would see them.
	/// </summary>
	/// <remarks>
	/// Raw <see cref="JsonNode"/> is kept rather than a deserialised DTO, because the properties that fail
	/// silently are exactly the ones a typed round trip would repair: the progress token's JSON kind and any
	/// <c>_meta</c> key no DTO knows about.
	/// </remarks>
	private sealed class RecordingClientSession : IParentMcpSession {

		private readonly List<JsonRpcNotification> _notifications = [];
		private readonly object _notificationsLock = new();

		/// <inheritdoc/>
		public bool SupportsSampling => false;

		/// <summary>Gets the progress notifications the client received, in arrival order.</summary>
		internal IReadOnlyList<JsonRpcNotification> ProgressNotifications {
			get {
				lock (_notificationsLock) {
					return [.. _notifications.Where(notification =>
						notification.Method == NotificationMethods.ProgressNotification)];
				}
			}
		}

		/// <summary>Gets each received beat's progress token, as raw JSON text so its KIND is compared too.</summary>
		internal IReadOnlyList<string> ProgressTokens =>
			[.. ProgressNotifications.Select(notification =>
				notification.Params?["progressToken"]?.ToJsonString())];

		/// <summary>Gets each received beat's monotonic progress counter, in arrival order.</summary>
		internal IReadOnlyList<int> ProgressSequence =>
			[.. ProgressNotifications.Select(notification =>
				notification.Params?["progress"]?.GetValue<int>() ?? 0)];

		/// <inheritdoc/>
		public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken) {
			if (message is JsonRpcNotification notification) {
				lock (_notificationsLock) {
					_notifications.Add(notification);
				}
			}
			return Task.CompletedTask;
		}

		// MCP9005: the sampling payload types are deprecated in SDK 2.2.0 (SEP-2577). This client advertises
		// no sampling capability, so the member exists only to satisfy the interface.
#pragma warning disable MCP9005
		/// <inheritdoc/>
		public ValueTask<CreateMessageResult> SampleAsync(CreateMessageRequestParams requestParams,
			CancellationToken cancellationToken) =>
			throw new NotSupportedException("This client advertises no sampling capability.");
#pragma warning restore MCP9005
	}

	/// <summary>
	/// A worker child that speaks real newline-framed JSON-RPC over a real pipe pair: it completes the
	/// handshake, then answers one <c>tools/call</c> — emitting its beats BEFORE the response, the way a
	/// heartbeat around a synchronous backend call does.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The beats precede the response on ONE pipe, and that is what makes the test deterministic.</b> The
	/// relay's read loop consumes messages serially and forwards each notification awaited in place, so every
	/// beat has necessarily reached the client before the call's answer resolves. No polling, no waiting on a
	/// background pump.
	/// </para>
	/// <para>
	/// Anonymous pipes rather than in-memory streams, following the neighbouring relay fixture: the point of
	/// this harness is that the beat is serialised and framed by the SDK's own transport, which an in-memory
	/// hand-off would skip.
	/// </para>
	/// </remarks>
	private sealed class ScriptedWorkerChild : IDisposable {

		private readonly Func<IReadOnlyDictionary<string, string>, int> _beatPlan;
		private readonly AnonymousPipeServerStream _parentToChildReader;
		private readonly AnonymousPipeClientStream _parentToChildWriter;
		private readonly AnonymousPipeServerStream _childToParentWriter;
		private readonly AnonymousPipeClientStream _childToParentReader;
		private readonly IWorkerLease _lease;
		private int _callCount;

		internal ScriptedWorkerChild(Func<IReadOnlyDictionary<string, string>, int> beatPlan) {
			_beatPlan = beatPlan;
			_parentToChildReader = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
			_parentToChildWriter =
				new AnonymousPipeClientStream(PipeDirection.Out, _parentToChildReader.GetClientHandleAsString());
			_childToParentWriter = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
			_childToParentReader =
				new AnonymousPipeClientStream(PipeDirection.In, _childToParentWriter.GetClientHandleAsString());
			_lease = Substitute.For<IWorkerLease>();
			_lease.ProcessId.Returns(31415);
			_lease.StandardInput.Returns(_parentToChildWriter);
			_lease.StandardOutput.Returns(_childToParentReader);
			// Stream.Null so the dispatcher's standard-error drain reaches end of stream at once: a stream that
			// never completed would cost every test in this fixture the drain's 250 ms stop bound.
			_lease.StandardError.Returns(Stream.Null);
			_lease.BudgetExpiresAtUtc.Returns(_ => DateTimeOffset.UtcNow.AddSeconds(30));
		}

		/// <summary>Gets the lease the substituted supervisor hands to the dispatcher.</summary>
		internal IWorkerLease Lease => _lease;

		/// <summary>
		/// Gets the beat cadence the child found in its own environment, or <see langword="null"/> when it
		/// was launched without one. This is the value <c>McpProgressHeartbeat</c> resolves at type load.
		/// </summary>
		internal string ObservedHeartbeatInterval { get; private set; }

		/// <summary>Gets how many <c>tools/call</c> requests the child answered.</summary>
		internal int CallCount => Volatile.Read(ref _callCount);

		/// <summary>Starts the child under <paramref name="childEnvironment"/>.</summary>
		/// <param name="childEnvironment">The environment the supervisor would have launched it with.</param>
		internal void Start(IReadOnlyDictionary<string, string> childEnvironment) {
			ObservedHeartbeatInterval =
				childEnvironment.GetValueOrDefault(McpProgressHeartbeat.IntervalOverrideEnvVar);
			int beats = _beatPlan(childEnvironment);
			_ = Task.Run(() => RunAsync(beats), CancellationToken.None);
		}

		/// <inheritdoc/>
		public void Dispose() {
			_parentToChildWriter.Dispose();
			_parentToChildReader.Dispose();
			_childToParentReader.Dispose();
			_childToParentWriter.Dispose();
		}

		private async Task RunAsync(int beats) {
			try {
				using StreamReader fromParent = new(_parentToChildReader);
				await using StreamWriter toParent = new(_childToParentWriter) { AutoFlush = true, NewLine = "\n" };
				string line;
				while ((line = await fromParent.ReadLineAsync().ConfigureAwait(false)) is not null) {
					await AnswerAsync(toParent, line, beats).ConfigureAwait(false);
				}
			}
			catch (Exception) {
				// The parent closing its end IS how a worker's stdin ends; a torn-down pipe is not a failure.
			}
		}

		private async Task AnswerAsync(StreamWriter toParent, string line, int beats) {
			JsonNode request = JsonNode.Parse(line);
			string method = request?["method"]?.GetValue<string>();
			if (method == "initialize") {
				await WriteAsync(toParent, new JsonObject {
					["jsonrpc"] = "2.0",
					["id"] = request["id"]?.DeepClone(),
					["result"] = new JsonObject {
						["protocolVersion"] = WorkerRelayOptions.MeasuredProtocolVersion,
						["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
						["serverInfo"] = new JsonObject { ["name"] = "scripted-worker", ["version"] = "1" }
					}
				}).ConfigureAwait(false);
				return;
			}
			if (method != "tools/call") {
				return;
			}
			Interlocked.Increment(ref _callCount);
			JsonNode progressToken = request["params"]?["_meta"]?["progressToken"];
			for (int beat = 1; beat <= beats && progressToken is not null; beat++) {
				// The caller's own token, cloned rather than rebuilt — a re-issued token is dropped silently by
				// a client that correlates ordinally, which is the failure this whole harness has to be able
				// to observe rather than repair.
				await WriteAsync(toParent, new JsonObject {
					["jsonrpc"] = "2.0",
					["method"] = NotificationMethods.ProgressNotification,
					["params"] = new JsonObject {
						["progressToken"] = progressToken.DeepClone(),
						["progress"] = beat,
						["message"] = string.Create(CultureInfo.InvariantCulture,
							$"{CohortToolName} is still running… (beat {beat})")
					}
				}).ConfigureAwait(false);
			}
			await WriteAsync(toParent, new JsonObject {
				["jsonrpc"] = "2.0",
				["id"] = request["id"]?.DeepClone(),
				["result"] = new JsonObject {
					["content"] = new JsonArray(new JsonObject {
						["type"] = "text",
						["text"] = "{\"success\":true}"
					}),
					["isError"] = false
				}
			}).ConfigureAwait(false);
		}

		private static Task WriteAsync(StreamWriter toParent, JsonObject message) =>
			toParent.WriteLineAsync(message.ToJsonString());
	}
}
