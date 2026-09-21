using System.Collections.Concurrent;
using System.Text.Json;
using Clio10.DetachedOperations;
using Clio10.DetachedOperations.Host;

namespace Clio10.SettingsVersioning;

/// <summary>
/// Portable, credential-free settings for one scope at one version. Deliberately the only surface a
/// runtime or an evidence file ever sees -- see <c>CredentialStore</c> for why a secret can never reach
/// this type, and <c>Program.T6_CredentialFreeSurface</c> for the check that keeps it that way.
/// </summary>
public sealed record SettingsSnapshot(string Id, string Scope, int Version, IReadOnlyDictionary<string, string> Values);

/// <summary>Raised when an edit or rollback is based on a version that is no longer current.</summary>
/// <remarks>
/// The same check backs both an edit race and a rollback racing a newer legitimate edit -- kirillkrylov's
/// requirement that rollback "must not silently overwrite a newer legitimate settings edit" is not a
/// second mechanism, it is this one applied to the operation that happens to move the pointer backward.
/// </remarks>
public sealed class ConcurrencyConflictException(string scope, int expectedBaseVersion, int actualVersion)
    : InvalidOperationException(
        $"scope '{scope}': expected base version {expectedBaseVersion}, current is {actualVersion}") {
    public string Scope { get; } = scope;
    public int ExpectedBaseVersion { get; } = expectedBaseVersion;
    public int ActualVersion { get; } = actualVersion;
}

/// <summary>
/// Versioned settings snapshots layered over the shared <see cref="OperationLedger"/>'s
/// <c>ConfigurationSnapshot</c> seam. Deliberately narrow: proving V1/V2 coexistence, failed-migration
/// safety, explicit-conflict edits and ledger-consistent cleanup -- not a general settings framework, and
/// deliberately not MCP-shaped. Nothing here needs a transport: <c>ConfigurationSnapshot</c> is an opaque
/// string threaded through <see cref="OperationLedger.Begin(string,string,object,string)"/>, and every
/// case below exercises that directly. Real-MCP-client integration belongs to the host-activation
/// deliverable, not this one -- narrowing my own earlier claim that this fixture would reuse the MCP
/// client pattern; on reflection that conflates two different assignments.
/// </summary>
public sealed class SettingsStore {
    private readonly OperationLedger _ledger;
    private readonly string _evidencePath;
    private readonly ConcurrentDictionary<string, SettingsSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _activeSnapshotId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _headVersion = new(StringComparer.Ordinal);
    private int _nextId;

    /// <summary>Test-only: makes <see cref="Migrate"/> fail after reading the source, before anything is written.</summary>
    public bool FailMigrationForTests { get; set; }

    /// <summary>
    /// Test-only: disables the version check in <see cref="Prepare"/> and <see cref="Rollback"/>, so a
    /// stale write silently overwrites instead of being refused. Exists to observe the conflict-detection
    /// claim fail, not just to assert it would -- see <c>Program.T3</c>.
    /// </summary>
    public bool SkipConcurrencyCheckForTests { get; set; }

    public SettingsStore(OperationLedger ledger, string evidencePath) {
        _ledger = ledger;
        _evidencePath = evidencePath;
    }

    public string CurrentSnapshotId(string scope) =>
        _activeSnapshotId.TryGetValue(scope, out string? id)
            ? id
            : throw new KeyNotFoundException($"no snapshot activated for scope '{scope}'");

    public int CurrentVersion(string scope) => _headVersion.GetValueOrDefault(scope, 0);

    public IReadOnlyDictionary<string, string> Values(string snapshotId) => _snapshots[snapshotId].Values;

    /// <summary>
    /// Prepares and activates a new snapshot for <paramref name="scope"/>. A concurrent edit based on a
    /// stale <paramref name="expectedBaseVersion"/> is refused, not merged and not silently overwritten.
    /// </summary>
    public SettingsSnapshot Prepare(string scope, IReadOnlyDictionary<string, string> values, int expectedBaseVersion) {
        int current = CurrentVersion(scope);
        if (!SkipConcurrencyCheckForTests && current != expectedBaseVersion)
            throw new ConcurrencyConflictException(scope, expectedBaseVersion, current);
        var snapshot = new SettingsSnapshot($"cfg-{Interlocked.Increment(ref _nextId)}", scope, current + 1, values);
        Persist("prepare", snapshot);
        _snapshots[snapshot.Id] = snapshot;
        _activeSnapshotId[scope] = snapshot.Id;
        _headVersion[scope] = snapshot.Version;
        return snapshot;
    }

    /// <summary>
    /// Migrates <paramref name="scope"/>'s current values through <paramref name="transform"/>. The
    /// transformed result is built entirely in memory before <see cref="Prepare"/> ever persists anything,
    /// so a transform that throws -- or <see cref="FailMigrationForTests"/> -- leaves the prior snapshot
    /// exactly as it was: not a partial write, because there is never a partial write to begin with.
    /// </summary>
    public SettingsSnapshot Migrate(string scope, Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> transform) {
        IReadOnlyDictionary<string, string> source = Values(CurrentSnapshotId(scope));
        if (FailMigrationForTests) throw new InvalidOperationException("injected migration failure");
        IReadOnlyDictionary<string, string> migrated = transform(source);
        return Prepare(scope, migrated, CurrentVersion(scope));
    }

    /// <summary>
    /// Points <paramref name="scope"/> back at <paramref name="toSnapshotId"/>'s exact data -- not a copy,
    /// the original snapshot -- refusing explicitly if a newer edit has landed since
    /// <paramref name="expectedCurrentVersion"/> was observed, same check as <see cref="Prepare"/>.
    /// </summary>
    public void Rollback(string scope, string toSnapshotId, int expectedCurrentVersion) {
        int current = CurrentVersion(scope);
        if (!SkipConcurrencyCheckForTests && current != expectedCurrentVersion)
            throw new ConcurrencyConflictException(scope, expectedCurrentVersion, current);
        SettingsSnapshot target = _snapshots[toSnapshotId];
        if (!string.Equals(target.Scope, scope, StringComparison.Ordinal))
            throw new InvalidOperationException($"snapshot '{toSnapshotId}' does not belong to scope '{scope}'");
        Persist("rollback", target);
        _activeSnapshotId[scope] = toSnapshotId;
        _headVersion[scope] = target.Version;
    }

    /// <summary>Admits an operation under <paramref name="scope"/>'s currently active snapshot.</summary>
    public IOperationLease Admit(string target, string runtimeVersion, object owner, string scope) =>
        _ledger.Begin(target, runtimeVersion, owner, CurrentSnapshotId(scope));

    /// <summary>
    /// Deletes every snapshot that is neither a scope's currently pinned snapshot NOR referenced by a
    /// retained operation. The retained-operation half is the ledger's own <see cref="IOperationLedger.ReferencedSnapshots"/>,
    /// consumed rather than recomputed -- a second reference count would eventually disagree with
    /// retention (Alexandr-Kravchuk's point, taken directly).
    /// </summary>
    public IReadOnlyCollection<string> Cleanup() {
        var referenced = new HashSet<string>(_ledger.ReferencedSnapshots, StringComparer.Ordinal);
        var pinned = new HashSet<string>(_activeSnapshotId.Values, StringComparer.Ordinal);
        string[] doomed = _snapshots.Keys.Where(id => !referenced.Contains(id) && !pinned.Contains(id)).ToArray();
        foreach (string id in doomed) _snapshots.TryRemove(id, out _);
        return doomed;
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
/// </summary>
public sealed class CredentialStore {
    private readonly ConcurrentDictionary<string, string> _byScope = new(StringComparer.Ordinal);
    public void Set(string scope, string secret) => _byScope[scope] = secret;
    public string Resolve(string scope) =>
        _byScope.TryGetValue(scope, out string? secret) ? secret : throw new KeyNotFoundException(scope);
}
