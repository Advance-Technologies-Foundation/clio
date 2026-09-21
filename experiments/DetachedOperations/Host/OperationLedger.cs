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
    private readonly Dictionary<string, OperationRecord> _unpersisted = new(StringComparer.Ordinal);

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

    /// <summary>
    /// Test-only: widens the window inside outcome recording so the lease's serialisation property can
    /// be observed deterministically instead of raced for. Nothing in the probe's normal path sets it.
    /// </summary>
    public int CompleteDelayMsForTests { get; set; }

    /// <summary>
    /// Test-only: restores the pre-repair lease, which set its "already reported" flag before calling
    /// the ledger. Exists so L1 can be shown to FAIL against the defect it guards.
    /// </summary>
    public bool UseFlagFirstLeaseForTests { get; set; }

    /// <summary>
    /// Test-only: writes a truncated evidence line and then throws, modelling a failure part-way
    /// THROUGH the write rather than before it. Applies to terminal records only.
    /// </summary>
    public bool FailMidWriteForTests { get; set; }

    /// <summary>
    /// Test-only: makes any evidence append fail cleanly, without writing anything. Models a storage
    /// fault that is still present when a repair is attempted.
    /// </summary>
    public bool FailAppendForTests { get; set; }

    /// <summary>
    /// Test-only: the snapshot a naive implementation would report. Used by the J1 mutation control,
    /// which answers queries from the CURRENT snapshot instead of the recorded one.
    /// </summary>
    public string? CurrentSnapshotForTests { get; set; }

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

    /// <summary>
    /// Identifiers whose OUTCOME has not been published yet.
    /// </summary>
    /// <remarks>
    /// Not the same set as "blocks quiescence". An operation that published its outcome while still
    /// holding its lease for owned cleanup is absent from here and still retained, so it still refuses
    /// retirement — measured by P5. Quiescence follows ownership; this property follows the outcome.
    /// </remarks>
    public IReadOnlyCollection<string> Running {
        get {
            lock (_swapLock) { ResolveOrphansCore(); }
            return _live.Where(p => p.Value.State == OperationState.Running).Select(p => p.Key).ToArray();
        }
    }

    /// <inheritdoc />
    public bool IsQuiescent(string? target = null) {
        lock (_swapLock) {
            ResolveOrphansCore();
            return IsQuiescentCore(target);
        }
    }

    /// <summary>
    /// Resolves operations whose owner is gone. Caller holds <see cref="_swapLock"/>.
    /// </summary>
    /// <remarks>
    /// An owner that reports itself not alive can never publish an outcome and can never run cleanup, so
    /// leaving its operation Running is not caution — it is a lie that an agent will wait on forever.
    /// Retention is dropped here rather than at a Dispose that is never coming, so a drain can finish.
    /// <para>
    /// The resolution is deliberately narrow. Losing the owner establishes that the LOCAL EXECUTOR is
    /// gone — not that the work failed, and not that anything it already did was undone. So the state is
    /// <see cref="OperationState.Unknown"/> and never <see cref="OperationState.Failed"/>, a record that
    /// already published a terminal state is left exactly as it is, and a failure to persist the
    /// resolution degrades the scope rather than dropping the record.
    /// </para>
    /// </remarks>
    private void ResolveOrphansCore() {
        foreach (string id in _owners.Keys.ToArray()) {
            if (!_owners.TryGetValue(id, out object? owner)) continue;
            if (owner is not IOwnerLiveness liveness || liveness.IsAlive) continue;
            if (!_live.TryGetValue(id, out var record) || record.State != OperationState.Running) {
                _owners.TryRemove(id, out _);       // outcome known, owner gone: nothing left to retain
                continue;
            }
            var resolved = record with {
                State = OperationState.Unknown, FinishedUtc = DateTimeOffset.UtcNow, Code = "owner-lost"
            };
            try { Append("end", resolved); }
            catch (Exception) {
                _degradedScopes.Add(record.Target);
                _unpersisted[id] = resolved;
            }
            _live[id] = resolved;
            _owners.TryRemove(id, out _);
        }
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

    /// <inheritdoc />
    public IDisposable? TryReserveAdmission(string? target = null) {
        string scope = target ?? string.Empty;
        lock (_swapLock) {
            if (_heldScopes.Contains(string.Empty)) return null;
            if (target is null && _heldScopes.Count > 0) return null;
            if (_heldScopes.Contains(scope)) return null;
            // Deliberately NOT gated on quiescence: closing the scope is what makes the drain finite.
            _heldScopes.Add(scope);
            return new SwapWindow(this, scope);
        }
    }

    private void ReleaseScope(string scope) {
        lock (_swapLock) { _heldScopes.Remove(scope); }
    }

    /// <inheritdoc />
    public IOperationLease Begin(string target, string runtimeVersion, object owner) =>
        Begin(target, runtimeVersion, owner, null);

    /// <inheritdoc />
    public IOperationLease Begin(string target, string runtimeVersion, object owner,
        string? configurationSnapshot) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        string id = Guid.NewGuid().ToString("n");
        // The snapshot is captured HERE, at admission, and never re-read. An operation runs under the
        // configuration it was admitted under, exactly as it runs on the release that admitted it.
        var record = new OperationRecord(id, target, DateTimeOffset.UtcNow, runtimeVersion,
            OperationState.Running, ConfigurationSnapshot: configurationSnapshot);
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
        return UseFlagFirstLeaseForTests ? new FlagFirstLease(this, id) : new Lease(this, id);
    }

    // Caller holds _swapLock. The coherent admission path ONLY -- it deliberately does not carry the
    // split-admission defect branch, because the first attempt at this refactor wrapped Begin in an
    // outer lock and thereby held the lock across that branch's own gap, which silenced the A5i
    // mutation control: the deliberate defect stopped producing violations. A regression that hides
    // a defect detector is worse than the defect.
    private IOperationLease AdmitUnderLock(string target, string runtimeVersion, object owner,
        string? configurationSnapshot) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (_heldScopes.Contains(string.Empty) || _heldScopes.Contains(target)) {
            throw new SwapWindowHeldException(target);
        }
        string id = Guid.NewGuid().ToString("n");
        var record = new OperationRecord(id, target, DateTimeOffset.UtcNow, runtimeVersion,
            OperationState.Running, ConfigurationSnapshot: configurationSnapshot);
        if (FailBeginPersistenceForTests) {
            throw new IOException("injected admission-evidence failure");
        }
        Append("begin", record);
        _live[id] = record;
        _owners[id] = owner;
        return UseFlagFirstLeaseForTests ? new FlagFirstLease(this, id) : new Lease(this, id);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> OwnersWithoutLiveness {
        get {
            lock (_swapLock) {
                return _owners.Where(p => p.Value is not IOwnerLiveness).Select(p => p.Key).ToArray();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> OperationHeldSnapshots {
        get {
            lock (_swapLock) {
                ResolveOrphansCore();
                return _owners.Keys
                    .Select(id => _live.TryGetValue(id, out var r) ? r.ConfigurationSnapshot : null)
                    .Where(snapshot => snapshot is not null)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()!;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> UnpersistedOperations {
        get { lock (_swapLock) { return _unpersisted.Keys.ToArray(); } }
    }

    /// <summary>
    /// Attempts to make a scope's memory-only outcomes durable. This is the only operation that
    /// actually repairs anything.
    /// </summary>
    /// <param name="target">Scope to repair.</param>
    /// <returns>How many were persisted, how many remain, and whether the scope mark was lifted.</returns>
    /// <remarks>
    /// Re-persisting an outcome is not a replay: it writes the record of something that already
    /// happened, exactly once, with its original values. Nothing is re-executed.
    /// </remarks>
    public RepairResult RepairDegraded(string target) {
        lock (_swapLock) {
            var mine = _unpersisted.Where(p => string.Equals(p.Value.Target, target, StringComparison.Ordinal))
                .ToArray();
            int repaired = 0;
            foreach (var entry in mine) {
                try {
                    Append("end", entry.Value);
                    _unpersisted.Remove(entry.Key);
                    repaired++;
                }
                catch (Exception) {
                    break;                          // the fault is still present; stop rather than thrash
                }
            }
            bool remainingHere = _unpersisted.Values.Any(r =>
                string.Equals(r.Target, target, StringComparison.Ordinal));
            bool cleared = !remainingHere && _degradedScopes.Remove(target);
            return new RepairResult(repaired, mine.Length - repaired, cleared);
        }
    }

    /// <summary>
    /// Abandons a scope's memory-only outcomes and lifts its degraded mark. This is **explicit loss**,
    /// not repair: the abandoned outcomes are gone and anything that asks about them later will be told
    /// the truth — that this host cannot establish them.
    /// </summary>
    /// <param name="target">Scope to abandon.</param>
    /// <returns>How many outcomes were abandoned.</returns>
    public int AcceptLoss(string target) {
        lock (_swapLock) {
            var mine = _unpersisted.Where(p => string.Equals(p.Value.Target, target, StringComparison.Ordinal))
                .Select(p => p.Key).ToArray();
            foreach (string id in mine) {
                _unpersisted.Remove(id);
            }
            _degradedScopes.Remove(target);
            return mine.Length;
        }
    }

    private readonly Dictionary<string, ActivationSelection> _selections = new(StringComparer.Ordinal);

    /// <summary>
    /// Test-only: runs inside the publication critical section, so a concurrent admission can be held
    /// exactly at the coordination boundary instead of raced for.
    /// </summary>
    public Action? OnPublishInsideBoundaryForTests { get; set; }

    /// <inheritdoc />
    public ActivationSelection CurrentSelection(string target) {
        lock (_swapLock) { return CurrentSelectionCore(target); }
    }

    private ActivationSelection CurrentSelectionCore(string target) =>
        _selections.TryGetValue(target, out var selection)
            ? selection
            : new ActivationSelection(string.Empty, null, 0);

    /// <inheritdoc />
    public bool TryPublishSelection(string target, string runtimeVersion, string? configurationSnapshot,
        long expectedGeneration) {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        lock (_swapLock) {
            ActivationSelection current = CurrentSelectionCore(target);
            if (current.Generation != expectedGeneration) return false;
            OnPublishInsideBoundaryForTests?.Invoke();
            _selections[target] = new ActivationSelection(runtimeVersion, configurationSnapshot,
                current.Generation + 1);
            return true;
        }
    }

    /// <inheritdoc />
    public IOperationLease BeginFromSelection(string target, object owner) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        lock (_swapLock) {
            // One read of the pair, inside the same lock a publication takes. Reading the two halves
            // separately is exactly the defect this overload exists to remove.
            ActivationSelection selection = CurrentSelectionCore(target);
            return AdmitUnderLock(target, selection.RuntimeVersion, owner, selection.ConfigurationSnapshot);
        }
    }

    /// <inheritdoc />
    public OperationRecord Query(string id) {
        lock (_swapLock) { ResolveOrphansCore(); }
        bool mutateSnapshot = Environment.GetEnvironmentVariable("MUTATE_SNAPSHOT_AT_QUERY") == "1"
            && CurrentSnapshotForTests is not null;
        if (_live.TryGetValue(id, out var live)) {
            return mutateSnapshot ? live with { ConfigurationSnapshot = CurrentSnapshotForTests } : live;
        }
        if (_recovered.TryGetValue(id, out var prior)) return prior;
        // Truthful: no evidence this identifier was ever issued. Distinct from "started, outcome unknown".
        return new OperationRecord(id, string.Empty, default, string.Empty, OperationState.NotFound);
    }

    private void Complete(string id, OperationState state, string? code) =>
        Complete(id, state, code, fromDisposal: false);

    private void Complete(string id, OperationState state, string? code, bool fromDisposal) {
        if (state is OperationState.Running or OperationState.NotFound)
            throw new ArgumentOutOfRangeException(nameof(state), state, "A terminal state is required.");
        if (CompleteDelayMsForTests > 0) {
            Thread.Sleep(CompleteDelayMsForTests);
        }
        lock (_swapLock) {
            // Exactly once, with ONE exception. A record that already reached a terminal state is never
            // rewritten -- except Unknown, which is not a verdict but an admission that the host does not
            // know. A genuine outcome arriving afterwards (the owner's last output still sitting in a pipe
            // buffer when it was declared gone) is knowledge replacing the absence of it, and both lines
            // stay in the evidence. Disposal is NOT knowledge, so its fallback never supersedes Unknown:
            // that would turn "I do not know" into a fabricated Failed.
            if (!_live.TryGetValue(id, out var existing)) return;
            bool supersedingUnknown = existing.State == OperationState.Unknown && !fromDisposal;
            if (existing.State != OperationState.Running && !supersedingUnknown) return;
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
                // in-memory record is now the only place this outcome exists. Tracked separately from
                // the scope mark, because clearing the mark does not make the record durable.
                _degradedScopes.Add(existing.Target);
                _unpersisted[id] = updated;
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
            finishedUtc = record.FinishedUtc?.ToString("O", CultureInfo.InvariantCulture), record.Code,
            configurationSnapshot = record.ConfigurationSnapshot
        });
        lock (_fileLock) {
            if (FailAppendForTests) {
                throw new IOException("injected evidence-append failure");
            }
            // A previous write may have torn mid-line, leaving the file without a trailing newline.
            // Appending straight onto it would concatenate two records into one unparseable line and
            // silently destroy the record being written as well as the one already damaged.
            bool needsSeparator = false;
            if (File.Exists(_evidencePath)) {
                using var probe = new FileStream(_evidencePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (probe.Length > 0) {
                    probe.Seek(-1, SeekOrigin.End);
                    needsSeparator = probe.ReadByte() != '\n';
                }
            }
            using var stream = new FileStream(_evidencePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            if (needsSeparator) {
                writer.WriteLine();
            }
            if (FailMidWriteForTests && kind == "end") {
                // Part-way through: a truncated record reaches the disk and the write then fails. The
                // torn line must never be readable as a terminal.
                writer.Write(line[..(line.Length / 2)]);
                writer.Flush();
                stream.Flush(true);
                throw new IOException("injected mid-write evidence failure");
            }
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
                root.GetProperty("Code").ValueKind == JsonValueKind.Null ? null : root.GetProperty("Code").GetString(),
                !root.TryGetProperty("configurationSnapshot", out var snapshot)
                    || snapshot.ValueKind == JsonValueKind.Null ? null : snapshot.GetString());
        }
        catch (Exception) { return null; }
    }

    // The defect, restored on demand: the flag moves before the work, so a concurrent Dispose observes
    // it, skips completion and releases ownership while the outcome is still unrecorded.
    private sealed class FlagFirstLease(OperationLedger ledger, string id) : IOperationLease {
        private int _reported;
        private int _released;

        public string Id { get; } = id;

        public void Complete(OperationState state, string? code = null) {
            if (Interlocked.Exchange(ref _reported, 1) == 0) ledger.Complete(Id, state, code);
        }

        public void Dispose() {
            Complete(OperationState.Failed, "lease-disposed-without-terminal");
            if (Interlocked.Exchange(ref _released, 1) == 0) ledger.ReleaseOwnership(Id);
        }
    }

    private sealed class SwapWindow(OperationLedger ledger, string scope) : IDisposable {
        private int _released;

        public void Dispose() {
            if (Interlocked.Exchange(ref _released, 1) == 0) ledger.ReleaseScope(scope);
        }
    }

    // Ownership release and outcome recording are serialised through one gate, and validation happens
    // BEFORE the single report is consumed. Two hazards made both necessary, both found by review:
    //  - a concurrent Dispose could observe the "already reported" flag that Complete had set on its
    //    way in, skip completion and release ownership while the outcome was still unrecorded;
    //  - an invalid Complete(Running/NotFound) consumed the flag before validation threw, after which
    //    nothing could ever complete the operation.
    private sealed class Lease : IOperationLease {
        private readonly OperationLedger _ledger;
        private readonly object _gate = new();
        private bool _reported;
        private bool _released;

        internal Lease(OperationLedger ledger, string id) {
            _ledger = ledger;
            Id = id;
        }

        public string Id { get; }

        /// <summary>Publishes the outcome. Does not end ownership — see <see cref="Dispose"/>.</summary>
        public void Complete(OperationState state, string? code = null) {
            // Validated first: a rejected call must not burn the one report this lease is allowed.
            if (state is OperationState.Running or OperationState.NotFound) {
                throw new ArgumentOutOfRangeException(nameof(state), state, "A terminal state is required.");
            }
            lock (_gate) {
                if (_reported) {
                    return;
                }
                _ledger.Complete(Id, state, code);   // recorded BEFORE the flag moves
                _reported = true;
            }
        }

        /// <summary>
        /// Ends ownership. Disposing without a published outcome is a defect, not a silent success, so
        /// a terminal is recorded first — under the same gate, so a concurrent completion cannot be
        /// skipped and then have its ownership pulled out from under it.
        /// </summary>
        public void Dispose() {
            lock (_gate) {
                if (!_reported) {
                    _ledger.Complete(Id, OperationState.Failed, "lease-disposed-without-terminal",
                        fromDisposal: true);
                    _reported = true;
                }
                if (!_released) {
                    _ledger.ReleaseOwnership(Id);
                    _released = true;
                }
            }
        }
    }
}
