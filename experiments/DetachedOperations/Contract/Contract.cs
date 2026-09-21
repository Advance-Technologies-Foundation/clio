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
}

/// <summary>Raised when an operation is started for a scope whose swap window is held.</summary>
public sealed class SwapWindowHeldException(string scope)
    : InvalidOperationException($"A swap window is held for '{scope}'; retry once it is released.") {
    /// <summary>The held scope — a target name, or an empty string for the whole process.</summary>
    public string Scope { get; } = scope;
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
}
