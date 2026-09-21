using System.Collections.Concurrent;
using System.Text.Json;
using Clio10.DetachedOperations;
using Clio10.DetachedOperations.Host;

namespace Clio10.SettingsVersioning;

/// <summary>
/// Portable, credential-free settings for one scope at one version. Deliberately the only surface a
/// runtime or an evidence file ever sees -- see <c>CredentialStore</c> for why a secret can never reach
/// this type, and <c>Program.T6_CredentialFreeSurface</c> for the check that keeps it that way. The
/// underlying <see cref="Values"/> dictionary is always a defensive copy taken at admission
/// (<see cref="SettingsStore.Prepare"/>); a caller mutating the collection it originally passed in cannot
/// reach a pinned snapshot's view -- kirillkrylov's finding, fixed here rather than merely documented.
/// </summary>
public sealed record SettingsSnapshot(string Id, string Scope, int Version, IReadOnlyDictionary<string, string> Values);

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
    /// Test-only: invoked synchronously inside <see cref="Prepare"/>'s critical section, after the
    /// revision check passes and before anything is written, still holding <see cref="_gate"/>. Lets a
    /// regression test force a deterministic interleaving window instead of relying on timing -- see
    /// <c>Program.T9</c>.
    /// </summary>
    public Action? OnPreparePassedCheckForTests { get; set; }

    /// <summary>
    /// Test-only: invoked synchronously inside <see cref="Admit"/>, after the current snapshot id is read
    /// and before <see cref="OperationLedger.Begin(string,string,object,string)"/> registers ownership,
    /// still holding <see cref="_gate"/>. See <c>Program.T10</c>.
    /// </summary>
    public Action? OnAdmitReadSnapshotIdForTests { get; set; }

    public SettingsStore(OperationLedger ledger, string evidencePath) {
        _ledger = ledger;
        _evidencePath = evidencePath;
    }

    public string CurrentSnapshotId(string scope) =>
        _activeSnapshotId.TryGetValue(scope, out string? id)
            ? id
            : throw new KeyNotFoundException($"no snapshot activated for scope '{scope}'");

    public int CurrentVersion(string scope) => _scopeRevision.GetValueOrDefault(scope, 0);

    public IReadOnlyDictionary<string, string> Values(string snapshotId) => _snapshots[snapshotId].Values;

    /// <summary>
    /// Prepares and activates a new snapshot for <paramref name="scope"/>. A concurrent edit based on a
    /// stale <paramref name="expectedBaseRevision"/> is refused, not merged and not silently overwritten
    /// -- and, unlike the first cut of this method, genuinely refused under real concurrent callers, not
    /// only under sequential stale-revision calls: the whole check-compute-persist-publish sequence runs
    /// under <see cref="_gate"/>, so a second caller cannot even read the revision until the first has
    /// either committed or thrown. <paramref name="values"/> is defensively copied before being stored, so
    /// a caller mutating the dictionary it passed in afterward cannot reach the pinned snapshot.
    /// </summary>
    public SettingsSnapshot Prepare(string scope, IReadOnlyDictionary<string, string> values, int expectedBaseRevision) {
        lock (_gate) {
            int current = CurrentVersion(scope);
            if (!SkipConcurrencyCheckForTests && current != expectedBaseRevision)
                throw new ConcurrencyConflictException(scope, expectedBaseRevision, current);
            OnPreparePassedCheckForTests?.Invoke();
            var frozen = new Dictionary<string, string>(values, StringComparer.Ordinal); // defensive copy
            int newRevision = current + 1;
            var snapshot = new SettingsSnapshot($"cfg-{Interlocked.Increment(ref _nextId)}", scope, newRevision, frozen);
            Persist("prepare", snapshot);
            _snapshots[snapshot.Id] = snapshot;
            _activeSnapshotId[scope] = snapshot.Id;
            _scopeRevision[scope] = newRevision;
            return snapshot;
        }
    }

    /// <summary>
    /// Migrates <paramref name="scope"/>'s current values through <paramref name="transform"/>. The
    /// transformed result is built entirely in memory before <see cref="Prepare"/> ever persists anything,
    /// so a transform that throws -- or <see cref="FailMigrationForTests"/> -- leaves the prior snapshot
    /// exactly as it was. The source snapshot and the revision it was read at are captured together, under
    /// the same lock acquisition, and that captured revision -- not a freshly re-read one -- is what
    /// <see cref="Prepare"/> checks: an edit landing after the read but before the migration commits is
    /// therefore detected as a conflict instead of being silently superseded by a migration that never
    /// actually saw it.
    /// </summary>
    public SettingsSnapshot Migrate(string scope, Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> transform) {
        IReadOnlyDictionary<string, string> source;
        int baseRevision;
        lock (_gate) {
            source = Values(CurrentSnapshotId(scope));
            baseRevision = CurrentVersion(scope);
        }
        // transform runs outside the lock deliberately -- it is caller-supplied and may be slow; the lock
        // only needs to cover the read that establishes what "based on" means.
        if (FailMigrationForTests) throw new InvalidOperationException("injected migration failure");
        IReadOnlyDictionary<string, string> migrated = transform(source);
        return Prepare(scope, migrated, baseRevision);
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

    /// <summary>
    /// Admits an operation under <paramref name="scope"/>'s currently active snapshot. Reading which
    /// snapshot is current and registering the operation as its owner happen under the same lock
    /// acquisition, so a concurrent <see cref="Cleanup"/> cannot observe the snapshot as unowned and
    /// unpinned in the gap between those two steps -- the race kirillkrylov found: "activation and cleanup
    /// between those steps can delete the ID being admitted."
    /// </summary>
    public IOperationLease Admit(string target, string runtimeVersion, object owner, string scope) {
        lock (_gate) {
            string snapshotId = CurrentSnapshotId(scope);
            OnAdmitReadSnapshotIdForTests?.Invoke();
            return _ledger.Begin(target, runtimeVersion, owner, snapshotId);
        }
    }

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
    /// Deletes every snapshot not covered by one of three explicit ownership reasons: a scope's currently
    /// pinned snapshot, a snapshot a retained operation still references (the ledger's own
    /// <see cref="IOperationLedger.OperationHeldSnapshots"/>, consumed rather than recomputed -- a second
    /// reference count would eventually disagree with retention), or a snapshot explicitly
    /// <see cref="RetainForRollback"/>ed. A snapshot with zero active operations is not, on its own,
    /// eligible -- kirillkrylov's point: <c>OperationHeldSnapshots</c> is the operation-held set, not the
    /// entire set eligible for deletion. Runs under the same <see cref="_gate"/> as every other mutating
    /// method here, so it cannot observe a snapshot as unowned in a window where <see cref="Admit"/> or
    /// <see cref="RetainForRollback"/> is mid-flight establishing ownership or retention over it.
    /// </summary>
    public IReadOnlyCollection<string> Cleanup() {
        lock (_gate) {
            var referenced = new HashSet<string>(_ledger.OperationHeldSnapshots, StringComparer.Ordinal);
            var pinned = new HashSet<string>(_activeSnapshotId.Values, StringComparer.Ordinal);
            string[] doomed = _snapshots.Keys
                .Where(id => !referenced.Contains(id) && !pinned.Contains(id) && !_rollbackRetained.ContainsKey(id))
                .ToArray();
            foreach (string id in doomed) _snapshots.TryRemove(id, out _);
            return doomed;
        }
    }

    public bool Contains(string snapshotId) => _snapshots.ContainsKey(snapshotId);

    private void Persist(string kind, SettingsSnapshot snapshot) {
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
/// an open <c>IReadOnlyDictionary&lt;string,string&gt;</c> -- structurally cannot hold an arbitrary
/// string a caller chose to put there. Nothing here prevents that misuse; T7 tests the separate-store
/// path this fixture actually offers, not a universal guarantee about what a caller could do instead.
/// </para>
/// </summary>
public sealed class CredentialStore {
    private readonly ConcurrentDictionary<string, string> _byScope = new(StringComparer.Ordinal);
    public void Set(string scope, string secret) => _byScope[scope] = secret;
    public string Resolve(string scope) =>
        _byScope.TryGetValue(scope, out string? secret) ? secret : throw new KeyNotFoundException(scope);
}
