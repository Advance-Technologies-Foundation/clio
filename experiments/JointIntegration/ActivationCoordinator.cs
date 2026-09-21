using Clio10.DetachedOperations;
using Clio10.DetachedOperations.Host;
using Clio10.SettingsVersioning;

namespace Clio10.JointIntegration;

/// <summary>
/// Owns the one rule that keeps the published pair and the settings store from drifting apart:
/// <b>every activation of either half republishes the pair.</b>
/// </summary>
/// <remarks>
/// <para>
/// Without it the selection goes stale in a way nothing detects. A settings-only activation moves the
/// store's current snapshot, the published selection still names the previous one, that snapshot is then
/// neither pinned nor held nor retained — so <see cref="SettingsStore.Cleanup"/> legitimately reclaims
/// it, and the next admission is registered under a snapshot that no longer exists. Measured as X7,
/// which drives the two halves directly and bypasses this class.
/// </para>
/// <para>
/// Adding "the published selection" as a fourth ownership reason for cleanup would also work and is
/// worse: it makes the settings store depend on the ledger's selection, and it leaves two places that
/// can disagree about what is current. Keeping them equal by construction needs no new ownership reason
/// at all — after any activation, pinned and selected are the same snapshot.
/// </para>
/// <para>
/// This class is deliberately not a framework. It has one method, it holds no state of its own, and it
/// exists because the rule has to live somewhere that owns both halves.
/// </para>
/// </remarks>
public sealed class ActivationCoordinator(OperationLedger ledger, SettingsStore settings) {
    /// <summary>
    /// Commits a runtime and a configuration candidate as one pair. Either half failing means nothing
    /// is published and the previous pair stays live.
    /// </summary>
    /// <param name="scope">Target scope.</param>
    /// <param name="runtimeVersion">The release to serve, already accepted by the host.</param>
    /// <param name="candidateId">A prepared settings candidate, or <see langword="null"/> to keep the current one.</param>
    /// <returns><see langword="true"/> when the pair was published.</returns>
    public bool TryCommitPair(string scope, string runtimeVersion, string? candidateId) {
        if (candidateId is not null) {
            // The settings half first: if it throws, nothing has been published and the live pair is
            // untouched. There is deliberately no compensating action here to get wrong.
            settings.Activate(scope, candidateId, settings.CurrentVersion(scope));
        }
        string current = settings.CurrentSnapshotId(scope);
        ActivationSelection selection = ledger.CurrentSelection(scope);
        return ledger.TryPublishSelection(scope, runtimeVersion, current, selection.Generation);
    }

    /// <summary>Republishes the pair after a settings-only change, keeping selected and pinned equal.</summary>
    public bool TryCommitSettingsOnly(string scope, string candidateId) =>
        TryCommitPair(scope, ledger.CurrentSelection(scope).RuntimeVersion, candidateId);
}
