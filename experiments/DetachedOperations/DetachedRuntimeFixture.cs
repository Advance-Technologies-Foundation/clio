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
    public string StartDetached(IOperationLedger ledger, string target, string effectPath, int workMilliseconds,
        string outcome, CancellationToken cancellationToken) {
        IOperationLease lease = ledger.Begin(target, Version, this);

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
            }
            catch (OperationCanceledException) {
                lease.Complete(OperationState.Cancelled, Signature + "-cancelled");
            }
            catch (Exception error) {
                lease.Complete(OperationState.Failed, error.GetType().Name);
            }
        }, CancellationToken.None);

        return lease.Id;
    }
}
