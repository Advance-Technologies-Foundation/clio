using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Clio10.DetachedOperations;
using Clio10.DetachedOperations.Host;

namespace Clio10.SettingsVersioning;

/// <summary>
/// Portable settings for one scope at one version. Deliberately the only surface a runtime or an
/// evidence file ever sees for non-secret configuration -- see <c>CredentialStore</c>, and its own
/// remarks, for exactly what that separation does and does not guarantee: it establishes the intended
/// path never carries a secret, not that <see cref="Values"/> structurally rejects an arbitrary string a
/// caller chose to put there.
/// </summary>
/// <remarks>
/// <see cref="Values"/> is <see cref="ImmutableDictionary{TKey,TValue}"/>, not merely typed as
/// <c>IReadOnlyDictionary</c>: kirillkrylov's finding was that an interface-only guarantee is not a
/// guarantee -- <c>IReadOnlyDictionary&lt;K,V&gt;</c> is a view, and a caller who casts a
/// <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>-backed instance to
/// <c>IDictionary&lt;K,V&gt;</c> can still mutate it, whether that cast happens on the value
/// <see cref="SettingsStore.Prepare"/> was originally handed, on the value <see cref="SettingsStore.Values"/>
/// later returns, or on the value a <see cref="SettingsStore.Migrate"/> transform receives. An
/// <c>ImmutableDictionary</c> instance rejects every mutating call through any interface it satisfies --
/// covered for all three aliases by <c>Program.T11</c> and <c>Program.T13</c>.
/// </remarks>
public sealed record SettingsSnapshot(string Id, string Scope, int Version, ImmutableDictionary<string, string> Values);

/// <summary>
/// Result of one <see cref="SettingsStore.Cleanup"/> pass.
/// </summary>
/// <param name="Reclaimed">Snapshots actually deleted this pass.</param>
/// <param name="HeldByOwnerWithoutLiveness">
/// Snapshots held by an operation whose owner the ledger reports in
/// <see cref="IOperationLedger.OwnersWithoutLiveness"/>. A DIAGNOSTIC, not a verdict --
/// <see cref="IOperationLedger.OwnersWithoutLiveness"/> lists every owner the ledger structurally cannot
/// ask, which includes ordinary in-process owners whose lifetime legitimately ends at <c>Dispose</c> and
/// were never going to need liveness resolution in the first place (kirillkrylov's narrowing of
/// Alexandr-Kravchuk's X3: absence of liveness is not proof an owner is dead, and this field must not be
/// read as one). What this field fixes is narrower and purely a visibility gap: before it existed,
/// <see cref="Cleanup"/> returned an empty reclaim list identically whether a snapshot was legitimately
/// still in use or pinned by an owner that will never report itself gone -- <see cref="Reclaimed"/> alone
/// could not tell those apart. This field makes the second case visible for a human or an operator policy
/// to look at; it changes nothing about what <see cref="Cleanup"/> reclaims, and this store applies no
/// automatic remediation from it.
/// </param>
public sealed record CleanupResult(
    IReadOnlyCollection<string> Reclaimed,
    IReadOnlyCollection<string> HeldByOwnerWithoutLiveness);

/// <summary>Raised when an edit or rollback is based on a revision that is no longer current.</summary>
/// <remarks>
/// The same check backs both an edit race and a rollback racing a newer legitimate edit -- kirillkrylov's
/// requirement that rollback "must not silently overwrite a newer legitimate settings edit" is not a
/// second mechanism, it is this one applied to the operation that happens to move the pointer backward.
/// </remarks>
public sealed class ConcurrencyConflictException(string scope, int expectedBaseRevision, int actualRevision)
    : InvalidOperationException(
        $"scope '{scope}': expected base revision {expectedBaseRevision}, current is {actualRevision}") {
    public string Scope { get; } = scope;
    public int ExpectedBaseRevision { get; } = expectedBaseRevision;
    public int ActualRevision { get; } = actualRevision;
}

/// <summary>
/// Versioned settings snapshots layered over the shared <see cref="OperationLedger"/>'s
/// <c>ConfigurationSnapshot</c> seam. Deliberately narrow: proving V1/V2 coexistence, failed-migration
/// safety, explicit-conflict edits and ledger-consistent cleanup -- not a general settings framework, and
/// deliberately not MCP-shaped. Nothing here needs a transport: <c>ConfigurationSnapshot</c> is an opaque
/// string threaded through <see cref="OperationLedger.Begin(string,string,object,string)"/>, and every
/// case below exercises that directly. Real-MCP-client integration belongs to the host-activation
/// deliverable, not this one.
/// <para>
/// <b>Concurrency, per kirillkrylov's source-level review:</b> every multi-step sequence that reads
/// state, decides, and then writes -- <see cref="Prepare"/>, <see cref="Rollback"/>, <see cref="Admit"/>,
/// <see cref="RetainForRollback"/>, <see cref="Cleanup"/> -- is serialized under one <see cref="_gate"/>.
/// Smallest supported policy: a single coarse lock, not per-scope or lock-free structures, because this
/// is a probe proving correctness properties, not a throughput-sensitive store. The scope's concurrency
/// revision (<see cref="CurrentVersion"/>) is tracked separately from a snapshot's own
/// <see cref="SettingsSnapshot.Version"/> and is monotonic per scope, including across
/// <see cref="Rollback"/> -- rollback moves the pointer to old DATA without reusing the old snapshot's
/// revision NUMBER, which would otherwise let a stale caller's expectation coincidentally match again
/// (an ABA hazard kirillkrylov named directly).
/// </para>
/// </summary>
public sealed class SettingsStore {
    private readonly OperationLedger _ledger;
    private readonly string _evidencePath;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, SettingsSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _activeSnapshotId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _scopeRevision = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _rollbackRetained = new(StringComparer.Ordinal);
    private int _nextId;

    /// <summary>Test-only: makes <see cref="Migrate"/> fail after reading the source, before anything is written.</summary>
    public bool FailMigrationForTests { get; set; }

    /// <summary>
    /// Test-only: disables the revision check in <see cref="Prepare"/> and <see cref="Rollback"/>, so a
    /// stale write silently overwrites instead of being refused. Exists to observe the conflict-detection
    /// claim fail, not just to assert it would -- see <c>Program.T3</c>.
    /// </summary>
    public bool SkipConcurrencyCheckForTests { get; set; }

    /// <summary>
    /// Test-only: makes <see cref="Cleanup"/> ignore <see cref="IOperationLedger.SelectedSnapshots"/>,
    /// reproducing the state before that reason existed. Exists to observe the X8 handoff-window failure
    /// fail, not just to assert the fix would -- see <c>Program.T17</c>.
    /// </summary>
    public bool SkipSelectedSnapshotsProtectionForTests { get; set; }

    /// <summary>
    /// Test-only: invoked synchronously inside <see cref="Prepare"/>'s critical section, after the
    /// revision check passes and before anything is written, still holding <see cref="_gate"/>. Lets a
    /// regression test force a deterministic interleaving window instead of relying on timing.
    /// </summary>
    public Action? OnPreparePassedCheckForTests { get; set; }

    /// <summary>
    /// Test-only: invoked synchronously inside <see cref="Activate"/>'s critical section, after the
    /// revision check passes and before anything is written, still holding <see cref="_gate"/>. This is
    /// where <see cref="PrepareAndActivate"/>'s actual exclusivity lives -- <see cref="Prepare"/> alone
    /// does not reserve anything, so a deterministic-handshake test for "two concurrent callers can't both
    /// win" must pause here, not inside <see cref="Prepare"/> -- see <c>Program.T9</c>.
    /// </summary>
    public Action? OnActivatePassedCheckForTests { get; set; }


    public SettingsStore(OperationLedger ledger, string evidencePath) {
        _ledger = ledger;
        _evidencePath = evidencePath;
    }

    public string CurrentSnapshotId(string scope) =>
        _activeSnapshotId.TryGetValue(scope, out string? id)
            ? id
            : throw new KeyNotFoundException($"no snapshot activated for scope '{scope}'");

    public int CurrentVersion(string scope) => _scopeRevision.GetValueOrDefault(scope, 0);

    public ImmutableDictionary<string, string> Values(string snapshotId) => _snapshots[snapshotId].Values;

    private readonly ConcurrentDictionary<string, byte> _pendingCandidates = new(StringComparer.Ordinal);

    /// <summary>
    /// Builds and persists a new snapshot for <paramref name="scope"/> without activating it -- Alexandr-Kravchuk's
    /// X5: a paired activation (a runtime half and a settings half that must commit together or not at
    /// all) needs its settings half preparable ahead of the runtime attempt, and committed only if that
    /// attempt actually succeeds. <see cref="Prepare"/> alone used to activate immediately, which is
    /// exactly what let a refused runtime release leave settings pointed at a pair that was never
    /// committed. The candidate is protected from <see cref="Cleanup"/> while pending -- see
    /// <see cref="Activate"/> and <see cref="AbandonCandidate"/> for how that window ends.
    /// <paramref name="expectedBaseRevision"/> is validated against what the candidate is built on, not
    /// reserved: multiple candidates can be prepared against the same base without conflicting each other,
    /// since only <see cref="Activate"/> is exclusive. <paramref name="values"/> is frozen into an
    /// <see cref="ImmutableDictionary{TKey,TValue}"/> before being stored -- not merely copied into another
    /// mutable dictionary typed as read-only, which a cast back to <c>IDictionary</c> would still defeat.
    /// </summary>
    public SettingsSnapshot Prepare(string scope, IReadOnlyDictionary<string, string> values, int expectedBaseRevision) {
        lock (_gate) {
            int current = CurrentVersion(scope);
            if (!SkipConcurrencyCheckForTests && current != expectedBaseRevision)
                throw new ConcurrencyConflictException(scope, expectedBaseRevision, current);
            OnPreparePassedCheckForTests?.Invoke();
            ImmutableDictionary<string, string> frozen = values.ToImmutableDictionary(StringComparer.Ordinal);
            var snapshot = new SettingsSnapshot($"cfg-{Interlocked.Increment(ref _nextId)}", scope, current + 1, frozen);
            Persist("prepare", snapshot);
            _snapshots[snapshot.Id] = snapshot;
            _pendingCandidates[snapshot.Id] = 0;
            return snapshot;
        }
    }

    /// <summary>
    /// Commits a previously <see cref="Prepare"/>d candidate as <paramref name="scope"/>'s current
    /// snapshot. Same explicit-conflict semantics as <see cref="Rollback"/>: a stale
    /// <paramref name="expectedCurrentRevision"/> is refused, not silently overridden, and the scope's
    /// revision counter moves strictly forward regardless of the candidate's own informational
    /// <see cref="SettingsSnapshot.Version"/>. The caller orders a paired activation as: prepare the
    /// settings candidate, activate the runtime half, then call this only if that succeeded -- a refused
    /// runtime never reaches this call, and the candidate is reclaimed by <see cref="Cleanup"/> instead of
    /// silently becoming current. The other direction matters just as much (kirillkrylov: "a settings
    /// commit failure after runtime activation leaves the same split"): the checks run and evidence is
    /// written BEFORE any of <see cref="_activeSnapshotId"/>, <see cref="_scopeRevision"/> or
    /// <see cref="_pendingCandidates"/> changes, so a write fault here (<see cref="FailNextActivatePersistForTests"/>)
    /// throws with the previous selection still active and the candidate still pending -- never a
    /// half-published state. What this method cannot do alone is stop the CALLER from having already
    /// activated the runtime half before calling this; composing the two into one coordination boundary is
    /// the joint-proof side of this seam, not this store's.
    /// </summary>
    public void Activate(string scope, string candidateId, int expectedCurrentRevision) {
        lock (_gate) {
            int current = CurrentVersion(scope);
            if (!SkipConcurrencyCheckForTests && current != expectedCurrentRevision)
                throw new ConcurrencyConflictException(scope, expectedCurrentRevision, current);
            OnActivatePassedCheckForTests?.Invoke();
            SettingsSnapshot candidate = _snapshots[candidateId];
            if (!string.Equals(candidate.Scope, scope, StringComparison.Ordinal))
                throw new InvalidOperationException($"snapshot '{candidateId}' does not belong to scope '{scope}'");
            Persist("activate", candidate);
            _activeSnapshotId[scope] = candidateId;
            _scopeRevision[scope] = current + 1;
            _pendingCandidates.TryRemove(candidateId, out _);
        }
    }

    /// <summary>
    /// Explicit release for a prepared candidate that will never be activated -- the "runtime activation
    /// failed, do not commit the paired settings change" path. Only lifts the pending-cleanup protection;
    /// the candidate itself is reclaimed on the next <see cref="Cleanup"/> pass, same as every other
    /// explicit release in this store (<see cref="ReleaseRollbackRetention"/>).
    /// </summary>
    public void AbandonCandidate(string candidateId) {
        lock (_gate) { _pendingCandidates.TryRemove(candidateId, out _); }
    }

    /// <summary>
    /// Prepares and immediately activates a snapshot in one step -- the ordinary case, for callers that
    /// don't need <see cref="Prepare"/>/<see cref="Activate"/>'s paired-activation split. Two separate lock
    /// acquisitions, not one critical section spanning both calls, which is safe rather than merely
    /// convenient: <see cref="Activate"/> re-validates <paramref name="expectedBaseRevision"/> against
    /// whatever is current at that moment, so a caller that races with this one gets a correct
    /// <see cref="ConcurrencyConflictException"/> instead of either call silently winning.
    /// </summary>
    public SettingsSnapshot PrepareAndActivate(string scope, IReadOnlyDictionary<string, string> values, int expectedBaseRevision) {
        SettingsSnapshot candidate = Prepare(scope, values, expectedBaseRevision);
        Activate(scope, candidate.Id, expectedBaseRevision);
        return candidate;
    }

    /// <summary>
    /// Migrates <paramref name="scope"/>'s current values through <paramref name="transform"/>. The
    /// transformed result is built entirely in memory before <see cref="PrepareAndActivate"/> ever
    /// persists anything, so a transform that throws -- or <see cref="FailMigrationForTests"/> -- leaves
    /// the prior snapshot exactly as it was, and the freeze happens independently, so even a transform
    /// that mutates its own return value afterward cannot reach the stored snapshot. The source snapshot
    /// and the revision it was read at are captured together, under the same lock acquisition, and that
    /// captured revision -- not a freshly re-read one -- is what activation checks: an edit landing after
    /// the read but before the migration commits is therefore detected as a conflict instead of being
    /// silently superseded by a migration that never actually saw it. <paramref name="transform"/>
    /// receives the source as <see cref="ImmutableDictionary{TKey,TValue}"/>, so a transform that casts it
    /// to a mutable interface to edit V1's live data in place -- rather than building a new result -- gets
    /// an exception at the mutating call, not a silently corrupted V1. Migrate always commits atomically
    /// (prepare-and-activate); it has no staged/candidate form of its own -- use <see cref="Prepare"/>
    /// directly for that.
    /// </summary>
    public SettingsSnapshot Migrate(string scope, Func<ImmutableDictionary<string, string>, IReadOnlyDictionary<string, string>> transform) {
        ImmutableDictionary<string, string> source;
        int baseRevision;
        lock (_gate) {
            source = Values(CurrentSnapshotId(scope));
            baseRevision = CurrentVersion(scope);
        }
        // transform runs outside the lock deliberately -- it is caller-supplied and may be slow; the lock
        // only needs to cover the read that establishes what "based on" means.
        if (FailMigrationForTests) throw new InvalidOperationException("injected migration failure");
        IReadOnlyDictionary<string, string> migrated = transform(source);
        return PrepareAndActivate(scope, migrated, baseRevision);
    }

    /// <summary>
    /// Points <paramref name="scope"/> back at <paramref name="toSnapshotId"/>'s exact data -- not a copy,
    /// the original snapshot -- refusing explicitly if a newer edit has landed since
    /// <paramref name="expectedCurrentRevision"/> was observed, same check as <see cref="Prepare"/>. The
    /// scope's revision counter moves strictly forward even though the pointer moves to older data: it is
    /// never set to the target snapshot's own <see cref="SettingsSnapshot.Version"/>, which would let a
    /// caller's stale expectation from BEFORE this rollback coincidentally match again after it.
    /// </summary>
    public void Rollback(string scope, string toSnapshotId, int expectedCurrentRevision) {
        lock (_gate) {
            int current = CurrentVersion(scope);
            if (!SkipConcurrencyCheckForTests && current != expectedCurrentRevision)
                throw new ConcurrencyConflictException(scope, expectedCurrentRevision, current);
            SettingsSnapshot target = _snapshots[toSnapshotId];
            if (!string.Equals(target.Scope, scope, StringComparison.Ordinal))
                throw new InvalidOperationException($"snapshot '{toSnapshotId}' does not belong to scope '{scope}'");
            Persist("rollback", target);
            _activeSnapshotId[scope] = toSnapshotId;
            _scopeRevision[scope] = current + 1; // monotonic forward -- never target.Version, see remarks above
        }
    }

    // Admit(target, runtimeVersion, owner, scope) removed (Alexandr-Kravchuk): it read this store's own
    // current snapshot and admitted an operation with it -- a second admission path once a joint
    // runtime+settings selection exists, and exactly the duplicate source of truth the whole boundary is
    // meant to prevent. "The settings store owns snapshots; it does not own what an operation is admitted
    // under." That is now BeginFromSelection's job, on the ledger, in the joint branch -- it reads one
    // published (runtime, snapshot) selection and registers ownership from it atomically, which also
    // means the settings-only version of that atomicity this method used to provide (kirillkrylov's
    // original finding: reading the current snapshot id and registering ownership must not straddle a
    // concurrent Cleanup) is no longer this store's responsibility either. Cleanup itself is unaffected --
    // Prepare/Activate/RetainForRollback/Cleanup still fully serialize under _gate; what's gone is the
    // guarantee for the specific sequence "read CurrentSnapshotId, then call Begin," which callers must
    // now get from the selection boundary that owns admission, not from this store.

    /// <summary>
    /// Explicitly keeps <paramref name="snapshotId"/> alive across <see cref="Cleanup"/> even once it has
    /// no referencing operation and is no longer any scope's current snapshot -- kirillkrylov's third
    /// ownership reason (a "retained rollback configuration"), deliberately explicit rather than automatic:
    /// how many generations back a rollback target must survive is an operator policy this fixture does
    /// not get to assume. The existence check and the registration happen under the same lock acquisition,
    /// so a concurrent <see cref="Cleanup"/> cannot slip in between them and delete the snapshot the instant
    /// after this method confirmed it was still there.
    /// </summary>
    public void RetainForRollback(string snapshotId) {
        lock (_gate) {
            if (!_snapshots.ContainsKey(snapshotId)) throw new KeyNotFoundException(snapshotId);
            _rollbackRetained[snapshotId] = 0;
        }
    }

    /// <summary>Explicit operator decision to stop protecting a rollback-retained snapshot from cleanup.</summary>
    public void ReleaseRollbackRetention(string snapshotId) {
        lock (_gate) { _rollbackRetained.TryRemove(snapshotId, out _); }
    }

    /// <summary>
    /// Deletes every snapshot not covered by one of five explicit ownership reasons: a scope's currently
    /// pinned snapshot, a snapshot a retained operation still references (the ledger's own
    /// <see cref="IOperationLedger.OperationHeldSnapshots"/>, consumed rather than recomputed -- a second
    /// reference count would eventually disagree with retention), a snapshot explicitly
    /// <see cref="RetainForRollback"/>ed, a candidate still pending <see cref="Activate"/>, or a snapshot
    /// named by a committed selection (<see cref="IOperationLedger.SelectedSnapshots"/>). A snapshot with
    /// zero active operations is not, on its own, eligible -- kirillkrylov's point:
    /// <c>OperationHeldSnapshots</c> is the operation-held set, not the entire set eligible for deletion.
    /// Runs under the same <see cref="_gate"/> as every other mutating method here, so it cannot observe a
    /// snapshot as unowned in a window where <see cref="RetainForRollback"/> or <see cref="Prepare"/> is
    /// mid-flight establishing retention or pending status over it.
    /// <para>
    /// <b>X3 (Alexandr-Kravchuk), narrowed by kirillkrylov.</b> A snapshot held by an operation whose
    /// owner the ledger cannot ask about (<see cref="IOperationLedger.OwnersWithoutLiveness"/>) used to be
    /// silently indistinguishable, in this method's return value, from one legitimately still in use --
    /// not because such an owner IS defective, but because <see cref="Reclaimed"/> alone gave no way to
    /// tell "still in use" apart from "this store cannot ever resolve it if it disappears without
    /// disposing." Most owners in that set are ordinary and will complete normally.
    /// <see cref="CleanupResult.HeldByOwnerWithoutLiveness"/> surfaces the distinction as a diagnostic for
    /// a human or an operator policy to act on; this method itself draws no conclusion from it and reclaims
    /// nothing differently because of it.
    /// </para>
    /// <para>
    /// <b>X8/X9 (Alexandr-Kravchuk), the fifth reason.</b> Committing a joint runtime+settings pair takes
    /// this store's lock and then the ledger's publication lock, and in the window between them this
    /// store is already pinned to the new snapshot while the ledger's committed selection still names the
    /// old one. A cleanup landing exactly there used to see the old snapshot as neither pinned, held, nor
    /// retained, and reclaim it out from under an admission that reads the (not-yet-republished) selection
    /// in the same window. <see cref="IOperationLedger.SelectedSnapshots"/> is not a second source of
    /// truth about what is current -- it answers only "is this snapshot named by a committed selection",
    /// which this store cannot know on its own. See <c>Program.T17</c>, reusing T10's deterministic
    /// interleaving as Alexandr suggested rather than a new pattern.
    /// </para>
    /// </summary>
    public CleanupResult Cleanup() {
        lock (_gate) {
            var referenced = new HashSet<string>(_ledger.OperationHeldSnapshots, StringComparer.Ordinal);
            var pinned = new HashSet<string>(_activeSnapshotId.Values, StringComparer.Ordinal);
            var selected = SkipSelectedSnapshotsProtectionForTests
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(_ledger.SelectedSnapshots, StringComparer.Ordinal);
            string[] doomed = _snapshots.Keys
                .Where(id => !referenced.Contains(id) && !pinned.Contains(id) && !selected.Contains(id)
                             && !_rollbackRetained.ContainsKey(id) && !_pendingCandidates.ContainsKey(id))
                .ToArray();
            foreach (string id in doomed) _snapshots.TryRemove(id, out _);

            string[] heldByUnresolvable = _ledger.OwnersWithoutLiveness
                .Select(opId => _ledger.Query(opId).ConfigurationSnapshot)
                .Where(snapshotId => snapshotId is not null && _snapshots.ContainsKey(snapshotId))
                .Select(snapshotId => snapshotId!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return new CleanupResult(doomed, heldByUnresolvable);
        }
    }

    public bool Contains(string snapshotId) => _snapshots.ContainsKey(snapshotId);

    /// <summary>
    /// Test-only: makes the next <see cref="Activate"/> fail after its checks pass but before it publishes
    /// anything -- kirillkrylov's "a failed settings-commit path, not only rejected-runtime activation."
    /// Models a real write fault, not a policy refusal.
    /// </summary>
    public bool FailNextActivatePersistForTests { get; set; }

    private void Persist(string kind, SettingsSnapshot snapshot) {
        if (kind == "activate" && FailNextActivatePersistForTests) {
            FailNextActivatePersistForTests = false;
            throw new IOException("injected settings-commit failure");
        }
        string line = JsonSerializer.Serialize(new {
            kind, snapshot.Id, snapshot.Scope, snapshot.Version, snapshot.Values
        });
        File.AppendAllText(_evidencePath, line + Environment.NewLine);
    }
}

/// <summary>
/// Secrets a scope's operations need, kept out of <see cref="SettingsSnapshot"/> entirely rather than
/// excluded field-by-field. Never persisted by <see cref="SettingsStore"/>, never serialized alongside a
/// snapshot, never migrated, never rolled back -- credential lifecycle is not settings lifecycle.
/// <para>
/// Narrowed per kirillkrylov: this establishes the INTENDED path never carries a secret (T7's sentinel
/// never appears in either persisted evidence file), not that <see cref="SettingsSnapshot.Values"/> --
/// an open string-keyed, string-valued dictionary, immutable against mutation but not against what a
/// caller chooses to put in it at admission -- structurally cannot hold an arbitrary secret string.
/// Nothing here prevents that misuse; T7 tests the separate-store path this fixture actually offers, not
/// a universal guarantee about what a caller could do instead.
/// </para>
/// </summary>
public sealed class CredentialStore {
    private readonly ConcurrentDictionary<string, string> _byScope = new(StringComparer.Ordinal);
    public void Set(string scope, string secret) => _byScope[scope] = secret;
    public string Resolve(string scope) =>
        _byScope.TryGetValue(scope, out string? secret) ? secret : throw new KeyNotFoundException(scope);
}
