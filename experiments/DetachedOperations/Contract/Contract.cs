namespace Clio10.DetachedOperations;

/// <summary>Terminal and non-terminal states an owned operation can report.</summary>
/// <remarks>
/// <see cref="Unknown"/> exists because its absence is the defect this probe targets: a host that cannot
/// remember an operation must say so, never that nothing was started.
/// </remarks>
public enum OperationState {
    /// <summary>Still owned and executing.</summary>
    Running,
    /// <summary>Finished successfully.</summary>
    Succeeded,
    /// <summary>Finished with a failure the runtime classified.</summary>
    Failed,
    /// <summary>Finished because cancellation was requested.</summary>
    Cancelled,
    /// <summary>Evidence says it started; this host cannot establish the outcome.</summary>
    Unknown,
    /// <summary>No evidence this identifier was ever issued.</summary>
    NotFound
}

/// <summary>
/// Portable record of one operation. Deliberately data only — no delegates, no runtime objects, no
/// feature semantics — so it can outlive the runtime that produced it and be written to durable evidence.
/// </summary>
/// <param name="Id">Host-issued identifier handed to the caller.</param>
/// <param name="Target">
/// The scope this operation occupies — an environment or tenant key. Present so quiescence can be asked
/// per target rather than per process: a session driving several environments would almost never be
/// globally idle, so a global-only predicate would be correct and unusable
/// (raised by @vladimir-nikonov in discussion #1643).
/// </param>
/// <param name="StartedUtc">When the host accepted the operation.</param>
/// <param name="RuntimeVersion">The runtime release that owns execution, as a string so no runtime type crosses.</param>
/// <param name="State">Current state.</param>
/// <param name="FinishedUtc">When a terminal state was recorded, if it was.</param>
/// <param name="Code">Opaque runtime-supplied detail. The host never interprets it.</param>
/// <param name="ConfigurationSnapshot">
/// Which configuration snapshot this operation was ADMITTED under, as an opaque string. Clause 6 of the
/// shared contract: the lifetime side needs only that a snapshot has a portable identity, so a record
/// naming it outlives the release and the process. Everything about preparing, migrating or rolling back
/// a snapshot belongs to the settings lane (@vladimir-nikonov) and nothing here interprets this value.
/// </param>
public sealed record OperationRecord(
    string Id,
    string Target,
    DateTimeOffset StartedUtc,
    string RuntimeVersion,
    OperationState State,
    DateTimeOffset? FinishedUtc = null,
    string? Code = null,
    string? ConfigurationSnapshot = null);

/// <summary>
/// The right to keep executing, and the obligation to report a terminal state exactly once.
/// </summary>
/// <remarks>
/// Holding a lease is what keeps the owning runtime alive. Disposing it without reporting is itself a
/// defect, so the implementation records a terminal state on dispose rather than letting the record
/// sit at <see cref="OperationState.Running"/> forever.
/// </remarks>
public interface IOperationLease : IDisposable {
    /// <summary>The host-issued identifier.</summary>
    string Id { get; }
    /// <summary>Records the single terminal state for this operation.</summary>
    void Complete(OperationState state, string? code = null);
}

/// <summary>Host-owned operation ledger. Generic: it stores records, it does not interpret them.</summary>
public interface IOperationLedger {
    /// <summary>Accepts a new operation owned by <paramref name="owner"/>, which is retained until the lease ends.</summary>
    /// <param name="target">Environment or tenant key this operation occupies.</param>
    /// <param name="runtimeVersion">Release identity of the owning runtime.</param>
    /// <param name="owner">The object whose lifetime must outlast the operation; retained by reference only.</param>
    IOperationLease Begin(string target, string runtimeVersion, object owner);

    /// <summary>
    /// Accepts a new operation and records the configuration snapshot in force at admission.
    /// </summary>
    /// <param name="target">Environment or tenant key this operation occupies.</param>
    /// <param name="runtimeVersion">Release identity of the owning runtime.</param>
    /// <param name="owner">The object whose lifetime must outlast the operation.</param>
    /// <param name="configurationSnapshot">Opaque identity of the snapshot; the ledger never reads it.</param>
    /// <remarks>
    /// <b>A separate overload, deliberately, and this was measured rather than reasoned about.</b> The
    /// first attempt added an optional parameter to the three-argument method instead. That is source
    /// compatible and <b>binary incompatible</b>: every release already built against the previous
    /// contract died with
    /// <c>MissingMethodException: Method not found: IOperationLedger.Begin(String, String, Object)</c>
    /// the moment it started an operation. An already-shipped partner or release is exactly what this
    /// contract exists to keep working, so the old signature stays and the new capability is an addition.
    /// </remarks>
    IOperationLease Begin(string target, string runtimeVersion, object owner, string? configurationSnapshot);

    /// <summary>Answers for an identifier, including for operations this process did not start.</summary>
    OperationRecord Query(string id);

    /// <summary>Identifiers still owned and running in this process.</summary>
    IReadOnlyCollection<string> Running { get; }

    /// <summary>
    /// Whether nothing is still running — for one target, or for the whole process when
    /// <paramref name="target"/> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// This is the predicate a staged host update would trigger on. It is deliberately NOT derived from
    /// open requests: the measured failure this probe exists for had an idle transport and live work.
    /// <para>
    /// <b>Observing this is not enough to act on it</b> — see <see cref="TryEnterSwapWindow"/>.
    /// </para>
    /// </remarks>
    bool IsQuiescent(string? target = null);

    /// <summary>
    /// Takes quiescence and holds it, so a swap can act on what it observed.
    /// </summary>
    /// <param name="target">Scope to hold, or <see langword="null"/> to hold the whole process.</param>
    /// <returns>
    /// A handle that keeps the scope quiescent until disposed, or <see langword="null"/> when work is
    /// already running and the swap must not proceed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Why a boolean is not sufficient.</b> Reading <see cref="IsQuiescent"/> and then swapping is
    /// check-then-act: an operation can begin in the gap, and the swap then destroys work that started
    /// after the check said it was safe. Raised by @vladimir-nikonov in discussion #1643 while scoping
    /// the composition probe; case A5 measures both halves.
    /// </para>
    /// <para>
    /// <b>The trade-off is explicit.</b> While a window is held, <see cref="Begin"/> for that scope is
    /// refused rather than queued, so a caller sees a clear, retryable refusal instead of a hidden stall.
    /// A production design may prefer to block or to queue; this probe refuses because a refusal is
    /// observable and a stall is not.
    /// </para>
    /// </remarks>
    IDisposable? TryEnterSwapWindow(string? target = null);

    /// <summary>
    /// Closes a scope to NEW work without requiring it to be idle first, so in-flight work can drain
    /// under the reservation.
    /// </summary>
    /// <param name="target">Scope to close, or <see langword="null"/> for the whole process.</param>
    /// <returns>A handle held until disposed, or <see langword="null"/> when the scope is already held.</returns>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> <see cref="TryEnterSwapWindow"/> demands quiescence before it grants
    /// anything, so an updater polling it against a continuously busy target can be starved forever —
    /// the deferral policy then stops being "the update waits" and becomes "the update never happens".
    /// Reserving first and draining second removes that possibility: nothing new is admitted, so the
    /// scope necessarily empties.
    /// </para>
    /// <para>
    /// <b>The cost is real and belongs to the caller.</b> A reservation refuses legitimate work for the
    /// whole drain, so a long-running operation delays every new call on that scope. That is a worse
    /// failure than a deferred update if the drain is unbounded, which is why a caller must bound it and
    /// release the reservation when the bound expires.
    /// </para>
    /// </remarks>
    IDisposable? TryReserveAdmission(string? target = null);

    /// <summary>
    /// Scopes whose evidence storage failed. Their operations' outcomes are still authoritative; what is
    /// degraded is the host's ability to record them.
    /// </summary>
    /// <remarks>
    /// <b>Execution outcome and evidence health are different axes</b> (raised by @kirillkrylov in
    /// discussion #1643). Successful work must not be reported as failed or retryable because storage
    /// failed. So a failed evidence write publishes the true terminal state, marks the scope degraded,
    /// and refuses automatic retirement — it does not rewrite the outcome and it never replays anything.
    /// Clearing a degraded scope is an operator decision, deliberately absent here.
    /// </remarks>
    IReadOnlyCollection<string> DegradedScopes { get; }

    /// <summary>
    /// Operations whose owner cannot be asked whether it is still there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A capability report, <b>not a verdict</b> (@kirillkrylov's scoping). An in-process owner that
    /// legitimately ends at <see cref="IDisposable.Dispose"/> appears here and is perfectly correct:
    /// something will dispose it. Only the host knows which of its owners were supposed to be
    /// liveness-capable, so the ledger reports the capability and takes no position on whether its
    /// absence is a defect.
    /// </para>
    /// <para>
    /// It exists because the absence is otherwise invisible. Orphan resolution skips such an owner
    /// silently, so a cross-process owner that should have been wrapped simply stays
    /// <see cref="OperationState.Running"/> and keeps retaining, and the only symptom is a drain that
    /// never finishes. Found by @vladimir-nikonov, who wired the correction into his own harness and
    /// observed that passing a bare <c>Process</c> changed nothing until it was wrapped.
    /// </para>
    /// </remarks>
    IReadOnlyCollection<string> OwnersWithoutLiveness { get; }

    /// <summary>
    /// Configuration snapshots held by a retained operation. <b>One input to cleanup, never the whole
    /// answer.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// A snapshot appears here exactly while some operation admitted under it is still retained, derived
    /// from retention rather than counted beside it, so it cannot drift from ownership the way a parallel
    /// count would. Orphan resolution releases the reference with the retention, so a lost owner does not
    /// pin a snapshot forever — the same failure shape as H3, in the settings lane.
    /// </para>
    /// <para>
    /// <b>Deleting everything absent from this set destroys live configuration</b> (@kirillkrylov). A
    /// snapshot with no running operation may still be the current one for the NEXT admission, a prepared
    /// activation candidate, or the retained rollback target — none of which this ledger knows about.
    /// K4 measures the gap rather than describing it. The settings owner combines those ownership reasons
    /// with this one; this property answers a single question and nothing more.
    /// </para>
    /// </remarks>
    IReadOnlyCollection<string> OperationHeldSnapshots { get; }

    /// <summary>
    /// Operations whose outcome is known in memory but is NOT on disk.
    /// </summary>
    /// <remarks>
    /// These are the only records that would be lost if the evidence owner were replaced. Clearing a
    /// degraded mark does not shorten this list — repairing or explicitly abandoning them does.
    /// </remarks>
    IReadOnlyCollection<string> UnpersistedOperations { get; }
}

/// <summary>Outcome of an attempt to make un-persisted records durable.</summary>
/// <param name="Repaired">How many records were written to durable evidence.</param>
/// <param name="Remaining">How many are still memory-only.</param>
/// <param name="ScopeCleared">Whether the scope's degraded mark was lifted as a result.</param>
public sealed record RepairResult(int Repaired, int Remaining, bool ScopeCleared);

/// <summary>Raised when an operation is started for a scope whose swap window is held.</summary>
public sealed class SwapWindowHeldException(string scope)
    : InvalidOperationException($"A swap window is held for '{scope}'; retry once it is released.") {
    /// <summary>The held scope — a target name, or an empty string for the whole process.</summary>
    public string Scope { get; } = scope;
}

/// <summary>
/// An owner whose continued existence can be checked, rather than one that promises to release itself.
/// </summary>
/// <remarks>
/// <para>
/// Ownership normally ends when the lease is disposed. That silently assumes the owner is an in-process
/// object which something will eventually dispose. When the owner is a separate process, a killed process
/// disposes nothing: the lease stays open and the ledger keeps answering <see cref="OperationState.Running"/>
/// for work that has no process. Measured on @vladimir-nikonov's MCP host in discussion #1643 — eight
/// seconds of polling for five seconds of work, `Running` every time, forever.
/// </para>
/// <para>
/// So a cross-process owner is asked whether it is still there. An owner that is gone cannot report an
/// outcome and cannot perform cleanup, so its operations resolve to <see cref="OperationState.Unknown"/>
/// and stop retaining anything.
/// </para>
/// </remarks>
public interface IOwnerLiveness {
    /// <summary>Whether this owner can still report an outcome.</summary>
    bool IsAlive { get; }
}

/// <summary>
/// A workflow supplied by a third party, composed from vendor capability it does not reference.
/// </summary>
/// <remarks>
/// A partner assembly references this contract and nothing else of ours: not a vendor release, not the
/// host. It receives the runtime it should compose and returns portable data.
/// </remarks>
public interface IPartnerWorkflow {
    /// <summary>Partner identity, for provenance in the result.</summary>
    string Name { get; }

    /// <summary>Composes vendor capability and returns portable data only.</summary>
    /// <param name="runtime">The vendor release this invocation is pinned to.</param>
    /// <returns>An outcome describing what ran, as strings.</returns>
    (string Partner, string RuntimeVersion, string Outcome) Compose(IDetachedRuntime runtime);
}

/// <summary>What a loaded runtime release must expose for this probe.</summary>
public interface IDetachedRuntime {
    /// <summary>Release identity.</summary>
    string Version { get; }

    /// <summary>
    /// The contract generation this release was built against. The host refuses a release whose
    /// generation it does not support, before activation and without disturbing what is running.
    /// </summary>
    int ContractVersion { get; }

    /// <summary>
    /// Starts work that outlives this call and returns immediately, exactly like a tool that hits its
    /// response deadline. The returned id is the caller's only handle.
    /// </summary>
    /// <param name="ledger">Host ledger; the runtime takes a lease and reports its own terminal state.</param>
    /// <param name="target">Environment or tenant key this operation occupies.</param>
    /// <param name="effectPath">File the operation appends one line to, so the effect can be counted.</param>
    /// <param name="workMilliseconds">How long the detached work runs.</param>
    /// <param name="outcome">Requested outcome: succeed, fail, or wait for cancellation.</param>
    /// <param name="cancellationToken">Cancels the detached work.</param>
    string StartDetached(IOperationLedger ledger, string target, string effectPath, int workMilliseconds,
        string outcome, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a value whose TYPE is declared by this release, handed back as <see cref="object"/>.
    /// </summary>
    /// <remarks>
    /// Models <c>OperationResult.Payload</c>, which is <see cref="object"/> and carries runtime-defined
    /// DTOs back to callers unchanged. Raised by @kirillkrylov in discussion #1643: a caller retaining
    /// such a value also retains the release that defined its type, so terminal status is not sufficient
    /// for reclamation. Case O1 measures it.
    /// </remarks>
    object CreateRuntimeDefinedResult();

    /// <summary>Throws an exception whose TYPE is declared by this release.</summary>
    /// <remarks>An escaping exception is a returned value with extra steps: catching it retains the release.</remarks>
    void ThrowRuntimeDefinedError();

    /// <summary>Does the same work and reports failure as portable data instead of a runtime type.</summary>
    /// <returns>An error code and message, both <see cref="string"/>.</returns>
    (string Code, string Message) TryRuntimeDefinedError();

    /// <summary>Returns a delegate whose target lives in this release.</summary>
    object CreateRuntimeDefinedCallback();

    /// <summary>
    /// Accepts a HOST delegate, invokes it, and must not retain it once the call returns.
    /// </summary>
    /// <param name="report">Host-owned progress sink.</param>
    /// <param name="steps">How many times to report.</param>
    void ReportProgressTo(Action<string> report, int steps);

    /// <summary>Whether this release is still holding a host delegate from a previous call.</summary>
    bool HoldsHostCallback { get; }
}
