using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// Raised when a worker's MCP session cannot answer: it closed its pipe, it answered a JSON-RPC error, or
/// its handshake was malformed.
/// </summary>
public sealed class WorkerRelayException : Exception {

	/// <summary>Initializes a new instance of the <see cref="WorkerRelayException"/> class.</summary>
	/// <param name="message">What went wrong.</param>
	public WorkerRelayException(string message)
		: base(message) {
	}

	/// <summary>Initializes a new instance of the <see cref="WorkerRelayException"/> class.</summary>
	/// <param name="message">What went wrong.</param>
	/// <param name="errorCode">The JSON-RPC error code the worker returned, when it returned one.</param>
	public WorkerRelayException(string message, int? errorCode)
		: base(message) => ErrorCode = errorCode;

	/// <summary>Initializes a new instance of the <see cref="WorkerRelayException"/> class.</summary>
	/// <param name="message">What went wrong.</param>
	/// <param name="innerException">The underlying failure.</param>
	public WorkerRelayException(string message, Exception innerException)
		: base(message, innerException) {
	}

	/// <summary>Gets the JSON-RPC error code the worker returned, or <c>null</c> when there was none.</summary>
	public int? ErrorCode { get; }
}

/// <inheritdoc cref="IWorkerMcpRelay"/>
/// <param name="logger">
/// The host logger, handed on to every session this relay opens. It is the only way the two teardown
/// events that DISCARD traffic — a notification left undelivered when the drain window expires, and a read
/// loop abandoned at the shutdown grace — reach an operator; see <see cref="WorkerRelaySession"/>.
/// </param>
public sealed class WorkerMcpRelay(ILogger logger) : IWorkerMcpRelay {

	private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

	/// <inheritdoc/>
	public async Task<WorkerRelaySession> OpenAsync(ITransport childTransport,
		IParentMcpSession parentSession, WorkerRelayOptions options, CancellationToken cancellationToken) {
		if (childTransport is null) {
			throw new ArgumentNullException(nameof(childTransport));
		}
		if (parentSession is null) {
			throw new ArgumentNullException(nameof(parentSession));
		}
		// Not a DI bypass: WorkerRelaySession is per-child runtime state over one live transport, so it is
		// created here rather than resolved. It deliberately carries no Clio-namespaced interface — the
		// assembly interface scan in BindingsModule would auto-register any class that does, and this one's
		// constructor takes a live transport that no container can supply, which would fail ValidateOnBuild
		// and stop clio from starting at all.
		WorkerRelaySession session =
			new(childTransport, parentSession, options ?? new WorkerRelayOptions(), _logger);
		// The read loop starts BEFORE the handshake on purpose: the initialize RESPONSE arrives through the
		// same single consumer, so a handshake-then-read order would deadlock on its own first request.
		session.StartReadLoop();
		try {
			await session.HandshakeAsync(cancellationToken).ConfigureAwait(false);
		}
		catch {
			await session.DisposeAsync().ConfigureAwait(false);
			throw;
		}
		return session;
	}
}

/// <summary>
/// One worker's live MCP session, seen from the parent: the single consumer of the child's transport, the
/// owner of request-id correlation on the child leg, and the relay of everything the child sends upward.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two guarantees that fail silently.</b> Notifications are forwarded from INSIDE the single read
/// loop and awaited one at a time, so the client observes the child's own order (ADR rule 12); and a
/// child <c>sampling/createMessage</c> is answered by the REAL client through
/// <see cref="IParentMcpSession.SampleAsync"/>, because a relay that refused it would silently degrade
/// <c>update-page</c> / <c>sync-pages</c> semantic review to <c>Skipped=true</c> with no error anywhere
/// (ADR rule 1).
/// </para>
/// <para>
/// <b>Why child→parent requests are answered off the loop</b> while notifications are not: sampling waits
/// on a human-scale round trip through the client, and doing that inline would stall every notification
/// queued behind it. Requests carry their own ids, so answering them out of order is correct; a
/// notification has no id, and its order IS its meaning.
/// </para>
/// <para>
/// <b>Teardown is TWO bounded waits, and which failure mode to prefer was decided rather than stumbled
/// into.</b> The two properties are in genuine tension. Forwarding under
/// <see cref="CancellationToken.None"/> lets a blocked client hold teardown open for the whole shutdown
/// grace (the wedge this boundary exists to remove); forwarding under a token cancelled at the FIRST line of
/// disposal makes an in-flight — or one-slot-behind, still unread — notification CANCELLED instead of
/// delivered, and the forward swallows that cancellation, so nothing anywhere records the loss. The event
/// most likely to be in that position is the authoritative terminal stage (ADR rule 4), so the silent
/// version is the worse of the two: a wedge is visible, a dropped terminal stage is not. Disposal therefore
/// closes the child transport FIRST — which completes the message channel, verified against the shipped
/// <c>StreamClientSessionTransport</c> — and gives the loop
/// <see cref="WorkerRelayOptions.NotificationDrainGrace"/> to finish forwarding what the worker already
/// emitted, INCLUDING what is still buffered, before the lifetime token is cancelled and the shutdown grace
/// abandons a loop that is still stuck. Both bounds report through <see cref="Clio.Common.ILogger"/>, so the
/// residual loss is announced rather than assumed away.
/// </para>
/// <para>
/// <b>What a cancelled call leaves behind, and the one rule that decides whether this session may be used
/// again.</b> The SDK's <c>StreamClientSessionTransport</c> serialises a COMPLETED send behind its own
/// <c>_sendLock</c>, not an atomic one: it takes the caller's token separately for the payload write, the
/// newline write and the flush (read off the shipped 2.2.0 IL — see
/// <see cref="IWorkerChildTransportOwner"/>). So a token that fires mid-send releases that lock with an
/// UNTERMINATED line on the child's stdin, and the next writer's JSON is appended to it. Hence:
/// <list type="bullet">
/// <item><description>a session whose send did NOT complete is RETIRED — never written to again, its
/// closure set, its process reclaimed by the supervisor's lease;</description></item>
/// <item><description>a session whose call was cancelled CLEANLY (the request was written) is reusable, but
/// only after a bounded <see cref="ProbeLivenessAsync"/> — the worker was told through
/// <c>notifications/cancelled</c>, and a worker that ignores that is still busy.</description></item>
/// </list>
/// <b>The pool exists, so the rules above are enforced rather than hypothetical.</b> Story 7's sticky
/// supervision IS that pool: <see cref="StickyWorkerPoll"/> retires a session whose send did not
/// complete, and proves liveness with a bounded probe before reusing one that was cleanly abandoned.
/// Read them as binding — a reader who takes them for anticipated design would conclude that reusing a
/// session needs no check. Still not an invitation to build a second pool here: the state lives in
/// the registry, not in the relay.
/// </para>
/// <para>
/// <b>Not a raw pass-through in both directions.</b> A child request is bridged through typed parent API
/// rather than forwarded raw, because the child's and the parent's request-id spaces are independent — a
/// raw forward would route the client's response to the parent session's own router instead of back into
/// this relay. Byte identity is contractually required only for notifications, where ClioRing correlates
/// on <c>params.progressToken</c> (compared ordinally) and reads <c>params._meta.clioStageEvent</c>; a
/// mismatch there is dropped SILENTLY on the consumer side, which is why those bytes are never rebuilt.
/// </para>
/// </remarks>
public sealed class WorkerRelaySession : IAsyncDisposable {

	/// <summary>The JSON-RPC method a worker uses to ask its client for a model completion.</summary>
	internal const string SamplingCreateMessageMethod = "sampling/createMessage";

	/// <summary>The JSON-RPC method that lists a worker's tools — also the liveness probe.</summary>
	internal const string ListToolsMethod = "tools/list";

	/// <summary>The JSON-RPC method that invokes a worker's tool.</summary>
	internal const string CallToolMethod = "tools/call";

	private const string InitializeMethod = "initialize";
	private const string InitializedNotificationMethod = "notifications/initialized";
	private const int MethodNotFoundErrorCode = -32601;
	private const int InternalErrorCode = -32603;

	private readonly ITransport _childTransport;
	private readonly IParentMcpSession _parentSession;
	private readonly WorkerRelayOptions _options;
	private readonly ILogger _logger;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Dictionary<string, TaskCompletionSource<JsonNode>> _pendingRequests = [];
	private readonly object _pendingRequestsLock = new();

	private Task _readLoop;
	private long _nextRequestId;
	private Exception _closure;
	private int _disposed;

	internal WorkerRelaySession(ITransport childTransport, IParentMcpSession parentSession,
		WorkerRelayOptions options, ILogger logger) {
		_childTransport = childTransport;
		_parentSession = parentSession;
		_options = options;
		_logger = logger;
	}

	/// <summary>
	/// Gets the protocol revision the worker agreed to, available once the handshake has completed.
	/// </summary>
	public string NegotiatedProtocolVersion { get; private set; }

	/// <summary>
	/// Gets a value indicating whether disposal ran out of
	/// <see cref="WorkerRelayOptions.NotificationDrainGrace"/> with the read loop still forwarding — so a
	/// notification the worker had already emitted may never have reached the client.
	/// </summary>
	/// <remarks>
	/// Readable AFTER disposal, by the supervisor, alongside the warning the session logs. This is the
	/// counted form of the residual loss described in the type remarks: the terminal stage event is the one
	/// most likely to be lost here, and a caller that bounds itself on that event (ADR rule 4) needs to be
	/// able to tell "it never arrived" from "it was thrown away during teardown".
	/// </remarks>
	public bool NotificationDrainTimedOut { get; private set; }

	/// <summary>
	/// Gets a value indicating whether disposal ABANDONED the read loop, because the loop was still stuck
	/// <see cref="WorkerRelayOptions.ReadLoopShutdownGrace"/> after the session lifetime token was cancelled.
	/// </summary>
	/// <remarks>
	/// The event this bound exists to survive, made readable. Nothing is left unobserved by abandoning the
	/// loop — it swallows its own exceptions and has already faulted every pending caller — but the worker
	/// process is then reclaimed by the supervisor's lease rather than by this session, and an operator
	/// looking at a worker that outlived its relay has to be able to see that this is why.
	/// </remarks>
	public bool ReadLoopAbandoned { get; private set; }

	/// <summary>
	/// Lists the worker's tools.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The worker's tool list.</returns>
	public async Task<ListToolsResult> ListToolsAsync(CancellationToken cancellationToken) {
		JsonNode result = await RequestAsync(ListToolsMethod, new JsonObject(), cancellationToken)
			.ConfigureAwait(false);
		return Deserialize<ListToolsResult>(result, ListToolsMethod);
	}

	/// <summary>
	/// Invokes one tool in the worker.
	/// </summary>
	/// <param name="parameters">
	/// The call parameters, relayed AS GIVEN. Hand over the caller's own
	/// <see cref="CallToolRequestParams.Meta"/> object rather than a fresh one:
	/// <see cref="RequestParams.ProgressToken"/> is a read-only view over <c>Meta["progressToken"]</c>, so
	/// re-issuing a token of the parent's own making makes ClioRing drop every stage event of the run — its
	/// correlation is ORDINAL and its failure is silent. The in-repo precedent is
	/// <c>ClioRunTool.DispatchAsync</c>, which copies <c>Meta</c> for exactly this reason. Everything else
	/// the caller's params carry (2.2.0's <c>InputResponses</c> / <c>RequestState</c>, any reserved
	/// <c>_meta</c> key) rides along because the object is serialised whole and never rebuilt field by field.
	/// </param>
	/// <param name="cancellationToken">Cancels the request; the worker is NOT killed by this.</param>
	/// <returns>The worker's tool result.</returns>
	public async Task<CallToolResult> CallToolAsync(CallToolRequestParams parameters,
		CancellationToken cancellationToken) {
		if (parameters is null) {
			throw new ArgumentNullException(nameof(parameters));
		}
		JsonNode payload = JsonSerializer.SerializeToNode(parameters, McpJsonUtilities.DefaultOptions);
		JsonNode result = await RequestAsync(CallToolMethod, payload, cancellationToken)
			.ConfigureAwait(false);
		return Deserialize<CallToolResult>(result, CallToolMethod);
	}

	/// <summary>
	/// Checks that the worker is still answering, by listing its tools.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>tools/list</c> and never <c>ping</c>: <c>ping</c> is not served on protocol revision
	/// <c>2026-07-28</c> (ADR §3.1b), and ClioRing moved its own health probe to <c>tools/list</c> in the
	/// same SDK upgrade for that reason.
	/// </para>
	/// <para>
	/// A probe that runs out of its bound abandons a request the worker already has, so the worker is told —
	/// the same <c>notifications/cancelled</c> every abandoned request emits (see <see cref="RequestAsync"/>).
	/// That is deliberate: a worker still composing a tool list nobody will read should stop, and a worker
	/// that ignores the notification is exactly the one this verdict is about.
	/// </para>
	/// </remarks>
	/// <param name="cancellationToken">Cancels the probe.</param>
	/// <param name="timeout">
	/// How long to wait for the worker's answer, overriding
	/// <see cref="WorkerRelayOptions.LivenessProbeTimeout"/> for this call only. Omit it to use the session's
	/// own bound; a caller whose remaining budget is smaller than that bound passes it here.
	/// </param>
	/// <returns>
	/// <c>true</c> when the worker answered; <c>false</c> when it failed, when it closed its pipe, OR when it
	/// did not answer inside the probe's own bound. The third outcome is the one the probe exists for: a
	/// worker with an open pipe that answers nothing is indistinguishable from a healthy one to a call that
	/// never returns.
	/// </returns>
	/// <exception cref="OperationCanceledException">
	/// <paramref name="cancellationToken"/> fired. This is deliberately NOT reported as <c>false</c>: a
	/// cancelled probe learned nothing about the worker, and collapsing the two makes a shutdown look like a
	/// dead worker. If the caller's token and the probe's own bound fire together, the caller wins.
	/// </exception>
	public async Task<bool> ProbeLivenessAsync(CancellationToken cancellationToken, TimeSpan? timeout = null) {
		// The bound is the probe's OWN, linked to the caller's token rather than replacing it. Without it the
		// probe completes only when the worker answers, when its pipe closes, or when the caller brought a
		// token — and the worker this question exists to catch does none of the first two, so the thread that
		// was meant to order the kill is the one that hangs.
		using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		bounded.CancelAfter(timeout ?? _options.LivenessProbeTimeout);
		try {
			await ListToolsAsync(bounded.Token).ConfigureAwait(false);
			return true;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
			// The probe's own bound expired. That is a VERDICT about the worker — "it did not answer in time" —
			// and not a cancellation, so it is reported like every other failure the probe observes. The filter
			// is what keeps the two exits apart, and it settles the tie the right way: if the caller's token
			// fired too, this guard is false and the cancellation below wins.
			return false;
		}
		catch (OperationCanceledException) {
			throw;
		}
		catch (Exception) {
			// A probe reports; it does not decide. The caller (the supervisor) owns the kill.
			return false;
		}
	}

	/// <summary>
	/// Sends one request to the worker and awaits its correlated answer.
	/// </summary>
	/// <param name="method">The JSON-RPC method.</param>
	/// <param name="parameters">The raw parameters node.</param>
	/// <param name="cancellationToken">
	/// Cancels this request only. Two things follow from it firing, both described in the type remarks: the
	/// worker is TOLD through <c>notifications/cancelled</c> when the request had already been written, and
	/// the session is RETIRED when it had not — a send that did not complete may have left half a frame on
	/// the child's stdin.
	/// </param>
	/// <returns>The raw result node the worker returned.</returns>
	/// <exception cref="WorkerRelayException">
	/// The worker answered an error, closed its pipe, or the session was retired by an earlier incomplete
	/// send.
	/// </exception>
	public async Task<JsonNode> RequestAsync(string method, JsonNode parameters,
		CancellationToken cancellationToken) {
		if (string.IsNullOrWhiteSpace(method)) {
			throw new ArgumentException("A JSON-RPC method name is required.", nameof(method));
		}
		RequestId id = new(Interlocked.Increment(ref _nextRequestId));
		string correlationKey = id.ToString();
		TaskCompletionSource<JsonNode> slot = new(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (_pendingRequestsLock) {
			if (_closure is not null) {
				throw AsRelayFailure(_closure);
			}
			_pendingRequests[correlationKey] = slot;
		}
		// Registered BEFORE the send so a token cancelled mid-flight still releases the awaiter: without
		// this the caller would wait for a worker that may never answer, and the parent's budget kill
		// would leave a permanently hung task behind it.
		using CancellationTokenRegistration cancellation = cancellationToken.Register(static state => {
			(WorkerRelaySession session, string key, CancellationToken token) =
				((WorkerRelaySession, string, CancellationToken))state;
			session.TakePending(key)?.TrySetCanceled(token);
		}, (this, correlationKey, cancellationToken));
		// The one fact both failure paths below turn on. The SDK's transport serialises a COMPLETED send, not
		// an atomic one, so "the request reached the worker" and "the caller gave up" are different states with
		// opposite correct answers — see the type remarks.
		bool sent = false;
		try {
			await _childTransport
				.SendMessageAsync(new JsonRpcRequest { Id = id, Method = method, Params = parameters },
					cancellationToken)
				.ConfigureAwait(false);
			sent = true;
			return await slot.Task.ConfigureAwait(false);
		}
		catch (Exception) {
			TakePending(correlationKey);
			if (!sent) {
				// RETIRED, not repaired. The send was abandoned somewhere inside serialize → payload write →
				// newline write → flush, each of which takes this very token, so the child's stdin may now hold
				// an unterminated line; the next writer's JSON would be appended to it and the worker would get
				// one frame it cannot parse. Setting the closure makes every later request fail at the guard
				// above, and the supervisor's lease reclaims the process. Passing CancellationToken.None to the
				// send instead is NOT the fix: this await is inline, so an uncancellable write against a full
				// pipe hangs the caller past its own budget — the wedge, one layer up.
				FailAllPending(new WorkerRelayException(
					$"The relay session was retired: the '{method}' request was not written to the worker "
					+ "completely, so its transport may hold a partial JSON-RPC frame."));
			}
			else if (cancellationToken.IsCancellationRequested && !IsRetired
				&& !string.Equals(method, InitializeMethod, StringComparison.Ordinal)) {
				// The worker HAS the request and nobody is waiting for the answer any more. A per-call worker
				// dies with the supervisor's kill either way, but a sticky one would go on executing the
				// abandoned tool, go on holding its Creatio session, and then be handed the next call on the
				// same transport with the old one still in flight.
				NotifyWorkerOfCancellation(id, method);
			}
			throw;
		}
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync() {
		if (Interlocked.Exchange(ref _disposed, 1) == 1) {
			return;
		}
		// The transport goes FIRST, and the lifetime token is not cancelled yet. Closing the transport
		// completes its message channel (checked against the shipped StreamClientSessionTransport, not
		// assumed), so the read loop drains what the worker already emitted and ends by itself — and every
		// one of those notifications is still forwarded under a token nobody has cancelled. Cancelling first
		// would instead CANCEL the forward that is in flight, and the terminal stage event is the one most
		// likely to be it; see the type remarks for why that silent loss is worse than the wedge.
		try {
			await _childTransport.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception) {
			// Disposing the transport closes the worker's pipes; it never terminates the worker, which the
			// supervisor's lease owns. A stream that is already gone is not a failure of this session.
		}
		if (_readLoop is not null
			&& !await WaitBoundedAsync(_readLoop, _options.NotificationDrainGrace).ConfigureAwait(false)) {
			// Bounded, so a client that never accepts a notification cannot hold teardown open — but REPORTED,
			// because what is given up here is a notification the worker emitted and the client never saw.
			NotificationDrainTimedOut = true;
			_logger.WriteWarning(
				$"The MCP worker relay gave up draining the worker's notifications after "
				+ $"{_options.NotificationDrainGrace.TotalSeconds:0.###}s: the client did not accept everything "
				+ "the worker had already emitted, so a stage event — possibly the terminal one a caller waits "
				+ "on — did not reach it.");
		}
		await _lifetime.CancelAsync().ConfigureAwait(false);
		FailAllPending(new WorkerRelayException("The relay session was disposed."));
		if (_readLoop is null) {
			_lifetime.Dispose();
			return;
		}
		// BOUNDED on purpose, and this bound is the point. The loop forwards notifications AWAITED IN PLACE —
		// that is what makes the client observe the worker's order — so a client that BLOCKS instead of
		// throwing stalls the loop at its head. An unbounded join here would turn that into a teardown that
		// never completes: the relay wedged and the worker leaked, which is exactly the failure class this
		// execution boundary exists to remove, reintroduced one layer up. So a stuck loop is ABANDONED rather
		// than joined. Nothing goes unobserved by abandoning it (it swallows every exception itself and has
		// already faulted every pending caller), and the worker process belongs to the supervisor's lease, not
		// to this session.
		if (!await WaitBoundedAsync(_readLoop, _options.ReadLoopShutdownGrace).ConfigureAwait(false)) {
			ReadLoopAbandoned = true;
			_logger.WriteWarning(
				$"The MCP worker relay abandoned its child read loop after "
				+ $"{_options.ReadLoopShutdownGrace.TotalSeconds:0.###}s: the client never released a forward "
				+ "and ignored cancellation. Teardown completed anyway so the relay cannot wedge; the worker "
				+ "process is reclaimed by the supervisor's lease rather than by this session.");
			// The CancellationTokenSource is deliberately NOT disposed while an abandoned loop can still read
			// its token: disposing it would replace the loop's clean cancellation with ObjectDisposedException
			// for no gain. It carries no timer and no external registration, so the GC reclaims it.
			return;
		}
		try {
			await _readLoop.ConfigureAwait(false);
		}
		catch (Exception) {
			// The loop's own failure is already reported to every pending caller.
		}
		_lifetime.Dispose();
	}

	/// <summary>
	/// Waits for <paramref name="work"/> for at most <paramref name="bound"/>.
	/// </summary>
	/// <remarks>
	/// The delay is cancelled and then OBSERVED when the work wins, rather than left to expire on its own:
	/// a bare <c>Task.Delay(bound, CancellationToken.None)</c> keeps a timer alive for the whole grace past a
	/// clean teardown, and one such timer per worker call is a leak in the hot path this boundary was built
	/// for.
	/// </remarks>
	/// <param name="work">The task to wait on.</param>
	/// <param name="bound">How long to wait.</param>
	/// <returns><c>true</c> when the work finished inside the bound; <c>false</c> when the bound expired.</returns>
	private static async Task<bool> WaitBoundedAsync(Task work, TimeSpan bound) {
		if (work.IsCompleted) {
			return true;
		}
		using CancellationTokenSource timer = new();
		Task expiry = Task.Delay(bound, timer.Token);
		Task finished = await Task.WhenAny(work, expiry).ConfigureAwait(false);
		if (!ReferenceEquals(finished, work)) {
			return false;
		}
		await timer.CancelAsync().ConfigureAwait(false);
		try {
			await expiry.ConfigureAwait(false);
		}
		catch (OperationCanceledException) {
			// Cancelling the timer is how it is stopped; awaiting it here is only how its cancellation is
			// observed, so no faulted task is left behind for the finalizer to notice.
		}
		return true;
	}

	/// <summary>
	/// Starts the single consumer of the child's transport.
	/// </summary>
	internal void StartReadLoop() =>
		_readLoop = Task.Run(RunReadLoopAsync, CancellationToken.None);

	/// <summary>
	/// Performs the child handshake: <c>initialize</c>, then <c>notifications/initialized</c>.
	/// </summary>
	/// <remarks>
	/// The relay deliberately does NOT probe <c>server/discover</c> first, the way a 2.2.0 client does. A
	/// child that answers that probe with a SUCCESS result of the wrong shape stalls the handshake for the
	/// full discover-probe timeout (5 s) and then hard-fails with a <c>JsonException</c> instead of falling
	/// back to <c>initialize</c> — five seconds of dead time inside the very budget the parent enforces
	/// (ADR §3.1b). Going straight to <c>initialize</c> cannot hit that trap at all.
	/// </remarks>
	/// <param name="cancellationToken">Cancels the handshake.</param>
	/// <returns>A task that completes when the worker is initialized.</returns>
	internal async Task HandshakeAsync(CancellationToken cancellationToken) {
		JsonObject capabilities = new();
		if (_parentSession.SupportsSampling) {
			// Advertised as raw JSON on purpose: the typed SamplingCapability is [Obsolete] in 2.2.0
			// (MCP9005), and the hand-rolled child leg has no reason to touch the deprecated type just to
			// emit an empty object.
			capabilities["sampling"] = new JsonObject();
		}
		JsonObject initializeParams = new() {
			["protocolVersion"] = _options.ProtocolVersion,
			["capabilities"] = capabilities,
			["clientInfo"] = new JsonObject {
				["name"] = _options.ClientName,
				["version"] = _options.ClientVersion
			}
		};
		JsonNode result = await RequestAsync(InitializeMethod, initializeParams, cancellationToken)
			.ConfigureAwait(false);
		string negotiated = result?["protocolVersion"]?.GetValue<string>();
		if (string.IsNullOrEmpty(negotiated)) {
			// Validated here because a hand-rolled handshake gets no SDK exception path: an unusable
			// initialize result would otherwise surface much later as an inexplicable tool failure.
			throw new WorkerRelayException(
				"The worker's initialize result carried no protocolVersion, so the session is not usable.");
		}
		if (!string.Equals(negotiated, _options.ProtocolVersion, StringComparison.Ordinal)) {
			// A counter-offer is REJECTED rather than stored. Both legs are clio, so a differing revision means
			// the parent and the worker are different builds — and the requested revision is load-bearing, not
			// cosmetic: sampling is deprecated as of 2026-07-28 (ADR §3.1a, OQ-6) and ADR rule 1 depends on it,
			// so silently accepting whatever the worker offers is how the page semantic review starts degrading
			// to Skipped=true with nothing anywhere saying why.
			throw new WorkerRelayException(
				$"The worker negotiated protocol revision '{negotiated}' instead of the requested "
				+ $"'{_options.ProtocolVersion}', so the relay's measured properties no longer apply.");
		}
		NegotiatedProtocolVersion = negotiated;
		await _childTransport
			.SendMessageAsync(new JsonRpcNotification { Method = InitializedNotificationMethod },
				cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>
	/// THE READ LOOP. One consumer, taking messages off the pipe serially, forwarding notifications
	/// awaited in place so the client observes the worker's own order.
	/// </summary>
	/// <returns>A task that completes when the worker's pipe closes or the session is disposed.</returns>
	private async Task RunReadLoopAsync() {
		try {
			await foreach (JsonRpcMessage message in _childTransport.MessageReader
				.ReadAllAsync(_lifetime.Token).ConfigureAwait(false)) {
				switch (message) {
					case JsonRpcNotification notification:
						await ForwardNotificationAsync(notification).ConfigureAwait(false);
						break;
					case JsonRpcRequest request:
						// Off the loop: a slow client must not stall notification forwarding.
						_ = Task.Run(() => AnswerChildRequestAsync(request), CancellationToken.None);
						break;
					case JsonRpcResponse response:
						TakePending(response.Id.ToString())?.TrySetResult(response.Result);
						break;
					case JsonRpcError error:
						TakePending(error.Id.ToString())?.TrySetException(new WorkerRelayException(
							$"The worker returned an error: {error.Error?.Message}", error.Error?.Code));
						break;
				}
			}
			// The pipe closing IS the completion signal. Faulting every awaiter here is what keeps a killed
			// or crashed worker from leaving hung callers behind — the failure mode the spike had.
			FailAllPending(new WorkerRelayException("The worker closed its transport before answering."));
		}
		catch (OperationCanceledException) {
			FailAllPending(new WorkerRelayException("The relay session was closed while the worker was running."));
		}
		catch (Exception ex) {
			FailAllPending(new WorkerRelayException("The worker's transport failed.", ex));
		}
	}

	/// <summary>
	/// Re-emits one of the worker's notifications to the real client.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A FRESH envelope carrying the SAME <see cref="JsonNode"/> subtree. Fresh, because the incoming
	/// message's context points at the CHILD's transport and must not travel upward; same subtree, because
	/// <c>_meta.clioStageEvent</c> and the exact <c>progressToken</c> — a JSON number stays a number — are
	/// what ClioRing correlates on, and a deserialise-and-rebuild would drop the parts of <c>_meta</c> no
	/// typed DTO knows about.
	/// </para>
	/// <para>
	/// Sent under the SESSION LIFETIME token, not <see cref="CancellationToken.None"/>. Because the forward
	/// is awaited inside the read loop, a client that blocks rather than throws stalls the loop head-of-line;
	/// under the lifetime token, disposal releases a co-operative client once
	/// <see cref="WorkerRelayOptions.NotificationDrainGrace"/> has passed, instead of waiting out the whole
	/// shutdown grace. The drain window is deliberately BEFORE that cancellation and not after it: a forward
	/// in flight at disposal is a notification the worker already emitted, and cancelling it — which is what
	/// this <c>catch</c> would then swallow — is how a terminal stage event disappears without a trace. A
	/// client that ignores the token after the window is handled by the shutdown grace, which reports the
	/// abandonment. See <see cref="DisposeAsync"/> and the type remarks for the trade.
	/// </para>
	/// </remarks>
	/// <param name="notification">The worker's notification.</param>
	/// <returns>A task that completes when the client has been given the notification.</returns>
	private async Task ForwardNotificationAsync(JsonRpcNotification notification) {
		if (!MayForward(notification)) {
			return;
		}
		try {
			await _parentSession.SendMessageAsync(
				new JsonRpcNotification { Method = notification.Method, Params = notification.Params },
				_lifetime.Token).ConfigureAwait(false);
		}
		catch (Exception) {
			// A disconnected client must never tear down a running worker mid-deploy; the same rule the
			// stage-event forwarder, the log notifier and the progress heartbeat already follow.
		}
	}

	/// <summary>
	/// Runs <see cref="WorkerRelayOptions.NotificationTap"/>, if one was supplied, and reports whether the
	/// notification may travel upward.
	/// </summary>
	/// <remarks>
	/// Called from INSIDE the read loop, so the tap sees the worker's own order — which is the whole reason
	/// the terminal-stage protocol can key on it (ADR §3.3). It defaults to FORWARDING on any failure: a
	/// tap that threw has learned nothing about this notification, and a relay that dropped client traffic
	/// because an observer misbehaved would turn a diagnostic defect into a silent protocol one.
	/// </remarks>
	/// <param name="notification">The worker's notification.</param>
	/// <returns><c>true</c> when the relay may forward it.</returns>
	private bool MayForward(JsonRpcNotification notification) {
		Func<JsonRpcNotification, bool> tap = _options.NotificationTap;
		if (tap is null) {
			return true;
		}
		try {
			return tap(notification);
		}
		catch (Exception exception) {
			_logger.WriteWarning(
				"The MCP worker relay's notification tap failed, so the notification was forwarded "
				+ $"unobserved: {SensitiveErrorTextRedactor.Redact(exception.Message)}");
			return true;
		}
	}

	/// <summary>
	/// Answers one child→parent request: sampling is bridged to the real client, everything else is
	/// refused so the worker fails fast instead of waiting on a relay that will never answer.
	/// </summary>
	/// <param name="request">The worker's request.</param>
	/// <returns>A task that completes when the worker has its answer.</returns>
	private async Task AnswerChildRequestAsync(JsonRpcRequest request) {
		try {
			if (!string.Equals(request.Method, SamplingCreateMessageMethod, StringComparison.Ordinal)) {
				await RespondWithErrorAsync(request.Id, MethodNotFoundErrorCode,
					$"'{request.Method}' is not relayed to the client.").ConfigureAwait(false);
				return;
			}
			if (!_parentSession.SupportsSampling) {
				await RespondWithErrorAsync(request.Id, MethodNotFoundErrorCode,
					"The client did not advertise the sampling capability.").ConfigureAwait(false);
				return;
			}
			// MCP9005: the sampling payload types are deprecated in SDK 2.2.0 (SEP-2577). Suppressed with a
			// justification, never silently — see IParentMcpSession.SampleAsync and OQ-6.
#pragma warning disable MCP9005
			CreateMessageRequestParams parameters = request.Params is null
				? throw new WorkerRelayException("The worker's sampling request carried no parameters.")
				: Deserialize<CreateMessageRequestParams>(request.Params, SamplingCreateMessageMethod);
			CreateMessageResult result = await _parentSession
				.SampleAsync(parameters, _lifetime.Token).ConfigureAwait(false);
#pragma warning restore MCP9005
			await _childTransport.SendMessageAsync(new JsonRpcResponse {
				Id = request.Id,
				Result = JsonSerializer.SerializeToNode(result, McpJsonUtilities.DefaultOptions)
			}, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception ex) {
			// The worker gets a JSON-RPC error rather than silence, because silence is what turns a failed
			// review into a call that hangs until the budget kills it. Redacted with the same rule as every
			// other MCP error text, so a path or credential can never ride out on this leg.
			try {
				await RespondWithErrorAsync(request.Id, InternalErrorCode,
					SensitiveErrorTextRedactor.Redact(ex.Message)).ConfigureAwait(false);
			}
			catch (Exception) {
				// The worker is already gone; there is nothing left to tell.
			}
		}
	}

	private async Task RespondWithErrorAsync(RequestId id, int code, string message) =>
		await _childTransport.SendMessageAsync(new JsonRpcError {
			Id = id,
			Error = new JsonRpcErrorDetail { Code = code, Message = message }
		}, CancellationToken.None).ConfigureAwait(false);

	/// <summary>
	/// Gets a value indicating whether this session has been RETIRED: it has a closure, so it will never
	/// be written to again and every later request fails at the guard in <see cref="RequestAsync"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>It is the union of every reason a session stops being writable</b> — a send that did not
	/// complete, disposal, the worker closing its pipe, and a transport failure. The union is deliberate
	/// rather than lossy: the ONE decision this property exists to serve is "may this session carry
	/// another call", and all four reasons answer it the same way. The interrupted send is the one that
	/// makes the rule binding (ADR §3.2a): the SDK's <c>_sendLock</c> guarantees a completed send and not
	/// an atomic one, so a token firing mid-send releases that lock over an unterminated line and the next
	/// writer's JSON is appended to it.
	/// </para>
	/// <para>
	/// Read under the same lock that writes the closure. Inside this type it also gates a WRITE to the
	/// child: telling a worker its call was cancelled over a transport that is closing achieves nothing
	/// and would only surface as an exception in an unrelated place.
	/// </para>
	/// </remarks>
	public bool IsRetired {
		get {
			lock (_pendingRequestsLock) {
				return _closure is not null;
			}
		}
	}

	/// <summary>
	/// Tells the worker that the parent abandoned one of its requests.
	/// </summary>
	/// <remarks>
	/// <para>
	/// FIRE AND FORGET, and off the caller's thread, because the caller's cancellation must not be delayed by
	/// a pipe write — a client that gave up waiting is entitled to be released immediately. It is also
	/// deliberately NOT emitted from the <see cref="CancellationToken.Register"/> callback: that callback runs
	/// synchronously on whichever thread cancelled the token, so a write to a closing transport would surface
	/// there rather than here.
	/// </para>
	/// <para>
	/// Sent under the session lifetime token rather than the caller's, which has just fired. Its own failure
	/// is swallowed: the worker may already have been killed by the supervisor's lease, and a notification
	/// nobody can receive is not a failure of the call that was cancelled.
	/// </para>
	/// </remarks>
	/// <param name="id">The request id the relay issued for the abandoned call.</param>
	/// <param name="method">The abandoned method, named in the reason so a worker log says what was dropped.</param>
	private void NotifyWorkerOfCancellation(RequestId id, string method) {
		JsonNode payload = JsonSerializer.SerializeToNode(
			new CancelledNotificationParams {
				RequestId = id,
				Reason = $"The parent abandoned its '{method}' request."
			}, McpJsonUtilities.DefaultOptions);
		_ = Task.Run(async () => {
			try {
				await _childTransport.SendMessageAsync(new JsonRpcNotification {
					Method = NotificationMethods.CancelledNotification,
					Params = payload
				}, _lifetime.Token).ConfigureAwait(false);
			}
			catch (Exception) {
				// See the remarks: an unreachable worker is not this call's failure.
			}
		}, CancellationToken.None);
	}

	private TaskCompletionSource<JsonNode> TakePending(string correlationKey) {
		lock (_pendingRequestsLock) {
			return _pendingRequests.Remove(correlationKey, out TaskCompletionSource<JsonNode> slot)
				? slot
				: null;
		}
	}

	private void FailAllPending(Exception failure) {
		List<TaskCompletionSource<JsonNode>> orphaned;
		lock (_pendingRequestsLock) {
			_closure ??= failure;
			orphaned = [.. _pendingRequests.Values];
			_pendingRequests.Clear();
		}
		foreach (TaskCompletionSource<JsonNode> slot in orphaned) {
			slot.TrySetException(failure);
		}
	}

	private static WorkerRelayException AsRelayFailure(Exception closure) =>
		closure as WorkerRelayException
		?? new WorkerRelayException("The relay session is closed.", closure);

	private static TResult Deserialize<TResult>(JsonNode payload, string method) {
		try {
			return JsonSerializer.Deserialize<TResult>(payload, McpJsonUtilities.DefaultOptions);
		}
		catch (JsonException ex) {
			throw new WorkerRelayException($"The worker's '{method}' payload could not be read.", ex);
		}
	}
}
