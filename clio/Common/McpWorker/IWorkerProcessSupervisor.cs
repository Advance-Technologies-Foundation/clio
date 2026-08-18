using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.McpWorker;

/// <summary>
/// How a bounded worker run ended.
/// </summary>
public enum WorkerRunStatus {

	/// <summary>The worker exited on its own inside its budget.</summary>
	Completed,

	/// <summary>The budget expired and the worker was killed with its descendants.</summary>
	BudgetExpired,

	/// <summary>The caller cancelled the wait; the worker was killed with its descendants.</summary>
	Canceled
}

/// <summary>
/// The result of waiting for a worker inside its budget.
/// </summary>
/// <param name="Status">How the run ended.</param>
/// <param name="ExitCode">Exit code when the worker exited on its own; <see langword="null"/> otherwise.</param>
/// <param name="Elapsed">Time from SPAWN to the end of the run — never including time spent queued.</param>
/// <param name="Termination">What was signalled when the worker had to be killed.</param>
public sealed record WorkerRunResult(
	WorkerRunStatus Status,
	int? ExitCode,
	TimeSpan Elapsed,
	WorkerTerminationOutcome? Termination);

/// <summary>
/// How long a worker is expected to live, which decides which admission pool it is admitted from.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is an admission classification, not a scheduling hint.</b> A slot is HELD from spawn to lease
/// dispose, so the two values consume capacity in fundamentally different ways: a
/// <see cref="PerCall"/> worker occupies a slot for one answer, while a <see cref="Sticky"/> worker
/// occupies one for the whole operation it supervises — minutes to an hour. Mixing them in one pool
/// lets long lifetimes starve ordinary reads, which is why they draw from separate pools with separate
/// caps (ADR §3.2c).
/// </para>
/// </remarks>
public enum WorkerLifetime {

	/// <summary>
	/// The worker answers one call and is disposed. Admitted from the per-call pool, which queues under
	/// <see cref="WorkerProcessSupervisor.DefaultQueueWaitBound"/> and refuses with
	/// <see cref="WorkerQueueWaitExpiredException"/>.
	/// </summary>
	PerCall,

	/// <summary>
	/// The worker outlives the call that created it and is reached again by later calls. Admitted from
	/// the sticky pool, which does NOT queue: its cap is the number of concurrent long operations the
	/// host supports, so the next one is refused immediately with
	/// <see cref="WorkerStickyCapacityExceededException"/> rather than after a queue wait that could only
	/// end in the same refusal.
	/// </summary>
	Sticky
}

/// <summary>
/// One request to run a worker process.
/// </summary>
public sealed record WorkerSpawnRequest {

	/// <summary>
	/// Gets the lifetime this worker is admitted under, which selects the admission pool. Default
	/// <see cref="WorkerLifetime.PerCall"/>: a caller that does not say it is starting a long operation
	/// is not one, and the sticky pool's capacity is scarce by construction.
	/// </summary>
	public WorkerLifetime Lifetime { get; init; } = WorkerLifetime.PerCall;

	/// <summary>
	/// Gets the arguments appended after the worker verb resolved by
	/// <see cref="IClioExecutablePathProvider"/>. Ignored when <see cref="LaunchOverride"/> is set.
	/// </summary>
	public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

	/// <summary>
	/// Gets an explicit executable and argument vector to run instead of clio's own worker verb.
	/// Used by tests, which contain and kill a purpose-built fixture rather than a real worker.
	/// </summary>
	public ClioWorkerLaunchDescriptor LaunchOverride { get; init; }

	/// <summary>
	/// Gets the response budget for this call. The clock starts when the process is SPAWNED, never
	/// when the request is admitted — see <see cref="IWorkerLease.BudgetExpiresAtUtc"/>.
	/// </summary>
	public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(DefaultBudgetSeconds);

	/// <summary>
	/// Gets the working directory for the worker.
	/// </summary>
	/// <remarks>
	/// <b>Null does NOT mean "the parent's".</b> The fallback chain is this value, then
	/// <see cref="ClioWorkerLaunchDescriptor.WorkingDirectory"/> — which
	/// <see cref="ClioExecutablePathProvider"/> resolves to the directory the clio ASSEMBLY lives in —
	/// and only then the parent's current directory, which is reached solely when a descriptor states
	/// none. A caller that wants the worker to see the same "here" as the host must therefore say so:
	/// leaving this null starts the child in the clio installation, where anything a tool anchors on the
	/// current directory (<c>.clio-pages/{schema}/</c> above all) is written into clio's own install tree
	/// instead of the user's workspace, with a successful answer and no warning.
	/// </remarks>
	public string WorkingDirectory { get; init; }

	/// <summary>
	/// Gets environment variables handed to the worker. With
	/// <see cref="ClearInheritedEnvironment"/> these are the ONLY variables the worker sees beyond
	/// <see cref="InheritedEnvironmentVariableAllowlist"/>.
	/// </summary>
	public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }

	/// <summary>
	/// Gets a value indicating whether the inherited environment is cleared before the allowlist and
	/// <see cref="EnvironmentVariables"/> are applied. Default <see langword="true"/>: ADR rule 11
	/// requires the payload handed to a worker to be frozen, and it is not frozen while an ambient
	/// variable can contradict it.
	/// </summary>
	public bool ClearInheritedEnvironment { get; init; } = true;

	/// <summary>
	/// Gets the ambient variables copied into the cleared child environment, read from the parent
	/// immediately before launch. Defaults to
	/// <see cref="WorkerProcessSupervisor.DefaultInheritedEnvironmentVariableAllowlist"/>.
	/// </summary>
	public IReadOnlyCollection<string> InheritedEnvironmentVariableAllowlist { get; init; }

	/// <summary>Default response budget in seconds when the caller states none.</summary>
	public const int DefaultBudgetSeconds = 120;
}

/// <summary>
/// The talking surface of a running worker: who it is, its three streams, and whether it is still
/// alive. It holds no admission slot and owns no lifetime.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is separate from <see cref="IWorkerLease"/>, and it is not a taste preference.</b>
/// Admission governs CREATING a worker, never TALKING to one that already exists (ADR §3.2c). A caller
/// that resolves to a worker somebody else created — a <c>compile-status</c> poll reaching the sticky
/// worker running the compile — must be able to converse with it while taking no slot, because the slot
/// it would otherwise wait for is HELD BY THE VERY WORKER it is trying to reach. That is hold-and-wait,
/// not starvation: it does not resolve under load, and on a single-slot host one long operation makes
/// every later call, including its own status poll, unreachable.
/// </para>
/// <para>
/// <b>The distinction is OWNERSHIP, not capability.</b> An <see cref="IWorkerLease"/> is this plus the
/// right to end the worker and return its slot; a channel is the conversation with none of that.
/// Handing a poll the lease itself would hand it the kill switch, and a single <c>using</c> on the poll
/// path would then terminate the operation the poll was only observing.
/// <see cref="IWorkerReach.ReachExisting"/> is the only way to obtain one, and what it returns is
/// deliberately not castable back to a lease.
/// </para>
/// </remarks>
public interface IWorkerChannel {

	/// <summary>Gets the worker's operating-system process identifier.</summary>
	int ProcessId { get; }

	/// <summary>Gets the writable end of the worker's standard input.</summary>
	Stream StandardInput { get; }

	/// <summary>Gets the readable end of the worker's standard output.</summary>
	Stream StandardOutput { get; }

	/// <summary>Gets the readable end of the worker's standard error.</summary>
	Stream StandardError { get; }

	/// <summary>Gets a value indicating whether the worker has exited.</summary>
	/// <remarks>
	/// This is how a caller that reached an EXISTING worker learns that it is already gone. Reaching is
	/// not an aliveness assertion: the worker may exit between the moment the caller resolved it and the
	/// moment it reads this, and a reach that threw on a dead worker would only move that race somewhere
	/// less convenient to handle.
	/// </remarks>
	bool HasExited { get; }

	/// <summary>Gets the worker's exit code once it has exited, or <see langword="null"/> before that.</summary>
	int? ExitCode { get; }

	/// <summary>Waits for the worker to exit, without bounding it.</summary>
	/// <param name="cancellationToken">Stops waiting; does not stop the worker.</param>
	/// <returns>A task that completes when the worker exits or the wait is cancelled.</returns>
	Task WaitForExitAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reaches a worker that already exists, without going anywhere near admission.
/// </summary>
/// <remarks>
/// <para>
/// <b>A separate, deliberately narrow interface, because that is the whole mechanism.</b> The binding
/// rule of ADR §3.2c cannot be enforced by documentation on <see cref="IWorkerProcessSupervisor"/>: an
/// implementer who routes a status poll through
/// <see cref="IWorkerProcessSupervisor.SpawnContainedAsync"/> satisfies every word of "the poll reaches
/// the same worker" and ships the deadlock. What removes the possibility is a dependency that cannot
/// spawn: a component injected with <see cref="IWorkerReach"/> instead of the whole supervisor has no
/// method that acquires a slot, so routing a poll through admission stops being a mistake somebody can
/// make and becomes code that does not compile.
/// </para>
/// <para>
/// The supervisor implements this as well, because it is the type that issues leases; the narrowing is
/// for the CONSUMER side.
/// </para>
/// </remarks>
public interface IWorkerReach {

	/// <summary>
	/// Returns a non-owning channel to a worker this supervisor already admitted, taking no admission
	/// slot and never waiting.
	/// </summary>
	/// <param name="lease">A live lease this supervisor issued.</param>
	/// <returns>
	/// A channel that can converse with the worker but can neither end it nor return its slot.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="lease"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The lease was not issued by this supervisor.</exception>
	/// <remarks>
	/// Synchronous and allocation-cheap on purpose: there is nothing to wait for. The prototype measured
	/// 0.00–0.02 s <c>compile-status</c> poll latency, which is only reachable when reaching costs no
	/// admission — a poll that queued for a slot could not be that fast.
	/// </remarks>
	IWorkerChannel ReachExisting(IWorkerLease lease);
}

/// <summary>
/// A worker the caller currently holds: one slot of the concurrency cap, one contained process, and
/// one registry entry. Disposing the lease kills the worker if it is still running, drops its registry
/// entry and returns the slot.
/// </summary>
/// <remarks>
/// The conversation half of this contract lives on <see cref="IWorkerChannel"/> so that a caller which
/// only needs to TALK to the worker can be handed exactly that and nothing else — see the remarks
/// there for why owning and talking are separated.
/// </remarks>
public interface IWorkerLease : IWorkerChannel, IDisposable {

	/// <summary>
	/// Gets the moment the process was spawned. The budget is measured from here, so a call that
	/// waited behind the concurrency cap is not punished for waiting.
	/// </summary>
	DateTimeOffset SpawnedAtUtc { get; }

	/// <summary>Gets the budget this worker was spawned with.</summary>
	TimeSpan Budget { get; }

	/// <summary>
	/// Gets the moment the budget expires: <see cref="SpawnedAtUtc"/> + <see cref="Budget"/>.
	/// </summary>
	/// <remarks>
	/// Anchoring on spawn is a measured requirement, not a preference. At concurrency width 16 on a
	/// four-core Windows box a perfectly healthy call waited 16.9 s just to reach <c>initialize</c>
	/// (ADR §2.4); a budget measured from admission would have killed it for being queued, which is a
	/// failure mode this fix would otherwise have invented.
	/// </remarks>
	DateTimeOffset BudgetExpiresAtUtc { get; }
}

/// <summary>
/// A point-in-time account of what the supervisor is running. Plain counters: the accounting exists so
/// a caller (and a test) can state what happened, not to feed a metrics pipeline.
/// </summary>
/// <param name="ConcurrencyCap">
/// Maximum workers allowed to run at once, across BOTH admission pools. It is the total the two caps
/// below partition, never their sum plus something.
/// </param>
/// <param name="ActiveWorkers">Workers running right now, of either lifetime.</param>
/// <param name="QueuedRequests">
/// Callers waiting for a PER-CALL slot right now. A queued caller is admitted as soon as a slot frees;
/// it is refused only if it outlasts the queue-wait bound
/// (<see cref="WorkerQueueWaitExpiredException"/>). Sticky admission never appears here — it does not
/// queue at all, it is refused immediately (<see cref="WorkerStickyCapacityExceededException"/>).
/// </param>
/// <param name="PeakActiveWorkers">Highest <paramref name="ActiveWorkers"/> observed in this process.</param>
/// <param name="TotalSpawned">Workers spawned since the supervisor was created.</param>
/// <param name="TotalTerminated">Workers this supervisor had to kill (budget, cancellation or dispose).</param>
/// <param name="TotalStaleReaped">Workers of dead previous parents killed by <see cref="IWorkerProcessSupervisor.ReapStaleWorkers"/>.</param>
/// <param name="StickyConcurrencyCap">
/// Concurrent long operations this host supports; see
/// <see cref="IWorkerProcessSupervisor.StickyConcurrencyCap"/>.
/// </param>
/// <param name="PerCallConcurrencyCap">
/// Slots ordinary per-call work always has, which sticky work can never take.
/// </param>
/// <param name="ActiveStickyWorkers">
/// Sticky slots occupied right now. Reported separately because a host can be perfectly idle for
/// ordinary reads while refusing every new long operation, and one number cannot say that.
/// </param>
public sealed record WorkerSupervisorSnapshot(
	int ConcurrencyCap,
	int ActiveWorkers,
	int QueuedRequests,
	int PeakActiveWorkers,
	long TotalSpawned,
	long TotalTerminated,
	long TotalStaleReaped,
	int StickyConcurrencyCap,
	int PerCallConcurrencyCap,
	int ActiveStickyWorkers);

/// <summary>
/// Thrown when a call waited for a worker concurrency slot for longer than the supervisor's queue-wait
/// bound. Nothing was spawned, and no request reached Creatio.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the queue wait is bounded at all.</b> The cap is
/// <see cref="IWorkerProcessSupervisor.ConcurrencyCap"/> workers, so on a four-core host the fifth
/// concurrent call queues. An UNBOUNDED queue wait reproduces the exact signature this whole feature
/// exists to remove — a call that returns nothing, issues zero requests to Creatio, and does so for an
/// arbitrarily long time — with the one difference that it eventually clears. "Eventually" is not a
/// bound, and a client that never cancels waits for ever, so the wait is bounded and the expiry is
/// named.
/// </para>
/// <para>
/// <b>Deliberately neither <see cref="TimeoutException"/> nor
/// <see cref="OperationCanceledException"/>.</b> Both would be MISREAD: the first says the backend was
/// slow, when in fact clio never spoke to it; the second says the caller gave up, when the caller was
/// still waiting. This is a third thing — clio itself is saturated — and it is the only one of the
/// three whose remedy is to reduce concurrency rather than to retry harder or to blame the stand.
/// </para>
/// <para>
/// <b>It is not the budget.</b> The budget bounds a worker that IS running (measured from spawn, see
/// <see cref="IWorkerLease.BudgetExpiresAtUtc"/>); this bounds the wait BEFORE one exists. Keeping the
/// two apart is what lets a caller tell "the environment is slow" from "this clio host is full".
/// </para>
/// </remarks>
public sealed class WorkerQueueWaitExpiredException : Exception {

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkerQueueWaitExpiredException"/> class.
	/// </summary>
	/// <param name="waitEndured">How long this call actually waited before it was refused.</param>
	/// <param name="configuredBound">The queue-wait bound in force when it was refused.</param>
	/// <param name="concurrencyCap">The concurrency cap of the pool it was waiting on.</param>
	/// <param name="queueDepth">
	/// Callers waiting on that pool at the moment the wait expired, INCLUDING this one — so the minimum
	/// meaningful value is 1 and the number reads as "how many calls are stacked up", not "how many
	/// others".
	/// </param>
	public WorkerQueueWaitExpiredException(TimeSpan waitEndured, TimeSpan configuredBound,
		int concurrencyCap, int queueDepth)
		: base(BuildMessage(waitEndured, configuredBound, concurrencyCap, queueDepth)) {
		WaitEndured = waitEndured;
		ConfiguredBound = configuredBound;
		ConcurrencyCap = concurrencyCap;
		QueueDepth = queueDepth;
	}

	/// <summary>Gets how long this call actually waited before it was refused.</summary>
	public TimeSpan WaitEndured { get; }

	/// <summary>Gets the queue-wait bound in force when this call was refused.</summary>
	public TimeSpan ConfiguredBound { get; }

	/// <summary>Gets the concurrency cap of the pool this call was waiting on.</summary>
	public int ConcurrencyCap { get; }

	/// <summary>
	/// Gets the number of callers waiting on that pool when the wait expired, including this one.
	/// </summary>
	/// <remarks>
	/// Reported because it is the one number that separates "briefly busy" from "structurally
	/// saturated": a depth of 1 against a full cap is a burst, a depth several times the cap is a host
	/// that will not recover by waiting.
	/// </remarks>
	public int QueueDepth { get; }

	private static string BuildMessage(TimeSpan waitEndured, TimeSpan configuredBound, int concurrencyCap,
		int queueDepth) =>
		$"No MCP worker slot became available within {FormatSeconds(configuredBound)} s "
		+ $"(waited {FormatSeconds(waitEndured)} s): all {concurrencyCap} worker slot(s) were in use "
		+ $"and {queueDepth} call(s) were queued. The call was not executed and issued no request to "
		+ "Creatio.";

	// Invariant, because this text is read by an agent and compared across machines: a decimal comma
	// from a host locale would make the same condition look like two different numbers.
	private static string FormatSeconds(TimeSpan value) =>
		value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// Thrown when a long-lived (sticky) worker was asked for while every sticky slot was already held.
/// Nothing was spawned, nothing was queued, and no request reached Creatio.
/// </summary>
/// <remarks>
/// <para>
/// <b>Immediate, and that is the design rather than an optimisation.</b>
/// <see cref="IWorkerProcessSupervisor.StickyConcurrencyCap"/> is not a burst limit that clears in a
/// moment — it IS the number of concurrent long operations this host supports, and a long operation
/// holds its slot for minutes to an hour. Queueing behind one for the 60 s a per-call caller waits
/// would spend a minute of the client's patience to arrive at the same refusal with less information.
/// So the answer is given at once and it carries the limit, which is the number the caller (or the
/// operator reading its message) actually needs.
/// </para>
/// <para>
/// <b>Distinct from <see cref="WorkerQueueWaitExpiredException"/> on purpose.</b> That one reports a
/// wait that was endured and a bound that expired; both would be fiction here, and a caller told "no
/// slot became available within 60 s" after waiting zero seconds cannot act on the message. This says
/// something different and actionable: the host is already running as many long operations as it is
/// configured to run.
/// </para>
/// </remarks>
public sealed class WorkerStickyCapacityExceededException : Exception {

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkerStickyCapacityExceededException"/> class.
	/// </summary>
	/// <param name="stickyConcurrencyCap">Concurrent long operations this host supports.</param>
	/// <param name="totalConcurrencyCap">
	/// The total admission capacity the sticky cap is derived from, reported so the message names both
	/// the limit and the knob that moves it.
	/// </param>
	public WorkerStickyCapacityExceededException(int stickyConcurrencyCap, int totalConcurrencyCap)
		: base(BuildMessage(stickyConcurrencyCap, totalConcurrencyCap)) {
		StickyConcurrencyCap = stickyConcurrencyCap;
		TotalConcurrencyCap = totalConcurrencyCap;
	}

	/// <summary>Gets the number of concurrent long operations this host supports.</summary>
	public int StickyConcurrencyCap { get; }

	/// <summary>Gets the total admission capacity the sticky cap was derived from.</summary>
	public int TotalConcurrencyCap { get; }

	private static string BuildMessage(int stickyConcurrencyCap, int totalConcurrencyCap) =>
		stickyConcurrencyCap == 0
			? "This clio host supports 0 concurrent long operations: its total worker concurrency is "
				+ $"{totalConcurrencyCap}, which leaves no capacity that could be reserved for a long "
				+ "operation without starving ordinary calls. Raise "
				+ $"{WorkerProcessSupervisor.ConcurrencyCapOverrideEnvVar} to at least 2. The call was not "
				+ "executed and issued no request to Creatio."
			: $"This clio host supports {stickyConcurrencyCap} concurrent long operation(s) and all of them "
				+ "are in use. Wait for one to finish, or raise "
				+ $"{WorkerProcessSupervisor.ConcurrencyCapOverrideEnvVar} (currently {totalConcurrencyCap}). "
				+ "The call was not executed and issued no request to Creatio.";
}

/// <summary>
/// Spawns, contains, bounds and reaps the short-lived child processes that execute MCP tool calls.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this derives from <see cref="IProcessExecutor"/>.</b> Not for reuse — the existing executor
/// cannot be adapted to a worker: its standard input is written once and then closed
/// (<see cref="ProcessExecutor"/>), its non-redirecting fire-and-forget path exposes no streams at all,
/// and its kill is best effort by documented contract
/// (<see cref="ProcessExecutionResult.DescendantTerminationUncertain"/>), which is precisely the
/// guarantee this supervisor has to strengthen. The derivation exists because <c>CLIO004</c> exempts
/// only classes implementing an interface literally named <c>IProcessExecutor</c> in a <c>Clio</c>
/// namespace, and this feature must own raw process creation. The four inherited members are
/// forwarded to the ordinary executor so that no caller of the old contract changes behaviour.
/// </para>
/// <para>
/// <b>The supervisor — not the MCP SDK — owns child lifetime.</b> The SDK's stdio client transport can
/// spawn a server itself and documents that it manages the entire lifecycle of that process,
/// force-terminating it after its own shutdown timeout (5 s by default). A worker must not be created
/// that way: containment requires ownership of creation (ADR §2.4), the relay independently requires
/// ownership of the child's transport read loop (ADR rule 2, §3.2), and a deploy child that the SDK
/// terminated after 5 s would violate ADR rule 4, under which the parent waits for an authoritative
/// terminal stage. Two independent constraints converge on a raw spawn, so the transport is attached
/// to the streams this supervisor hands out and never allowed to create or kill the process.
/// </para>
/// </remarks>
public interface IWorkerProcessSupervisor : IProcessExecutor, IWorkerReach {

	/// <summary>
	/// Gets the maximum number of workers allowed to run at once, across both admission pools. Derived
	/// from <see cref="Environment.ProcessorCount"/> and overridable by the operator through
	/// <see cref="WorkerProcessSupervisor.ConcurrencyCapOverrideEnvVar"/>: wall time grows linearly past
	/// the core count, so a larger cap buys no throughput and only inflates per-call latency (ADR §2.4).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The cap is a shared, held resource, not a rate.</b> A slot is taken when the worker is spawned
	/// and returned only when the lease is disposed, so a worker whose LIFETIME outlives the answer it
	/// produced consumes one slot for its whole life, not for the duration of its response.
	/// </para>
	/// <para>
	/// <b>Which is why this total is PARTITIONED and not shared.</b> It is split into
	/// <see cref="StickyConcurrencyCap"/> and <see cref="PerCallConcurrencyCap"/>, and a worker draws
	/// from exactly one of them according to <see cref="WorkerSpawnRequest.Lifetime"/>. Ordinary reads
	/// therefore do NOT queue behind long-lived holders — the per-call floor is capacity sticky work can
	/// never take. Before the split, a four-core host was four long-lived holders away from queueing
	/// every other call, including the status polls of those same operations, which is hold-and-wait
	/// rather than slowness (ADR §3.2c).
	/// </para>
	/// </remarks>
	int ConcurrencyCap { get; }

	/// <summary>
	/// Gets the number of concurrent long operations this host supports: the cap of the pool that
	/// <see cref="WorkerLifetime.Sticky"/> workers are admitted from.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Strictly less than <see cref="ConcurrencyCap"/>, by derivation rather than by convention</b> —
	/// see <see cref="WorkerProcessSupervisor.DeriveStickyConcurrencyCap"/>. The remainder is
	/// <see cref="PerCallConcurrencyCap"/>, and it is a guaranteed floor: two pools whose caps left
	/// per-call work at zero would only have relabelled the exhaustion.
	/// </para>
	/// <para>
	/// This number is a product statement as much as a resource one, so it is published rather than kept
	/// private: it is what a caller is told when the next long operation is refused, and it can be
	/// <b>zero</b> on a host whose total capacity is one — a single slot cannot both carry a long
	/// operation and leave ordinary calls a floor. Raising
	/// <see cref="WorkerProcessSupervisor.ConcurrencyCapOverrideEnvVar"/> is the operator's answer to
	/// that.
	/// </para>
	/// </remarks>
	int StickyConcurrencyCap { get; }

	/// <summary>
	/// Gets the slots reserved for ordinary per-call work:
	/// <see cref="ConcurrencyCap"/> − <see cref="StickyConcurrencyCap"/>, never less than one while the
	/// total is at least one.
	/// </summary>
	int PerCallConcurrencyCap { get; }

	/// <summary>
	/// Waits for a slot, then spawns a worker inside its platform containment and registers it for
	/// stale cleanup.
	/// </summary>
	/// <param name="request">What to run and with what budget.</param>
	/// <param name="cancellationToken">
	/// Ends the wait for a slot early. A queued call is never DROPPED for being busy (AC-01) — it is
	/// admitted as soon as a slot frees — but the wait is bounded: see
	/// <see cref="WorkerQueueWaitExpiredException"/> for why an unbounded queue wait is the wedge in
	/// another shape.
	/// </param>
	/// <returns>The lease; dispose it to kill the worker and return the slot.</returns>
	/// <remarks>
	/// <b>This method always CREATES a worker; it never returns one that already exists.</b> That is the
	/// binding half of ADR §3.2c: admission governs creation only, so a caller looking for a worker it
	/// (or another call) already started must go through <see cref="IWorkerReach.ReachExisting"/>, which
	/// takes no slot. Routing such a call here makes it wait for capacity that the worker it is looking
	/// for is holding.
	/// </remarks>
	/// <exception cref="WorkerQueueWaitExpiredException">
	/// No per-call slot became available within the supervisor's queue-wait bound. Nothing was spawned
	/// and no request reached Creatio.
	/// </exception>
	/// <exception cref="WorkerStickyCapacityExceededException">
	/// The request asked for <see cref="WorkerLifetime.Sticky"/> and every sticky slot was held. Refused
	/// immediately rather than queued, because the cap is the number of concurrent long operations the
	/// host supports and waiting could only reach the same answer later.
	/// </exception>
	/// <exception cref="OperationCanceledException">
	/// <paramref name="cancellationToken"/> was cancelled while queued or while spawning.
	/// </exception>
	Task<IWorkerLease> SpawnContainedAsync(WorkerSpawnRequest request, CancellationToken cancellationToken);

	/// <summary>
	/// Waits for the worker to exit within its budget, killing it — and, through containment, its
	/// descendants — when the budget expires or the caller cancels.
	/// </summary>
	/// <param name="lease">The lease to bound.</param>
	/// <param name="cancellationToken">Caller cancellation; kills the worker.</param>
	/// <returns>How the run ended, with elapsed time measured from spawn.</returns>
	Task<WorkerRunResult> WaitWithinBudgetAsync(IWorkerLease lease, CancellationToken cancellationToken);

	/// <summary>Kills a worker and every descendant its containment covers.</summary>
	/// <param name="lease">The lease to terminate.</param>
	/// <returns>What was signalled, and whether descendants were covered.</returns>
	WorkerTerminationOutcome KillContained(IWorkerLease lease);

	/// <summary>
	/// Kills workers recorded by parents that are no longer running, at parent startup.
	/// </summary>
	/// <remarks>
	/// Every candidate is revalidated against the full identity triple
	/// (process id, start time, executable path) immediately before the kill, because process ids are
	/// reused and killing a stranger's process is its own defect (AC-02).
	/// </remarks>
	/// <returns>What was found, what was killed, and what was discarded as a stranger.</returns>
	StaleWorkerReapReport ReapStaleWorkers();

	/// <summary>Gets a point-in-time account of running, queued and historical workers.</summary>
	/// <returns>The snapshot.</returns>
	WorkerSupervisorSnapshot GetSnapshot();
}
