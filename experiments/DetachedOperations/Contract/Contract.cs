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
public sealed record OperationRecord(
    string Id,
    string Target,
    DateTimeOffset StartedUtc,
    string RuntimeVersion,
    OperationState State,
    DateTimeOffset? FinishedUtc = null,
    string? Code = null);

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
    /// </remarks>
    bool IsQuiescent(string? target = null);
}

/// <summary>What a loaded runtime release must expose for this probe.</summary>
public interface IDetachedRuntime {
    /// <summary>Release identity.</summary>
    string Version { get; }

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
}
