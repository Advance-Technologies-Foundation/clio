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

    /// <summary>Opens a ledger over an evidence file, recovering any prior process's unfinished operations.</summary>
    public OperationLedger(string evidencePath) {
        _evidencePath = evidencePath;
        _recovered = Recover(evidencePath);
    }

    /// <summary>Operations recovered from a previous process that have no recorded outcome.</summary>
    public IReadOnlyCollection<string> RecoveredUnknown =>
        _recovered.Where(p => p.Value.State == OperationState.Unknown).Select(p => p.Key).ToArray();

    /// <inheritdoc />
    public IReadOnlyCollection<string> Running =>
        _live.Where(p => p.Value.State == OperationState.Running).Select(p => p.Key).ToArray();

    /// <inheritdoc />
    public bool IsQuiescent(string? target = null) => !_live.Values.Any(r =>
        r.State == OperationState.Running &&
        (target is null || string.Equals(r.Target, target, StringComparison.Ordinal)));

    /// <inheritdoc />
    public IOperationLease Begin(string target, string runtimeVersion, object owner) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        string id = Guid.NewGuid().ToString("n");
        var record = new OperationRecord(id, target, DateTimeOffset.UtcNow, runtimeVersion, OperationState.Running);
        _live[id] = record;
        _owners[id] = owner;                       // retention: the runtime cannot be retired under it
        Append("begin", record);
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
        var updated = _live.AddOrUpdate(id,
            _ => new OperationRecord(id, string.Empty, DateTimeOffset.UtcNow, string.Empty, state,
                DateTimeOffset.UtcNow, code),
            (_, existing) => existing.State == OperationState.Running
                ? existing with { State = state, FinishedUtc = DateTimeOffset.UtcNow, Code = code }
                : existing);                        // exactly once; a second report never overwrites
        if (updated.State == state && updated.Code == code) Append("end", updated);
        _owners.TryRemove(id, out _);               // release retention so the runtime may be retired
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

    private sealed class Lease(OperationLedger ledger, string id) : IOperationLease {
        private int _reported;

        public string Id { get; } = id;

        public void Complete(OperationState state, string? code = null) {
            if (Interlocked.Exchange(ref _reported, 1) == 0) ledger.Complete(Id, state, code);
        }

        // Disposing without a terminal state is a defect, not a silent success: record it as such
        // rather than leaving the caller polling a Running record that will never move.
        public void Dispose() => Complete(OperationState.Failed, "lease-disposed-without-terminal");
    }
}
