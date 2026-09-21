using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Clio10.DetachedOperations.Host;

/// <summary>
/// Generic host ledger: issues identifiers, retains owners, and keeps durable evidence that an
/// operation was started.
/// </summary>
/// <remarks>
/// <para>
/// <b>Evidence, not replay.</b> Two lines are appended per operation — one when it begins, one when it
/// reaches a terminal state. Nothing about the work itself is written, so recovery can report that an
/// outcome is unavailable but can never resume or retry anything. That boundary is deliberate: persisting
/// a record does not make a retry safe, and this probe does not claim it does.
/// </para>
/// <para>
/// <b>Why the owner is retained by reference.</b> The lease holds the object that owns execution — in the
/// probe, the loaded runtime instance. While any lease is alive its runtime cannot be retired, which is
/// what makes "activate a new runtime" and "finish the old operation" independent of each other.
/// </para>
/// <para>
/// <b>No feature semantics.</b> The ledger stores an opaque <c>Code</c> supplied by the runtime and never
/// reads it. Interpreting what an operation means stays with the runtime that owns it.
/// </para>
/// </remarks>
public sealed class OperationLedger : IOperationLedger {
    private readonly string _evidencePath;
    private readonly object _fileLock = new();
    private readonly ConcurrentDictionary<string, OperationRecord> _live = new();
    private readonly ConcurrentDictionary<string, object> _owners = new();
    private readonly IReadOnlyDictionary<string, OperationRecord> _recovered;
    private readonly object _swapLock = new();
    private readonly HashSet<string> _heldScopes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _degradedScopes = new(StringComparer.Ordinal);

    private readonly bool _splitAdmissionForTests;

    /// <summary>
    /// Test-only: makes the terminal evidence write fail, so the consequence of a persistence failure
    /// can be measured instead of described. Nothing in the probe's normal path sets it.
    /// </summary>
    public bool FailEndPersistenceForTests { get; set; }

    /// <summary>
    /// Test-only: makes the admission evidence write fail, so the stranded-admission repair has a
    /// deterministic regression. Nothing in the probe's normal path sets it.
    /// </summary>
    public bool FailBeginPersistenceForTests { get; set; }

    /// <summary>Opens a ledger over an evidence file, recovering any prior process's unfinished operations.</summary>
    public OperationLedger(string evidencePath) : this(evidencePath, false) {
    }

    /// <summary>
    /// Test-only constructor that can reproduce the original split-admission defect on demand.
    /// </summary>
    /// <param name="evidencePath">Evidence file.</param>
    /// <param name="splitAdmissionForTests">
    /// <see langword="true"/> restores the pre-repair behaviour: check the held scopes, release the lock,
    /// then register. This exists so the regression can be shown to FAIL against a deliberately broken
    /// ledger — a concurrency test that has never been seen to fail proves nothing about the code it
    /// guards. Nothing in the probe's normal path sets it.
    /// </param>
    public OperationLedger(string evidencePath, bool splitAdmissionForTests) {
        _evidencePath = evidencePath;
        _splitAdmissionForTests = splitAdmissionForTests;
        _recovered = Recover(evidencePath);
    }

    /// <summary>Operations recovered from a previous process that have no recorded outcome.</summary>
    public IReadOnlyCollection<string> RecoveredUnknown =>
        _recovered.Where(p => p.Value.State == OperationState.Unknown).Select(p => p.Key).ToArray();

    /// <inheritdoc />
    public IReadOnlyCollection<string> Running =>
        _live.Where(p => p.Value.State == OperationState.Running).Select(p => p.Key).ToArray();

    /// <inheritdoc />
    public bool IsQuiescent(string? target = null) {
        lock (_swapLock) { return IsQuiescentCore(target); }
    }

    // Quiescence follows RETENTION, not the published outcome. An operation may report Succeeded and
    // still hold its lease while owned cleanup finishes; retiring the release then would pull the floor
    // out from under that cleanup. Ownership ends at Dispose, not at Complete.
    private bool IsQuiescentCore(string? target) => !_owners.Keys.Any(id =>
        _live.TryGetValue(id, out var r) &&
        (target is null || string.Equals(r.Target, target, StringComparison.Ordinal)));

    /// <inheritdoc />
    public IReadOnlyCollection<string> DegradedScopes {
        get { lock (_swapLock) { return _degradedScopes.ToArray(); } }
    }

    /// <inheritdoc />
    public IDisposable? TryEnterSwapWindow(string? target = null) {
        string scope = target ?? string.Empty;
        lock (_swapLock) {
            // Refused while evidence is degraded: retiring a release whose outcomes could not be
            // recorded would destroy the only place the truth still exists.
            if (_degradedScopes.Count > 0 &&
                (target is null || _degradedScopes.Contains(scope))) {
                return null;
            }
            // Exclusion is checked in BOTH directions. A global window excludes every target window, and
            // any held scope excludes a global one; granting a target window under a held global window
            // would let work start on the very host the global window is protecting.
            if (_heldScopes.Contains(string.Empty)) return null;
            if (target is null && _heldScopes.Count > 0) return null;
            if (_heldScopes.Contains(scope)) return null;
            if (!IsQuiescentCore(target)) return null;
            _heldScopes.Add(scope);
            return new SwapWindow(this, scope);
        }
    }

    private void ReleaseScope(string scope) {
        lock (_swapLock) { _heldScopes.Remove(scope); }
    }

    /// <inheritdoc />
    public IOperationLease Begin(string target, string runtimeVersion, object owner) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        string id = Guid.NewGuid().ToString("n");
        var record = new OperationRecord(id, target, DateTimeOffset.UtcNow, runtimeVersion, OperationState.Running);
        if (_splitAdmissionForTests) {
            // The defect, on purpose: check, release, then register. The sleep only widens a gap that
            // exists either way, so the mutation is observable in a two-second run instead of rarely.
            lock (_swapLock) {
                if (_heldScopes.Contains(string.Empty) || _heldScopes.Contains(target)) {
                    throw new SwapWindowHeldException(target);
                }
            }
            Thread.Sleep(1);
            lock (_swapLock) {
                _live[id] = record;
                _owners[id] = owner;
                Append("begin", record);
            }
            return new Lease(this, id);
        }

        lock (_swapLock) {
            // Admission and registration are ONE critical section. Splitting them — checking the window,
            // releasing the lock, then inserting the record — leaves a gap in which a swap observes
            // quiescence and is granted a window that this operation then runs underneath.
            if (_heldScopes.Contains(string.Empty) || _heldScopes.Contains(target)) {
                throw new SwapWindowHeldException(target);   // refused, not queued
            }
            // Evidence FIRST, then registration. The reverse order strands admission: a failed write
            // left a Running record with no lease to complete it, so the scope was blocked forever by an
            // operation that never started. A throw here registers nothing and starts no work; a
            // partially durable line is recovered as Unknown, which is truthful.
            if (FailBeginPersistenceForTests) {
                throw new IOException("injected admission-evidence failure");
            }
            Append("begin", record);
            _live[id] = record;
            _owners[id] = owner;                   // retention: the runtime cannot be retired under it
        }
        return new Lease(this, id);
    }

    /// <inheritdoc />
    public OperationRecord Query(string id) {
        if (_live.TryGetValue(id, out var live)) return live;
        if (_recovered.TryGetValue(id, out var prior)) return prior;
        // Truthful: no evidence this identifier was ever issued. Distinct from "started, outcome unknown".
        return new OperationRecord(id, string.Empty, default, string.Empty, OperationState.NotFound);
    }

    private void Complete(string id, OperationState state, string? code) {
        if (state is OperationState.Running or OperationState.NotFound)
            throw new ArgumentOutOfRangeException(nameof(state), state, "A terminal state is required.");
        lock (_swapLock) {
            // Exactly once: a record that already reached a terminal state is never rewritten.
            if (!_live.TryGetValue(id, out var existing) || existing.State != OperationState.Running) return;
            var updated = existing with { State = state, FinishedUtc = DateTimeOffset.UtcNow, Code = code };

            // Evidence is written BEFORE the state becomes visible as terminal, and the whole transition
            // happens inside the window lock. Both orderings matter:
            //  - persist-then-publish, because a crash after publishing but before persisting would leave
            //    disk holding only the begin record, so a completed operation would be recovered as
            //    Unknown;
            //  - all of it under the lock, because otherwise a swap window can be granted the instant the
            //    state flips, while the terminal record is still unwritten, and a replacement acting on
            //    that window loses the evidence entirely.
            // The cost is that a completion serialises against window acquisition, including its fsync.
            // Acceptable here; a production ledger would likely want a two-phase commit instead.
            // The outcome is published either way. Storage health is a SEPARATE axis: successful work
            // must not become a failed or retryable business operation because a disk write failed.
            try {
                if (FailEndPersistenceForTests) {
                    throw new IOException("injected evidence-write failure");
                }
                Append("end", updated);
            }
            catch (Exception) {
                // Visible degradation, no replay, and no automatic retirement for this scope: the
                // in-memory record is now the only place this outcome exists.
                _degradedScopes.Add(existing.Target);
            }
            _live[id] = updated;
            // Retention is NOT released here. Publishing an outcome and relinquishing ownership are
            // different events; the lease's Dispose is the second one.
        }
    }

    private void ReleaseOwnership(string id) {
        lock (_swapLock) { _owners.TryRemove(id, out _); }
    }

    private void Append(string kind, OperationRecord record) {
        string line = JsonSerializer.Serialize(new {
            kind, record.Id, record.Target, startedUtc = record.StartedUtc.ToString("O", CultureInfo.InvariantCulture),
            runtimeVersion = record.RuntimeVersion, state = record.State.ToString(),
            finishedUtc = record.FinishedUtc?.ToString("O", CultureInfo.InvariantCulture), record.Code
        });
        lock (_fileLock) {
            using var stream = new FileStream(_evidencePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(line);
            writer.Flush();
            stream.Flush(true);                     // survive process loss, which is the case being measured
        }
    }

    private static IReadOnlyDictionary<string, OperationRecord> Recover(string path) {
        var found = new Dictionary<string, OperationRecord>(StringComparer.Ordinal);
        if (!File.Exists(path)) return found;
        foreach (string line in File.ReadAllLines(path)) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            OperationRecord? parsed = TryParse(line);
            if (parsed is null) continue;           // a torn final line is itself evidence of process loss
            found[parsed.Id] = found.TryGetValue(parsed.Id, out var prior) && prior.State != OperationState.Running
                ? prior                              // a terminal line already won
                : parsed;
        }
        // A begin with no end means this host cannot establish the outcome. Say that, never "not found".
        return found.ToDictionary(p => p.Key,
            p => p.Value.State == OperationState.Running
                ? p.Value with { State = OperationState.Unknown, Code = "history-unavailable" }
                : p.Value, StringComparer.Ordinal);
    }

    private static OperationRecord? TryParse(string line) {
        try {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            string id = root.GetProperty("Id").GetString()!;
            var state = Enum.Parse<OperationState>(root.GetProperty("state").GetString()!);
            DateTimeOffset started = DateTimeOffset.Parse(root.GetProperty("startedUtc").GetString()!,
                CultureInfo.InvariantCulture);
            string? finishedText = root.GetProperty("finishedUtc").ValueKind == JsonValueKind.Null
                ? null : root.GetProperty("finishedUtc").GetString();
            return new OperationRecord(id, root.GetProperty("Target").GetString() ?? string.Empty, started,
                root.GetProperty("runtimeVersion").GetString() ?? string.Empty,
                state, finishedText is null ? null : DateTimeOffset.Parse(finishedText, CultureInfo.InvariantCulture),
                root.GetProperty("Code").ValueKind == JsonValueKind.Null ? null : root.GetProperty("Code").GetString());
        }
        catch (Exception) { return null; }
    }

    private sealed class SwapWindow(OperationLedger ledger, string scope) : IDisposable {
        private int _released;

        public void Dispose() {
            if (Interlocked.Exchange(ref _released, 1) == 0) ledger.ReleaseScope(scope);
        }
    }

    private sealed class Lease(OperationLedger ledger, string id) : IOperationLease {
        private int _reported;
        private int _released;

        public string Id { get; } = id;

        /// <summary>Publishes the outcome. Does not end ownership — see <see cref="Dispose"/>.</summary>
        public void Complete(OperationState state, string? code = null) {
            if (Interlocked.Exchange(ref _reported, 1) == 0) ledger.Complete(Id, state, code);
        }

        /// <summary>
        /// Ends ownership. Disposing without having published an outcome is a defect, not a silent
        /// success, so a terminal is recorded rather than leaving a caller polling a record that will
        /// never move.
        /// </summary>
        public void Dispose() {
            Complete(OperationState.Failed, "lease-disposed-without-terminal");
            if (Interlocked.Exchange(ref _released, 1) == 0) ledger.ReleaseOwnership(Id);
        }
    }
}
