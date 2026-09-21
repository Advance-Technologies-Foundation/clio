namespace Clio10.DetachedOperationsFixture;

using Clio10.DetachedOperations;

/// <summary>
/// One workflow module compiled as two separate releases. The detached work deliberately closes over
/// <c>this</c>, so the operation genuinely keeps its originating release alive rather than merely
/// recording which one started it.
/// </summary>
public sealed class DetachedRuntime : IDetachedRuntime {
    /// <inheritdoc />
    public string Version { get; } = typeof(DetachedRuntime).Assembly.GetName().Version!.ToString();

    /// <summary>The line this release writes, so the effect proves which release executed it.</summary>
    private string Signature =>
#if RELEASE_V2
        "v2-executed-by-" + Version;
#else
        "v1-executed-by-" + Version;
#endif

    /// <inheritdoc />
    public object CreateRuntimeDefinedResult() => new ReleasePayload(Version);

    /// <summary>A DTO whose type lives in this release, exactly like a partner workflow's own result.</summary>
    public sealed record ReleasePayload(string ProducedBy);

    /// <summary>An exception type declared by this release. Catching one retains the release.</summary>
    public sealed class ReleaseFault(string producedBy) : Exception("release fault from " + producedBy) {
        /// <summary>Which release raised it.</summary>
        public string ProducedBy { get; } = producedBy;
    }

    private Action<string>? _retainedHostCallback;

    /// <inheritdoc />
    public bool HoldsHostCallback => _retainedHostCallback is not null;

    /// <inheritdoc />
    public void ThrowRuntimeDefinedError() => throw new ReleaseFault(Version);

    /// <inheritdoc />
    public (string Code, string Message) TryRuntimeDefinedError() =>
        ("release-fault", "release fault from " + Version);

    /// <inheritdoc />
    public object CreateRuntimeDefinedCallback() {
        // The delegate's target is this instance, so holding it holds the release.
        return new Func<string>(() => "callback from " + Version);
    }

    /// <inheritdoc />
    public void ReportProgressTo(Action<string> report, int steps) {
        for (int step = 1; step <= steps; step++) {
            report($"{Version} step {step}/{steps}");
        }
        // Deliberately NOT stored. Retaining a host delegate would tie the host's object graph to this
        // release for as long as the release lives, which is the mirror of the escaped-value problem.
        _retainedHostCallback = null;
    }

    /// <inheritdoc />
    public string StartDetached(IOperationLedger ledger, string target, string effectPath, int workMilliseconds,
        string outcome, CancellationToken cancellationToken) {
        IOperationLease lease = ledger.Begin(target, Version, this);
        bool holdCleanup = outcome == "cleanup";

        // Detached exactly like an over-deadline tool: the caller gets the id now, the work continues.
        // CancellationToken.None on the task itself; cancellation is observed inside, so the operation
        // always reaches a terminal state instead of vanishing.
        _ = Task.Run(async () => {
            try {
                await Task.Delay(workMilliseconds, cancellationToken).ConfigureAwait(false);
                if (outcome == "fail") {
                    lease.Complete(OperationState.Failed, Signature + "-failed");
                    return;
                }
                if (outcome == "hang") {
                    // Never terminates on its own: models work that a bounded drain cannot outwait.
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                if (outcome == "partial") {
                    // Models a multi-step operation that writes its first artefact and then fails: the
                    // artefact a naive presence check looks for exists, and the operation did not finish.
                    await File.AppendAllTextAsync(effectPath, Signature + "-part1" + Environment.NewLine,
                        CancellationToken.None).ConfigureAwait(false);
                    lease.Complete(OperationState.Failed, Signature + "-partial");
                    return;
                }
                // The single external effect. Written once, after the work, by the owning release.
                await File.AppendAllTextAsync(effectPath, Signature + Environment.NewLine, CancellationToken.None)
                    .ConfigureAwait(false);
                lease.Complete(OperationState.Succeeded, Signature);
                if (holdCleanup) {
                    // Owned cleanup after the outcome is known: the lease is deliberately still held,
                    // so the release must not be retired yet.
                    await Task.Delay(1500, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) {
                lease.Complete(OperationState.Cancelled, Signature + "-cancelled");
            }
            catch (Exception error) {
                lease.Complete(OperationState.Failed, error.GetType().Name);
            }
            finally {
                lease.Dispose();                     // ownership ends here, never at Complete
            }
        }, CancellationToken.None);

        return lease.Id;
    }
}
