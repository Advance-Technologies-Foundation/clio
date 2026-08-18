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
public sealed class McpWorkerCallDispatcher : IMcpWorkerCallDispatcher {

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
	private const int StandardErrorTailLimit = 2000;

	private readonly IWorkerProcessSupervisor _supervisor;
	private readonly IWorkerChildTransportOwner _transportOwner;
	private readonly IWorkerMcpRelay _relay;
	private readonly ISettingsRepository _settingsRepository;
	private readonly ILogger _logger;
	private readonly TimeSpan _budget;

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
		ILogger logger)
		: this(supervisor, transportOwner, relay, settingsRepository, logger,
			ResolveBudget(System.Environment.GetEnvironmentVariable(BudgetOverrideEnvVar))) {
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
	/// <exception cref="ArgumentNullException">A dependency is missing.</exception>
	internal McpWorkerCallDispatcher(
		IWorkerProcessSupervisor supervisor,
		IWorkerChildTransportOwner transportOwner,
		IWorkerMcpRelay relay,
		ISettingsRepository settingsRepository,
		ILogger logger,
		TimeSpan budget) {
		ArgumentNullException.ThrowIfNull(supervisor);
		ArgumentNullException.ThrowIfNull(transportOwner);
		ArgumentNullException.ThrowIfNull(relay);
		ArgumentNullException.ThrowIfNull(settingsRepository);
		ArgumentNullException.ThrowIfNull(logger);
		_supervisor = supervisor;
		_transportOwner = transportOwner;
		_relay = relay;
		_settingsRepository = settingsRepository;
		_logger = logger;
		_budget = budget;
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

		// Composed BEFORE the spawn: the supervisor clears the inherited environment, so this delta plus its
		// own allowlist is everything the worker sees. An ordinary worker gets NO read-deadline override —
		// the parent bounds it by killing, and a second in-child deadline would abandon the work while
		// keeping the per-tenant monitor, which is the wedge this feature removes (ADR rule 11).
		IReadOnlyDictionary<string, string> childEnvironment = McpWorkerEnvironment.ComposeChildEnvironment(
			ReadFrozenFeatures(), McpWorkerLifetime.PerCall);
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
				// OQ-9: a worker answering `{"result":null}` deserialises to a null CallToolResult, which
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
	/// <param name="standardErrorTail">Tail of the worker's standard error, or <see langword="null"/>.</param>
	/// <returns>The structured result.</returns>
	internal static CallToolResult BudgetExpiredResult(string toolName, TimeSpan budget, string standardErrorTail) {
		string seconds = FormatSeconds(budget);
		string text =
			$"MCP tool '{toolName}' did not answer within its {seconds}s worker budget "
			+ $"(error-class={BudgetExpiredErrorClass}), so clio terminated the worker process that was "
			+ "running it. Nothing else in this environment was affected and the call is safe to retry.";
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
	/// <param name="standardErrorTail">Tail of the worker's standard error, or <see langword="null"/>.</param>
	/// <returns>The structured result.</returns>
	internal static CallToolResult RelayFailureResult(
		string toolName, string reason, string detail, string standardErrorTail) {
		string redactedDetail = string.IsNullOrWhiteSpace(detail)
			? null
			: SensitiveErrorTextRedactor.Redact(detail);
		string text =
			$"MCP tool '{toolName}' was not executed: {reason} "
			+ $"(error-class={RelayFailureErrorClass})."
			+ (redactedDetail is null ? string.Empty : $" {redactedDetail}");
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

	private static void AttachWorkerDiagnostics(JsonObject payload, string standardErrorTail) {
		if (!string.IsNullOrWhiteSpace(standardErrorTail)) {
			payload["worker-stderr"] = SensitiveErrorTextRedactor.Redact(standardErrorTail);
		}
	}

	/// <summary>
	/// Continuously drains one worker's standard error, keeping a bounded tail for diagnostics.
	/// </summary>
	/// <remarks>
	/// <b>Draining is not diagnostics, it is liveness.</b> A worker that fills its standard-error pipe
	/// buffer BLOCKS on the write and goes silent, which the parent then observes as a call that never
	/// answers — a hang, attributed to the stand, caused by clio. The bounded tail is the useful
	/// by-product: without it a worker that fails at startup yields only "the worker closed its transport
	/// before answering".
	/// </remarks>
	private sealed class WorkerStandardErrorDrain {

		private readonly Stream _stream;
		private readonly int _limit;
		private readonly StringBuilder _tail = new();
		private readonly object _tailLock = new();
		private Task _pump;

		internal WorkerStandardErrorDrain(Stream stream, int limit) {
			_stream = stream;
			_limit = limit;
		}

		internal void Start() {
			if (_stream is null) {
				return;
			}
			_pump = Task.Run(async () => {
				try {
					using StreamReader reader = new(_stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
						bufferSize: 1024, leaveOpen: true);
					char[] buffer = new char[1024];
					int read;
					while ((read = await reader.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false)) > 0) {
						lock (_tailLock) {
							_tail.Append(buffer, 0, read);
							if (_tail.Length > _limit) {
								_tail.Remove(0, _tail.Length - _limit);
							}
						}
					}
				}
				catch (Exception) {
					// The pipe closing when the worker dies is the ordinary end of this loop, not a failure,
					// and a drain must never be able to fail the call it exists to keep alive.
				}
			});
		}

		internal string Tail() {
			lock (_tailLock) {
				return _tail.Length == 0 ? null : _tail.ToString().Trim();
			}
		}

		internal async Task StopAsync() {
			if (_pump is null) {
				return;
			}
			// Bounded: the worker's pipes are closed by the lease dispose that follows, which ends the read.
			// Waiting unbounded here would let a stuck pipe hold the response open, which is the failure
			// class this whole execution boundary removes.
			await Task.WhenAny(_pump, Task.Delay(TimeSpan.FromMilliseconds(250))).ConfigureAwait(false);
		}
	}
}
