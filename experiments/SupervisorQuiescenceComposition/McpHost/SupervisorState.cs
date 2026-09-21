using System.Collections.Concurrent;
using System.Diagnostics;
using Clio10.DetachedOperations;
using Clio10.DetachedOperations.Host;

namespace Clio10.SupervisorComposition.McpHost;

/// <summary>
/// Owns the ledger and the currently-running backend process behind the MCP tool surface. Intentionally
/// narrow: this probe's job is the client-correlation claim (an operation id survives a real swap over a
/// real, never-reconnected MCP connection), not re-proving S1-S10's concurrency/pressure claims, which
/// already have their own evidence. Duplicates a few small helpers from ../Supervisor/Program.cs rather
/// than sharing a library between the two probes -- acceptable for a probe, not a production boundary.
/// </summary>
public sealed class SupervisorState {
    private readonly string _backendDllPath;
    private readonly string _effectPath;
    private readonly OperationLedger _ledger;
    private readonly ConcurrentDictionary<string, IOperationLease> _leases = new();
    private ConcurrentDictionary<string, byte> _accepted = new();
    private Process _backend;
    private CancellationTokenSource _readerCts;
    private Task _readerTask;
    private readonly SemaphoreSlim _swapGate = new(1, 1);

    public SupervisorState(string backendDllPath, string workDir) {
        _backendDllPath = backendDllPath;
        _effectPath = Path.Combine(workDir, "effect.log");
        _ledger = new OperationLedger(Path.Combine(workDir, "operations.jsonl"));
        (_backend, _readerCts, _readerTask) = StartBackendAndPump();
    }

    /// <summary>Starts detached work and returns its host-issued id -- opaque to the caller by design.</summary>
    public async Task<string> StartOperationAsync(string target, int workMs, string outcome) {
        IOperationLease lease = _ledger.Begin(target, "V1", _backend);
        _leases[lease.Id] = lease;
        await _backend.StandardInput.WriteLineAsync($"start {target} {lease.Id} {workMs} {outcome}");
        await _backend.StandardInput.FlushAsync();
        return lease.Id;
    }

    /// <summary>Truthful state for an id: Running, Succeeded, Failed, Unknown, or NotFound.</summary>
    public string QueryOperation(string id) => _ledger.Query(id).State.ToString();

    /// <summary>Which real backend process PID is currently serving, for the test driver to verify a swap actually happened.</summary>
    public int CurrentBackendPid => _backend.Id;

    /// <summary>
    /// Gates a real backend replacement on the ledger (reserve-then-drain, same primitive S2/S4/S6-S9
    /// migrated to after Alexandr's starvation proof), then swaps to a fresh backend process.
    /// </summary>
    public async Task<string> TriggerSwapAsync(string? target, TimeSpan drainBudget, bool force = false) {
        await _swapGate.WaitAsync();
        try {
            IDisposable? reservation = _ledger.TryReserveAdmission(target);
            if (reservation is null) return "already-swapping";
            DateTime deadline = DateTime.UtcNow + drainBudget;
            while (!force && !_ledger.IsQuiescent(target)) {
                if (DateTime.UtcNow >= deadline) { reservation.Dispose(); return "deferred"; }
                await Task.Delay(25);
            }
            try {
                int oldPid = _backend.Id;
                _backend.Kill(entireProcessTree: true);
                try { await _backend.WaitForExitAsync(); }
                catch (InvalidOperationException) { /* already exited */ }
                _readerCts.Cancel();
                try { await _readerTask; }
                catch (OperationCanceledException) { }

                (_backend, _readerCts, _readerTask) = StartBackendAndPump();
                if (!await TryHandshakeAsync(TimeSpan.FromSeconds(5))) return "swap-failed: new backend not ready";
                return $"swapped {oldPid} -> {_backend.Id}";
            }
            finally { reservation.Dispose(); }
        }
        finally { _swapGate.Release(); }
    }

    private (Process, CancellationTokenSource, Task) StartBackendAndPump() {
        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(_backendDllPath);
        start.ArgumentList.Add(_effectPath);
        Process backend = Process.Start(start) ?? throw new InvalidOperationException("backend did not start");
        var accepted = new ConcurrentDictionary<string, byte>();
        _accepted = accepted;
        var cts = new CancellationTokenSource();
        Task pump = PumpAsync(backend, cts.Token, accepted);
        return (backend, cts, pump);
    }

    private async Task PumpAsync(Process backend, CancellationToken token, ConcurrentDictionary<string, byte> accepted) {
        try {
            while (!token.IsCancellationRequested) {
                string? line = await backend.StandardOutput.ReadLineAsync(token);
                if (line is null) break;
                if (line == "pong") { accepted["pong"] = 0; continue; }
                string[] parts = line.Split(' ');
                if (parts.Length == 2 && parts[0] == "accepted") { accepted[parts[1]] = 0; }
                else if (parts.Length == 3 && parts[0] == "done" && _leases.TryRemove(parts[1], out var lease)) {
                    lease.Complete(parts[2] == "succeed" ? OperationState.Succeeded : OperationState.Failed);
                    lease.Dispose();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private async Task<bool> TryHandshakeAsync(TimeSpan budget) {
        try {
            await _backend.StandardInput.WriteLineAsync("ping");
            await _backend.StandardInput.FlushAsync();
        }
        catch (IOException) { return false; }
        DateTime deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline) {
            if (_accepted.ContainsKey("pong")) return true;
            await Task.Delay(10);
        }
        return false;
    }
}
