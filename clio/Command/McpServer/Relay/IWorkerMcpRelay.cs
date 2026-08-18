using System;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// The parent leg of the worker relay: the one seam through which a child worker's traffic reaches the
/// REAL MCP client, and through which the client's answers come back.
/// </summary>
/// <remarks>
/// <para>
/// This exists as an interface for two reasons. The first is testability: the concrete parent is
/// <see cref="ModelContextProtocol.Server.McpServer"/>, whose members are neither virtual nor
/// interface-backed, so a relay that talked to it directly could not be observed by a unit test — and the
/// two properties that fail SILENTLY here (notification ORDER and whether sampling was forwarded at all)
/// are exactly the properties a test has to pin.
/// </para>
/// <para>
/// The second is containment of a deprecation. Sampling is <c>[Obsolete]</c> throughout MCP SDK 2.2.0
/// (diagnostic <c>MCP9005</c>, SEP-2577), so every suppression lives in the single adapter
/// <see cref="McpServerParentSession"/> and nowhere in the relay itself. See ADR §3.1a / OQ-6 for the
/// migration to <c>InputRequest</c> / <c>ResolveInputRequestsAsync</c>; nothing new may be built on the
/// deprecated surface in the meantime.
/// </para>
/// </remarks>
public interface IParentMcpSession {

	/// <summary>
	/// Gets a value indicating whether the real client advertised the sampling capability.
	/// </summary>
	/// <remarks>
	/// The relay mirrors this onto the child leg's <c>initialize</c>. Advertising sampling to the child
	/// when the client cannot serve it would make the child issue requests the relay can only refuse,
	/// which degrades the page semantic review to <c>Skipped=true</c> after a wasted round trip instead
	/// of before it.
	/// </remarks>
	bool SupportsSampling { get; }

	/// <summary>
	/// Sends one message to the real client verbatim.
	/// </summary>
	/// <param name="message">
	/// The message to send. The relay hands over notifications whose <c>Params</c> is the very
	/// <see cref="System.Text.Json.Nodes.JsonNode"/> subtree taken off the child's pipe, so
	/// <c>_meta.clioStageEvent</c> and the exact <c>progressToken</c> (including when it is a JSON
	/// NUMBER) travel unchanged. An implementation must not deserialise and rebuild it.
	/// </param>
	/// <param name="cancellationToken">Cancels the send.</param>
	/// <returns>A task that completes when the message has been handed to the client transport.</returns>
	Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken);

	/// <summary>
	/// Asks the real client to run one sampling (model) request on the child's behalf.
	/// </summary>
	/// <param name="requestParams">The child's request, deserialised from its raw form.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The client's answer, which the relay returns into the child's pending request.</returns>
	// MCP9005: the sampling payload types are deprecated in SDK 2.2.0 (SEP-2577). Suppressed WITH this
	// justification rather than silently, following the three existing sites in this repo
	// (BindingsModule, McpLogNotifier, PageBodySamplingService): the feature still works and ADR rule 1
	// depends on it, and OQ-6 tracks the migration to InputRequest / ResolveInputRequestsAsync. Nothing
	// new may be built on it in the meantime.
#pragma warning disable MCP9005
	ValueTask<CreateMessageResult> SampleAsync(CreateMessageRequestParams requestParams,
		CancellationToken cancellationToken);
#pragma warning restore MCP9005
}

/// <summary>
/// What one child leg advertises about itself during <c>initialize</c>.
/// </summary>
/// <remarks>
/// A plain value carrier: no behaviour, so it is a <see langword="record"/> per the DI policy.
/// </remarks>
public sealed record WorkerRelayOptions {

	/// <summary>
	/// The protocol revision the relay REQUESTS from the worker.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This default is the measured one, not a guess. The relay spike that proved sampling relays to the
	/// real client (121/121 runs on SDK 2.2.0) and that <c>_meta.clioStageEvent</c> survives
	/// byte-identically negotiated exactly this revision. Raising it to <c>2026-07-28</c> is not a
	/// cosmetic change: sampling is deprecated as of that revision (ADR §3.1a), and clio's own
	/// <c>PageBodySamplingService</c> already documents its sampling path as the legacy-initialize one.
	/// </para>
	/// <para>
	/// Any change here must be re-measured against the two silent failures — a dropped sampling round
	/// trip shows up only as a quietly worse answer, never as an error.
	/// </para>
	/// </remarks>
	public const string MeasuredProtocolVersion = "2024-11-05";

	/// <summary>Gets the protocol revision requested from the worker.</summary>
	public string ProtocolVersion { get; init; } = MeasuredProtocolVersion;

	/// <summary>Gets the client name the relay reports to the worker.</summary>
	public string ClientName { get; init; } = "clio-mcp-worker-relay";

	/// <summary>Gets the client version the relay reports to the worker.</summary>
	public string ClientVersion { get; init; } = "1";

	/// <summary>
	/// Gets how long disposal lets the read loop keep FORWARDING before the session lifetime token is
	/// cancelled — the window in which a notification the worker already emitted still reaches the client.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This window exists because the notification most likely to be in flight (or one slot behind, still
	/// unread in the transport channel) when a caller disposes right after a tool result is the
	/// AUTHORITATIVE TERMINAL STAGE EVENT — the one ADR rule 4 says the parent waits for and the
	/// deploy/uninstall family bounds itself on. Cancelling the forward instead of delivering it loses that
	/// event with no trace anywhere, because a forward swallows its own failure so a disconnected client
	/// cannot tear down a running worker.
	/// </para>
	/// <para>
	/// Bounded all the same: a client that never accepts the notification must not hold teardown open. When
	/// the window expires the session says so through <see cref="Clio.Common.ILogger"/> and
	/// <c>WorkerRelaySession.NotificationDrainTimedOut</c>, so the loss is reported rather than silent.
	/// </para>
	/// </remarks>
	public TimeSpan NotificationDrainGrace { get; init; } = TimeSpan.FromSeconds(2);

	/// <summary>
	/// Gets how long disposal waits for the read loop to finish AFTER the session lifetime token is
	/// cancelled, before abandoning it.
	/// </summary>
	/// <remarks>
	/// Bounded, and the bound is the point. Notifications are forwarded AWAITED IN PLACE inside the read
	/// loop — that is what makes the client observe the worker's own order — so a client that BLOCKS instead
	/// of throwing stalls the loop at its head. An unbounded join during disposal would then make teardown
	/// itself never complete: the relay wedged and the worker leaked, which is the failure class this
	/// execution boundary exists to remove. Lowering this in a test is expected; removing the bound is not.
	/// </remarks>
	public TimeSpan ReadLoopShutdownGrace { get; init; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Gets how long <c>WorkerRelaySession.ProbeLivenessAsync</c> waits for the worker's <c>tools/list</c>
	/// answer before reporting the worker as not answering.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The probe needs a bound OF ITS OWN, because the one worker state it exists to catch is a worker whose
	/// stdout pipe is open and which answers nothing: that worker never closes the pipe and never responds, so
	/// a probe with no bound waits forever — and the thread that was meant to order the kill is the stuck one.
	/// A probe that hangs on the worker it is asking about reproduces, one process down, the exact wedge this
	/// execution boundary exists to remove.
	/// </para>
	/// <para>
	/// The value is set against the measured cost of the alternative rather than picked. Probing exists only
	/// because reusing a live worker beats spawning a new one, and a spawn plus <c>initialize</c> is p50
	/// <b>2.763 s</b> on Windows Server 2022 (ADR §2.4; ~0.65 s is the macOS best case, §1.2). So a probe
	/// allowed to run longer than a respawn has no reason to exist at all, which puts the ceiling at 2.763 s;
	/// 2 s sits under it while still leaving a busy-but-healthy worker — one draining a tool result, one
	/// answering a sampling round trip — room to serve a <c>tools/list</c> that costs it no I/O. A caller with
	/// a tighter budget than the default passes its own bound per call instead of editing this.
	/// </para>
	/// </remarks>
	public TimeSpan LivenessProbeTimeout { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Opens the parent side of one worker's MCP session: it takes an ALREADY CONNECTED child transport,
/// starts the single read loop that owns it, and performs the handshake.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the relay owns the child's read loop instead of using <c>McpClient</c>.</b> The SDK dispatches
/// client notification callbacks CONCURRENTLY, so a child that emits stage events <c>0..5</c> in order
/// reaches the client reordered (measured <c>[5,4,2,3,0,1]</c>, and a different permutation on the
/// retry). Adding a single-consumer queue in the parent does not fix it — a queue only preserves the
/// order its producer already had, and the producer is the racing dispatch layer. Reading
/// <see cref="ITransport.MessageReader"/> directly takes messages off the wire serially, BEFORE any SDK
/// dispatch exists, so forwarding inherits the pipe's order (ADR rule 12, §3.2). Anyone reading this as
/// a queue-placement problem will reintroduce the defect.
/// </para>
/// <para>
/// <b>This interface does not spawn anything.</b> The worker process is created and contained by
/// <c>Clio.Common.McpWorker.IWorkerProcessSupervisor</c>, which owns process creation for measured
/// reasons of its own, and the transport is attached to the streams it hands out — see
/// <see cref="IWorkerChildTransportOwner"/>.
/// </para>
/// </remarks>
public interface IWorkerMcpRelay {

	/// <summary>
	/// Starts the read loop over <paramref name="childTransport"/> and completes the child handshake.
	/// </summary>
	/// <param name="childTransport">
	/// A connected transport over the worker's stdio. The returned session becomes its ONLY consumer:
	/// <see cref="ITransport.MessageReader"/> is a channel reader, so a second consumer would steal
	/// messages.
	/// </param>
	/// <param name="parentSession">The parent leg — where the child's traffic is relayed to.</param>
	/// <param name="options">What to advertise during <c>initialize</c>; defaults when omitted.</param>
	/// <param name="cancellationToken">Cancels the handshake; the session is disposed if it does.</param>
	/// <returns>The open session, owning the read loop until it is disposed.</returns>
	/// <exception cref="ArgumentNullException">A required argument is missing.</exception>
	/// <exception cref="WorkerRelayException">The worker's handshake was malformed or it closed first.</exception>
	Task<WorkerRelaySession> OpenAsync(ITransport childTransport, IParentMcpSession parentSession,
		WorkerRelayOptions options, CancellationToken cancellationToken);
}
