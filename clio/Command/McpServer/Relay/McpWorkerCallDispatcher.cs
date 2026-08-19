using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.UserEnvironment;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Relay;

/// <inheritdoc cref="IMcpWorkerCallDispatcher"/>
/// <remarks>
/// The <c>terminal-stage</c> half of this class — the deploy/uninstall family bounded by the worker's own
/// <c>run-completed</c> stage event rather than by a stopwatch (ADR §3.3) — lives in
/// <c>McpWorkerCallDispatcher.TerminalStage.cs</c>.
/// </remarks>
public sealed partial class McpWorkerCallDispatcher : IMcpWorkerCallDispatcher {

	/// <summary>
	/// Environment variable overriding <see cref="DefaultBudget"/>, in seconds (invariant culture,
	/// accepted range 0 &lt; n ≤ 3600).
	/// </summary>
	/// <remarks>
	/// Deliberately SEPARATE from <c>CLIO_MCP_READ_DEADLINE_SECONDS</c>. That variable bounds an
	/// in-process read by abandoning it, is scheduled for deletion at Stage 10, and is explicitly NOT
	/// inherited by an ordinary worker (ADR rule 11) — deriving the parent's kill budget from a contract
	/// that is being removed would tie the new mechanism's lifetime to the old one's.
	/// </remarks>
	internal const string BudgetOverrideEnvVar = "CLIO_MCP_WORKER_BUDGET_SECONDS";

	/// <summary>Machine-readable error class emitted when the parent kills a worker at its budget.</summary>
	/// <remarks>
	/// The SAME wire token the in-process read deadline uses, on purpose: from a client's point of view
	/// "clio bounded this read rather than blocking" is one situation with one correct response, and every
	/// piece of shipped agent guidance keyed on <c>error-class=creatio-timeout</c> keeps applying unchanged
	/// as tools move into workers. The envelope distinguishes itself with
	/// <c>worker-budget-expired: true</c>.
	/// </remarks>
	internal const string BudgetExpiredErrorClass = "creatio-timeout";

	/// <summary>
	/// Error class for a call refused because every worker slot on this host is in use.
	/// </summary>
	internal const string WorkerSaturationErrorClass = "clio-worker-saturated";

	/// <summary>Machine-readable error class emitted when the relay itself failed.</summary>
	/// <remarks>
	/// NOT the timeout class. A worker that crashed, closed its pipe, or answered a malformed result is a
	/// clio defect, and telling an agent to "wait and retry" would hide it behind a retry loop.
	/// </remarks>
	internal const string RelayFailureErrorClass = "clio-worker-relay-failure";

	/// <summary>
	/// Default wall-clock budget for one worker call, matching the in-process read deadline's 120 s so
	/// moving a tool into a worker does not silently change how long a client waits.
	/// </summary>
	/// <remarks>
	/// It must stay generous enough for the SLOW platform rather than tuned to the fast one: child spawn
	/// plus <c>initialize</c> measured p50 2.763 s on Windows Server 2022 against 0.65 s on macOS
	/// (ADR §2.4), and that is before the call itself, before queueing behind the concurrency cap, and
	/// against a development-shape `dotnet clio.dll` launch.
	/// </remarks>
	internal static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(120);

	/// <summary>How much of a failed worker's standard error is kept for the error envelope.</summary>
	/// <remarks>
	/// The single source of this number. It is surfaced on the envelope as
	/// <c>worker-stderr-tail-chars</c> and named in the human-readable text whenever the tail was cut, so a
	/// reader can tell a partial diagnosis from a whole one — nothing else, test assertions included, may
	/// restate the literal.
	/// </remarks>
	internal const int StandardErrorTailLimit = 2000;

	/// <summary>
	/// Stands in for the worker's standard error when the bound cut so late that not one COMPLETE line
	/// survived it, so the tail that remains cannot be surfaced.
	/// </summary>
	/// <remarks>
	/// A trimmed tail begins at an arbitrary offset and its first, partial line is dropped before anything
	/// is surfaced — see <c>WorkerStandardErrorDrain.WithoutOrphanedFirstLine</c> for why that is a
	/// security rule rather than tidiness. When that partial line was the WHOLE tail (a worker emitting one
	/// unbroken line), the alternative to this notice is an empty string, which would take
	/// <c>worker-stderr</c>, <c>worker-stderr-truncated</c> and the caveat sentence off the envelope
	/// together and read as "the worker said nothing" instead of "clio withheld what it kept".
	/// </remarks>
	internal const string StandardErrorNoCompleteLineNotice =
		"[clio withheld the worker's standard-error tail: the bound cut mid-line and no complete line "
		+ "survived it, so nothing here could be shown without risking an unredacted fragment]";

	private readonly IWorkerProcessSupervisor _supervisor;
	private readonly IWorkerChildTransportOwner _transportOwner;
	private readonly IWorkerMcpRelay _relay;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IStickyWorkerRegistry _stickyWorkers;
	private readonly IStickyWorkerPoll _stickyPoll;
	private readonly ISharedResourceReservation _reservations;
	private readonly Tools.IToolCommandResolver _commandResolver;
	private readonly ILogger _logger;
	private readonly TimeSpan _budget;
	private readonly TimeSpan _stageEventSilenceBound;
	private readonly TimeSpan _postTerminalExitGrace;
	private readonly TimeSpan _stickyCallBudget;
	private readonly TimeSpan _stickyCompletionLinger;

	/// <summary>
	/// Initializes a new instance of the <see cref="McpWorkerCallDispatcher"/> class.
	/// </summary>
	/// <param name="supervisor">Owns process creation, containment, the concurrency cap and the kill.</param>
	/// <param name="transportOwner">Attaches an MCP transport to the worker's redirected streams.</param>
	/// <param name="relay">Opens the parent leg of the worker's MCP session.</param>
	/// <param name="settingsRepository">Source of the feature generation frozen into the worker.</param>
	/// <param name="logger">Host logger; worker diagnostics go here, never to standard output.</param>
	/// <exception cref="ArgumentNullException">A dependency is missing.</exception>
	public McpWorkerCallDispatcher(
		IWorkerProcessSupervisor supervisor,
		IWorkerChildTransportOwner transportOwner,
		IWorkerMcpRelay relay,
		ISettingsRepository settingsRepository,
		IStickyWorkerRegistry stickyWorkers,
		IStickyWorkerPoll stickyPoll,
		ISharedResourceReservation reservations,
		Tools.IToolCommandResolver commandResolver,
		ILogger logger)
		: this(supervisor, transportOwner, relay, settingsRepository, stickyWorkers, stickyPoll,
			reservations, commandResolver, logger,
			ResolveBudget(System.Environment.GetEnvironmentVariable(BudgetOverrideEnvVar)),
			ResolveStageEventSilenceBound(
				System.Environment.GetEnvironmentVariable(StageEventSilenceOverrideEnvVar))) {
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="McpWorkerCallDispatcher"/> class with an explicit
	/// budget, so a test can bound a call without mutating process-wide environment state.
	/// </summary>
	/// <param name="supervisor">Owns process creation, containment, the concurrency cap and the kill.</param>
	/// <param name="transportOwner">Attaches an MCP transport to the worker's redirected streams.</param>
	/// <param name="relay">Opens the parent leg of the worker's MCP session.</param>
	/// <param name="settingsRepository">Source of the feature generation frozen into the worker.</param>
	/// <param name="logger">Host logger.</param>
	/// <param name="budget">Wall-clock budget measured from SPAWN.</param>
	/// <param name="stageEventSilenceBound">
	/// How long a <c>terminal-stage</c> call tolerates NO stage event of any kind before it declares the
	/// child lost. Defaults to <see cref="DefaultStageEventSilenceBound"/>. It is not an operation timer:
	/// every stage event restarts it, so a healthy deploy that streams may run for as long as it likes.
	/// </param>
	/// <param name="postTerminalExitGrace">
	/// How long a <c>terminal-stage</c> call waits for the worker AFTER its <c>run-completed</c> event
	/// before killing it and answering with the terminal outcome. Defaults to
	/// <see cref="DefaultPostTerminalExitGrace"/>.
	/// </param>
	/// <param name="stickyCallBudget">
	/// Wall-clock bound on ONE call to a sticky worker; defaults to <see cref="DefaultStickyCallBudget"/>.
	/// </param>
	/// <param name="stickyCompletionLinger">
	/// How long a sticky worker stays reachable after reporting completion; defaults to
	/// <see cref="DefaultStickyCompletionLinger"/>. Stated by a test so the reap is observable without
	/// waiting out a window measured in minutes.
	/// </param>
	/// <exception cref="ArgumentNullException">A dependency is missing.</exception>
	internal McpWorkerCallDispatcher(
		IWorkerProcessSupervisor supervisor,
		IWorkerChildTransportOwner transportOwner,
		IWorkerMcpRelay relay,
		ISettingsRepository settingsRepository,
		IStickyWorkerRegistry stickyWorkers,
		IStickyWorkerPoll stickyPoll,
		ISharedResourceReservation reservations,
		Tools.IToolCommandResolver commandResolver,
		ILogger logger,
		TimeSpan budget,
		TimeSpan? stageEventSilenceBound = null,
		TimeSpan? postTerminalExitGrace = null,
		TimeSpan? stickyCallBudget = null,
		TimeSpan? stickyCompletionLinger = null) {
		ArgumentNullException.ThrowIfNull(supervisor);
		ArgumentNullException.ThrowIfNull(transportOwner);
		ArgumentNullException.ThrowIfNull(relay);
		ArgumentNullException.ThrowIfNull(settingsRepository);
		ArgumentNullException.ThrowIfNull(stickyWorkers);
		ArgumentNullException.ThrowIfNull(stickyPoll);
		ArgumentNullException.ThrowIfNull(reservations);
		ArgumentNullException.ThrowIfNull(commandResolver);
		ArgumentNullException.ThrowIfNull(logger);
		_supervisor = supervisor;
		_transportOwner = transportOwner;
		_relay = relay;
		_settingsRepository = settingsRepository;
		_stickyWorkers = stickyWorkers;
		_stickyPoll = stickyPoll;
		_reservations = reservations;
		_commandResolver = commandResolver;
		_stickyCallBudget = stickyCallBudget ?? DefaultStickyCallBudget;
		_stickyCompletionLinger = stickyCompletionLinger ?? DefaultStickyCompletionLinger;
		_logger = logger;
		_budget = budget;
		_stageEventSilenceBound = stageEventSilenceBound ?? DefaultStageEventSilenceBound;
		_postTerminalExitGrace = postTerminalExitGrace ?? DefaultPostTerminalExitGrace;
	}

	/// <summary>
	/// Parses a raw seconds override into a budget, falling back to <see cref="DefaultBudget"/> for
	/// null / empty / non-numeric / out-of-range values. Pure, so the parse rules are testable without
	/// touching the environment.
	/// </summary>
	/// <param name="rawValue">The raw override value.</param>
	/// <returns>The resolved budget.</returns>
	internal static TimeSpan ResolveBudget(string rawValue) {
		if (!string.IsNullOrWhiteSpace(rawValue)
			&& double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
			&& seconds > 0 && seconds <= 3600) {
			return TimeSpan.FromSeconds(seconds);
		}
		return DefaultBudget;
	}

	/// <inheritdoc/>
	public async ValueTask<CallToolResult> DispatchAsync(
		McpExecutionRoute route,
		CallToolRequestParams parameters,
		IParentMcpSession parentSession,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(route);
		ArgumentNullException.ThrowIfNull(parameters);
		ArgumentNullException.ThrowIfNull(parentSession);
		if (route.Disposition != McpExecutionDisposition.Worker) {
			throw new InvalidOperationException(
				$"McpWorkerCallDispatcher was handed a '{route.Disposition}' route for "
				+ $"'{route.RoutingKey}'. It executes a routing decision and never re-makes one, so an "
				+ "in-process route reaching here is a dispatch-site defect.");
		}
		string toolName = route.RoutingKey ?? parameters.Name;

		// The deploy family is bounded by its own authoritative terminal stage, never by the generic kill
		// below: killing a deploy at a stopwatch can leave a half-installed environment, which is the one
		// place where terminating the process is the wrong tool (ADR rule 4 / §3.3). The branch reads the
		// DECLARED policy rather than a name list, so the two are one decision — and it is deliberately
		// NOT fail-open to terminal-stage for an unclassified route: McpExecutionRouterTests pins that the
		// shipped router hands both cohort members their metadata, which is where that could regress.
		if (route.Metadata is { BudgetPolicy: McpToolBudgetPolicy.TerminalStage }) {
			return await DispatchTerminalStageAsync(toolName, parameters, parentSession, cancellationToken)
				.ConfigureAwait(false);
		}

		// The long-running families are supervised by a worker that OUTLIVES this response, so a later
		// status poll of the same family reaches the process holding the operation instead of an empty
		// registry in a different one. The branch reads the DECLARED metadata, and it requires BOTH a
		// sticky lifetime AND a named family: a sticky lifetime with no family would be a worker nothing
		// could ever reach again, which is a leak rather than a feature.
		if (route.Metadata is { Lifetime: McpToolExecutionLifetime.Sticky } stickyMetadata
			&& stickyMetadata.OperationFamily != McpToolOperationFamily.None) {
			return await DispatchStickyAsync(toolName, stickyMetadata, parameters, parentSession,
				cancellationToken).ConfigureAwait(false);
		}

		return await DispatchPerCallAsync(toolName, parameters, parentSession, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>
	/// Runs one ordinary per-call worker: spawn, relay, answer, kill.
	/// </summary>
	/// <param name="toolName">The canonical tool name, for diagnostics and the error envelope.</param>
	/// <param name="parameters">The caller's params.</param>
	/// <param name="parentSession">The parent leg the child's traffic is relayed to.</param>
	/// <param name="cancellationToken">Caller cancellation.</param>
	/// <returns>The worker's result, or a named error envelope.</returns>
	/// <remarks>
	/// Also the fallback for a STATUS poll of a long-running family that found no live sticky worker —
	/// after a parent restart, say. It answers from an empty registry, which is exactly what the
	/// in-process host did in the same situation, rather than spawning a sticky worker whose only effect
	/// would be to take the target's configuration-build reservation away from the compile it was asked
	/// to report on.
	/// </remarks>
	private async ValueTask<CallToolResult> DispatchPerCallAsync(
		string toolName,
		CallToolRequestParams parameters,
		IParentMcpSession parentSession,
		CancellationToken cancellationToken) {
		// Composed BEFORE the spawn: the supervisor clears the inherited environment, so this delta plus its
		// own allowlist is everything the worker sees. An ordinary worker gets NO read-deadline override —
		// the parent bounds it by killing, and a second in-child deadline would abandon the work while
		// keeping the per-tenant monitor, which is the wedge this feature removes (ADR rule 11).
		IReadOnlyDictionary<string, string> childEnvironment =
			ComposeChildEnvironmentSafely(McpWorkerLifetime.PerCall);
		WorkerSpawnRequest spawnRequest = ComposeSpawnRequest(childEnvironment, _budget);

		IWorkerLease lease;
		try {
			// Waiting for a slot happens HERE and is not bounded by the budget — that is the whole point of
			// anchoring the budget on spawn. SpawnContainedAsync returns only once the process exists.
			lease = await _supervisor.SpawnContainedAsync(spawnRequest, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) {
			throw;
		}
		catch (WorkerQueueWaitExpiredException exception) {
			// SATURATION IS NOT A RELAY FAILURE, and collapsing it into one throws away the whole point of
			// the named exception. R-10 promises a refusal "carrying cap and queue depth… never an error
			// that reads as a backend timeout" — and "the worker process could not be started" reads as a
			// clio defect, sending an agent to hunt a bug in clio when the true answer is that the host is
			// busy and the call is worth retrying in a moment. The numbers are the actionable part: they
			// tell an operator whether to wait or to raise CLIO_MCP_WORKER_CONCURRENCY.
			_logger.WriteWarning(
				$"MCP worker for '{toolName}' was not started: {exception.Message}");
			return WorkerSaturationResult(toolName, exception);
		}
		catch (Exception exception) {
			_logger.WriteWarning(
				$"MCP worker for '{toolName}' could not be started: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
			return RelayFailureResult(toolName, "the worker process could not be started", exception.Message, null);
		}

		WorkerStandardErrorDrain standardError = new(lease.StandardError, StandardErrorTailLimit);
		standardError.Start();
		// One source covering the handshake AND the call: the budget bounds the whole round trip, so a
		// worker that never completes `initialize` is killed exactly like one that never answers the tool.
		using CancellationTokenSource budgetSource =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		TimeSpan remaining = lease.BudgetExpiresAtUtc - DateTimeOffset.UtcNow;
		budgetSource.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
		WorkerRelaySession session = null;
		try {
			ITransport childTransport = await _transportOwner
				.ConnectAsync(lease.StandardInput, lease.StandardOutput, budgetSource.Token)
				.ConfigureAwait(false);
			session = await _relay
				.OpenAsync(childTransport, parentSession, options: null, budgetSource.Token)
				.ConfigureAwait(false);
			CallToolResult result = await session
				.CallToolAsync(WithoutParentSessionMetadata(parameters), budgetSource.Token)
				.ConfigureAwait(false);
			if (result is null) {
				// OQ-10: a worker answering `{"result":null}` deserialises to a null CallToolResult, which
				// would otherwise reach the SDK as "the tool answered" and be serialised as an empty success.
				// A worker that answered nothing is a defect, and it is named rather than smoothed over.
				_logger.WriteWarning($"MCP worker for '{toolName}' returned a null tool result.");
				return RelayFailureResult(toolName, "the worker returned a null tool result",
					detail: null, standardError.Tail());
			}
			return result;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			// The CALLER gave up. Kill the worker so nothing is left running behind a client that stopped
			// waiting, then let cancellation propagate — it is not a timeout and must not be reported as one.
			KillQuietly(lease, toolName);
			throw;
		}
		catch (OperationCanceledException) {
			// The budget expired. Kill FIRST: closing the worker's pipes ends the relay's read loop
			// promptly, so the bounded teardown below costs milliseconds instead of its full grace window.
			KillQuietly(lease, toolName);
			_logger.WriteWarning(
				$"MCP worker for '{toolName}' (pid {lease.ProcessId}) exceeded its "
				+ $"{FormatSeconds(_budget)} s budget and was killed.");
			return BudgetExpiredResult(toolName, _budget, standardError.Tail());
		}
		catch (Exception exception) {
			KillQuietly(lease, toolName);
			_logger.WriteWarning(
				$"MCP worker relay for '{toolName}' failed: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
			return RelayFailureResult(toolName, "the worker relay failed", exception.Message, standardError.Tail());
		}
		finally {
			if (session is not null) {
				// Bounded by the session's own two grace windows; it never waits on the worker process.
				await session.DisposeAsync().ConfigureAwait(false);
			}
			await standardError.StopAsync().ConfigureAwait(false);
			// Disposing the lease kills the worker if it is still running, drops its stale-worker registry
			// entry and returns the concurrency slot. It is the ONLY thing that returns the slot, so it runs
			// on every path including the throwing ones.
			lease.Dispose();
		}
	}

	/// <summary>The clio verb a worker child runs. Its own <c>mcp-server</c>, in worker mode.</summary>
	private const string WorkerVerb = "mcp-server";

	/// <summary>
	/// Builds the spawn request for one per-call worker: the worker verb, the budget, the frozen
	/// environment delta, and the HOST's working directory.
	/// </summary>
	/// <param name="childEnvironment">The frozen environment delta handed to the worker.</param>
	/// <param name="budget">The wall-clock budget, measured from spawn.</param>
	/// <returns>The spawn request.</returns>
	/// <remarks>
	/// <para>
	/// <b>The working directory is a correctness input, not a cosmetic default.</b> Leaving it null does
	/// NOT give the child the parent's directory: <c>WorkerProcessSupervisor.BuildLaunchRequest</c> falls
	/// back to <see cref="ClioWorkerLaunchDescriptor.WorkingDirectory"/>, which
	/// <see cref="ClioExecutablePathProvider"/> resolves to the directory the clio ASSEMBLY lives in. A
	/// cohort <c>get-page</c> anchors <c>.clio-pages/{schema}/</c> on the process current directory
	/// (<see cref="PageFileWriter"/>), so a child started in the install tree writes the user's page files
	/// into clio's own installation, answers <c>success: true</c>, and reports nothing — observed live
	/// twice on Stage 6. Every path a tool resolves relative to "here" has the same shape, so the fix
	/// belongs at the spawn, not on one tool's anchor.
	/// </para>
	/// <para>
	/// <b>Read under <c>CwdLock</c>, released before the spawn.</b> Two MCP tools pin the process-wide
	/// directory and restore it (<c>CreateUiProjectTool</c>, <c>DownloadConfigurationTool</c>), both
	/// entirely inside that lock, so an unlocked read could hand a worker another tool's transient
	/// directory. The lock covers the READ only — holding it across a process spawn would serialise every
	/// worker behind it. Lock ordering is the documented one (per-tenant → <c>CwdLock</c>, never the
	/// reverse): nothing is acquired while it is held.
	/// </para>
	/// </remarks>
	internal static WorkerSpawnRequest ComposeSpawnRequest(
		IReadOnlyDictionary<string, string> childEnvironment, TimeSpan budget) {
		string hostWorkingDirectory;
		lock (Tools.McpToolExecutionLock.CwdLock) {
			hostWorkingDirectory = System.Environment.CurrentDirectory;
		}
		return new WorkerSpawnRequest {
			Arguments = [WorkerVerb, McpWorkerEnvironment.WorkerFlag],
			Budget = budget,
			WorkingDirectory = hostWorkingDirectory,
			EnvironmentVariables = childEnvironment
		};
	}

	/// <summary>
	/// The reserved <c>_meta</c> keys MCP 2026-07-28 clients attach to every request to describe the
	/// session they are speaking on. They belong to the PARENT's session with the real client, not to the
	/// call, and the relay's child leg is a different session.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Found by running it.</b> Forwarding the caller's params verbatim — which ADR rule 1 requires, and
	/// which is right for everything else — made every relayed call fail with <i>"The negotiated protocol
	/// version cannot change within a session. The session negotiated '2024-11-05', but a request specified
	/// '2026-07-28'."</i> The SDK's own server-side check is correct: the parent's client negotiated the
	/// newer revision and stamps it on each request, the relay negotiates the revision its measured
	/// properties were proven on, and a request carrying the OTHER session's version is genuinely
	/// contradictory.
	/// </para>
	/// <para>
	/// <b>Only session-describing keys are removed, and only when present.</b> Everything the contract
	/// depends on rides through untouched — <c>progressToken</c> (ClioRing correlates on it ordinally and
	/// fails silently on a mismatch), <c>clioStageEvent</c>, and any key neither leg knows about. The params
	/// object is copied ONLY when one of these keys is actually there, so the ordinary call still hands the
	/// child the caller's own object.
	/// </para>
	/// </remarks>
	private static readonly string[] ParentSessionMetadataKeys = [
		"io.modelcontextprotocol/protocolVersion",
		"io.modelcontextprotocol/clientInfo",
		"io.modelcontextprotocol/clientCapabilities",
		"io.modelcontextprotocol/sessionId"
	];

	/// <summary>
	/// Returns the params to send to the worker, with the parent session's own <c>_meta</c> descriptors
	/// removed. See <see cref="ParentSessionMetadataKeys"/> for why they cannot travel.
	/// </summary>
	/// <param name="parameters">The caller's params.</param>
	/// <returns>The caller's own object when nothing had to change; otherwise a copy.</returns>
	internal static CallToolRequestParams WithoutParentSessionMetadata(CallToolRequestParams parameters) {
		JsonObject meta = parameters?.Meta;
		if (meta is null) {
			return parameters;
		}
		bool carriesSessionMetadata = false;
		foreach (string key in ParentSessionMetadataKeys) {
			if (meta.ContainsKey(key)) {
				carriesSessionMetadata = true;
				break;
			}
		}
		if (!carriesSessionMetadata) {
			return parameters;
		}
		JsonObject relayMeta = meta.DeepClone().AsObject();
		foreach (string key in ParentSessionMetadataKeys) {
			relayMeta.Remove(key);
		}
		// Every settable property of CallToolRequestParams and its RequestParams base is carried across.
		// Three of the four are inherited and easy to forget, and a copy that rebuilds only Name/Arguments
		// /Meta would drop the retry payload (InputResponses, RequestState) on exactly the calls that
		// carry session metadata — that is, on every real client call. ProgressToken is deliberately
		// absent: it is read-only and derived from `_meta.progressToken`, which rides through untouched.
		return new CallToolRequestParams {
			Name = parameters.Name,
			Arguments = parameters.Arguments,
			InputResponses = parameters.InputResponses,
			RequestState = parameters.RequestState,
			Meta = relayMeta
		};
	}

	// Composes the child environment so that NOTHING on this line can fail a spawn.
	//
	// All three dispatch paths build the child environment before entering their try, which makes any
	// throw here cohort-fatal: not one tool, every worker-routed call, until somebody edits
	// appsettings.json. That already happened once — Format threw on a feature key containing its own
	// separator, and `clio experimental --name "a;b=c"` is enough to put one on disk. The encoding was
	// fixed, but the SHAPE is the defect: a helper on this line is one refactor away from throwing again.
	//
	// The fallback re-composes with an EMPTY feature map rather than returning nothing, because the
	// environment carries more than features (the read-deadline policy above depends on lifetime), and a
	// worker started with every gated feature off is the same fail-closed answer ReadFrozenFeatures gives
	// for an unreadable settings file. If the empty-map composition throws too, the failure is in the
	// composer and not in the data, and it is allowed through.
	//
	// Deliberately untested: after the encoding fix no reachable input makes the first call throw, so a
	// test could only assert this by mocking a static. It is insurance against a future edit, and it is
	// recorded as such rather than dressed up as covered behaviour.
	private IReadOnlyDictionary<string, string> ComposeChildEnvironmentSafely(McpWorkerLifetime lifetime) {
		IReadOnlyDictionary<string, bool> frozenFeatures = ReadFrozenFeatures();
		try {
			return McpWorkerEnvironment.ComposeChildEnvironment(frozenFeatures, lifetime);
		}
		catch (Exception exception) {
			_logger.WriteWarning(
				"MCP worker feature generation could not be carried to the worker; it starts with every "
				+ $"gated feature off: {SensitiveErrorTextRedactor.Redact(exception.Message)}");
			return McpWorkerEnvironment.ComposeChildEnvironment(
				new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase), lifetime);
		}
	}

	private IReadOnlyDictionary<string, bool> ReadFrozenFeatures() {
		try {
			return _settingsRepository.GetFeatures();
		}
		catch (Exception exception) {
			// A worker with an empty feature map has every gated feature OFF, which is the same fail-closed
			// answer an absent payload produces. Failing the CALL because appsettings.json could not be read
			// for its OPTIONAL feature section would be a worse trade.
			_logger.WriteWarning(
				"MCP worker feature generation could not be read; the worker starts with every gated feature "
				+ $"off: {SensitiveErrorTextRedactor.Redact(exception.Message)}");
			return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		}
	}

	private void KillQuietly(IWorkerLease lease, string toolName) {
		try {
			_supervisor.KillContained(lease);
		}
		catch (Exception exception) {
			// Reported, never rethrown: the caller is already receiving an answer, and the lease dispose in
			// the finally block is a second chance at the same kill.
			_logger.WriteWarning(
				$"MCP worker for '{toolName}' (pid {lease.ProcessId}) could not be killed: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
		}
	}

	private static string FormatSeconds(TimeSpan value) =>
		Math.Max(1, (int)Math.Round(value.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

	/// <summary>
	/// Builds the structured result returned when the parent killed a worker at its budget.
	/// </summary>
	/// <param name="toolName">Canonical tool name.</param>
	/// <param name="budget">The elapsed budget.</param>
	/// <param name="standardErrorTail">
	/// Bounded tail of the worker's standard error, or <see langword="null"/> when there is none.
	/// </param>
	/// <returns>The structured result.</returns>
	internal static CallToolResult BudgetExpiredResult(
		string toolName, TimeSpan budget, WorkerStandardErrorTail standardErrorTail) {
		string seconds = FormatSeconds(budget);
		string text = WithStandardErrorBoundNote(
			$"MCP tool '{toolName}' did not answer within its {seconds}s worker budget "
			+ $"(error-class={BudgetExpiredErrorClass}), so clio terminated the worker process that was "
			+ "running it. Nothing else in this environment was affected and the call is safe to retry.",
			standardErrorTail);
		JsonObject payload = new() {
			["success"] = false,
			["error-class"] = BudgetExpiredErrorClass,
			["worker-budget-expired"] = true,
			["tool"] = toolName,
			["budget-seconds"] = int.Parse(seconds, CultureInfo.InvariantCulture),
			["error"] = text,
			["retry-guidance"] =
				"The Creatio stand did not answer in time. Unlike the in-process deadline this replaces, the "
				+ "work was terminated rather than abandoned, so no later call for this environment is "
				+ "affected: retry the same call, or narrow it (smaller limit, tighter filter) and retry. "
				+ $"Raise the budget with the {BudgetOverrideEnvVar} environment variable if the stand is "
				+ "legitimately slow."
		};
		AttachWorkerDiagnostics(payload, standardErrorTail);
		return new CallToolResult {
			IsError = true,
			Content = [new TextContentBlock { Text = text }],
			StructuredContent = JsonSerializer.SerializeToElement(payload)
		};
	}

	/// <summary>
	/// Builds the structured result returned when the worker path itself failed — a crash, a closed pipe,
	/// a malformed handshake or a null answer.
	/// </summary>
	/// <param name="toolName">Canonical tool name.</param>
	/// <param name="reason">Short description of what failed.</param>
	/// <param name="detail">Underlying exception message, redacted before it is surfaced.</param>
	/// <param name="standardErrorTail">
	/// Bounded tail of the worker's standard error, or <see langword="null"/> when there is none.
	/// </param>
	/// <returns>The structured result.</returns>
	internal static CallToolResult RelayFailureResult(
		string toolName, string reason, string detail, WorkerStandardErrorTail standardErrorTail) {
		string redactedDetail = string.IsNullOrWhiteSpace(detail)
			? null
			: SensitiveErrorTextRedactor.Redact(detail);
		string text = WithStandardErrorBoundNote(
			$"MCP tool '{toolName}' was not executed: {reason} "
			+ $"(error-class={RelayFailureErrorClass})."
			+ (redactedDetail is null ? string.Empty : $" {redactedDetail}"),
			standardErrorTail);
		JsonObject payload = new() {
			["success"] = false,
			["error-class"] = RelayFailureErrorClass,
			["tool"] = toolName,
			["error"] = text,
			["retry-guidance"] =
				"This is a clio worker-process failure, not a slow Creatio stand. Retrying is unlikely to "
				+ "help until the cause is known; report the worker diagnostics below."
		};
		AttachWorkerDiagnostics(payload, standardErrorTail);
		return new CallToolResult {
			IsError = true,
			Content = [new TextContentBlock { Text = text }],
			StructuredContent = JsonSerializer.SerializeToElement(payload)
		};
	}

	/// <summary>
	/// Appends the truncation caveat to a human-readable failure sentence when the worker wrote more
	/// standard error than <see cref="StandardErrorTailLimit"/> keeps.
	/// </summary>
	/// <param name="text">The failure sentence.</param>
	/// <param name="standardErrorTail">The bounded tail, or <see langword="null"/> when there is none.</param>
	/// <returns>The sentence, with the caveat when one is owed.</returns>
	/// <remarks>
	/// The structured marker alone is not enough. A person reads the sentence — it is what lands in a chat
	/// transcript and in a bug report — and a reader handed the END of a 40 KB stack trace with nothing
	/// saying so sees text starting mid-frame, cannot tell that the exception line is missing, and
	/// investigates whatever the surviving frames happen to name.
	/// </remarks>
	private static string WithStandardErrorBoundNote(string text, WorkerStandardErrorTail standardErrorTail) {
		if (!CarriesStandardError(standardErrorTail) || !standardErrorTail.Truncated) {
			return text;
		}
		return text
			+ " The worker wrote more to standard error than clio keeps, so worker-stderr below holds only"
			+ $" the COMPLETE LINES within its last {StandardErrorTailLimit} characters: it starts mid-stream,"
			+ " and the first line of the failure — usually the one that names the cause — is not in it.";
	}

	private static void AttachWorkerDiagnostics(JsonObject payload, WorkerStandardErrorTail standardErrorTail) {
		if (!CarriesStandardError(standardErrorTail)) {
			return;
		}
		payload["worker-stderr"] = SensitiveErrorTextRedactor.Redact(standardErrorTail.Text);
		if (!standardErrorTail.Truncated) {
			// Deliberately absent rather than false: an agent has to be able to read "no marker" as "this is
			// the worker's whole diagnosis", and a field that is always there says nothing by being there.
			return;
		}
		payload["worker-stderr-truncated"] = true;
		payload["worker-stderr-tail-chars"] = StandardErrorTailLimit;
	}

	private static bool CarriesStandardError(WorkerStandardErrorTail standardErrorTail) =>
		standardErrorTail is not null && !string.IsNullOrWhiteSpace(standardErrorTail.Text);

	/// <summary>
	/// One bounded snapshot of a worker's standard error: the text that was kept, and whether keeping it
	/// meant dropping anything.
	/// </summary>
	/// <param name="Text">
	/// The kept text — at most the LAST <see cref="StandardErrorTailLimit"/> characters, and when that
	/// bound actually cut, only the COMPLETE lines within them (or
	/// <see cref="StandardErrorNoCompleteLineNotice"/> when there were none).
	/// </param>
	/// <param name="Truncated">
	/// <see langword="true"/> when the worker wrote more than the bound, so <paramref name="Text"/> starts
	/// mid-stream and the first line of the failure is not in it.
	/// </param>
	/// <remarks>
	/// The two values travel together, taken under ONE lock, because they are only meaningful together: a
	/// caller that read the text and then asked a separate "was it trimmed?" member could be answered
	/// about a later state of the buffer than the one it is describing.
	/// </remarks>
	public sealed record WorkerStandardErrorTail(string Text, bool Truncated);

	/// <summary>
	/// Builds the envelope for a call that waited out the queue bound because the host was saturated.
	/// </summary>
	/// <param name="toolName">The refused tool.</param>
	/// <param name="exception">The refusal, carrying the wait endured, the bound, the cap and the depth.</param>
	/// <returns>The error result.</returns>
	internal static CallToolResult WorkerSaturationResult(string toolName,
		WorkerQueueWaitExpiredException exception) {
		string text = $"'{toolName}' was not started. {exception.Message}";
		JsonObject payload = new() {
			["success"] = false,
			["tool"] = toolName,
			["error-class"] = WorkerSaturationErrorClass,
			["worker-concurrency"] = exception.ConcurrencyCap,
			["queue-depth"] = exception.QueueDepth,
			["waited-seconds"] = Math.Round(exception.WaitEndured.TotalSeconds, 3),
			["queue-wait-bound-seconds"] = Math.Round(exception.ConfiguredBound.TotalSeconds, 3),
			// Unlike an indeterminate deploy, this one IS safe to retry: nothing was spawned and no request
			// reached Creatio, so the call had no effect at all.
			["retry-guidance"] = "The host is busy, not broken: nothing was spawned and no request reached "
				+ "Creatio. Retry shortly, or raise CLIO_MCP_WORKER_CONCURRENCY if this host should run more "
				+ "workers at once.",
			["message"] = text
		};
		return new CallToolResult {
			IsError = true,
			Content = [new TextContentBlock { Text = text }],
			StructuredContent = JsonSerializer.SerializeToElement(payload)
		};
	}
}
