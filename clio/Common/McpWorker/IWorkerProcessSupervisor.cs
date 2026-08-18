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
/// One request to run a worker process.
/// </summary>
public sealed record WorkerSpawnRequest {

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
/// A worker the caller currently holds: one slot of the concurrency cap, one contained process, and
/// one registry entry. Disposing the lease kills the worker if it is still running, drops its registry
/// entry and returns the slot.
/// </summary>
public interface IWorkerLease : IDisposable {

	/// <summary>Gets the worker's operating-system process identifier.</summary>
	int ProcessId { get; }

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

	/// <summary>Gets the writable end of the worker's standard input.</summary>
	Stream StandardInput { get; }

	/// <summary>Gets the readable end of the worker's standard output.</summary>
	Stream StandardOutput { get; }

	/// <summary>Gets the readable end of the worker's standard error.</summary>
	Stream StandardError { get; }

	/// <summary>Gets a value indicating whether the worker has exited.</summary>
	bool HasExited { get; }

	/// <summary>Gets the worker's exit code once it has exited, or <see langword="null"/> before that.</summary>
	int? ExitCode { get; }

	/// <summary>Waits for the worker to exit, without bounding it.</summary>
	/// <param name="cancellationToken">Stops waiting; does not stop the worker.</param>
	/// <returns>A task that completes when the worker exits or the wait is cancelled.</returns>
	Task WaitForExitAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A point-in-time account of what the supervisor is running. Plain counters: the accounting exists so
/// a caller (and a test) can state what happened, not to feed a metrics pipeline.
/// </summary>
/// <param name="ConcurrencyCap">Maximum workers allowed to run at once.</param>
/// <param name="ActiveWorkers">Workers running right now.</param>
/// <param name="QueuedRequests">
/// Callers waiting for a slot right now. A queued caller is admitted as soon as a slot frees; it is
/// refused only if it outlasts the queue-wait bound (<see cref="WorkerQueueWaitExpiredException"/>).
/// </param>
/// <param name="PeakActiveWorkers">Highest <paramref name="ActiveWorkers"/> observed in this process.</param>
/// <param name="TotalSpawned">Workers spawned since the supervisor was created.</param>
/// <param name="TotalTerminated">Workers this supervisor had to kill (budget, cancellation or dispose).</param>
/// <param name="TotalStaleReaped">Workers of dead previous parents killed by <see cref="IWorkerProcessSupervisor.ReapStaleWorkers"/>.</param>
public sealed record WorkerSupervisorSnapshot(
	int ConcurrencyCap,
	int ActiveWorkers,
	int QueuedRequests,
	int PeakActiveWorkers,
	long TotalSpawned,
	long TotalTerminated,
	long TotalStaleReaped);

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
public interface IWorkerProcessSupervisor : IProcessExecutor {

	/// <summary>
	/// Gets the maximum number of workers allowed to run at once. Derived from
	/// <see cref="Environment.ProcessorCount"/>: wall time grows linearly past the core count, so a
	/// larger cap buys no throughput and only inflates per-call latency (ADR §2.4).
	/// </summary>
	/// <remarks>
	/// <b>The cap is a shared, held resource, not a rate.</b> A slot is taken when the worker is spawned
	/// and returned only when the lease is disposed, so a worker whose LIFETIME outlives the answer it
	/// produced consumes one slot for its whole life, not for the duration of its response. On a
	/// four-core host that is four holders away from a fully occupied cap, and everything else queues
	/// behind them — bounded by
	/// <see cref="WorkerProcessSupervisor.DefaultQueueWaitBound"/> rather than for ever, but queued.
	/// This is a property of the design today, for any long call; it is stated here because a lifetime
	/// longer than one response (a worker kept alive across calls) makes it the ordinary case rather
	/// than the slow one.
	/// </remarks>
	int ConcurrencyCap { get; }

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
	/// <exception cref="WorkerQueueWaitExpiredException">
	/// No slot became available within the supervisor's queue-wait bound. Nothing was spawned and no
	/// request reached Creatio.
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
