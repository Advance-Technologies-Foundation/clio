using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Clio10.DetachedOperations;
using Clio10.DetachedOperations.Host;

// Composes two legs that each already have evidence but have never been run together:
//   - transport continuity: a supervisor replacing a real child process without dropping its own
//     caller (measured separately: https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539341)
//   - execution quiescence: E3's OperationLedger and TryEnterSwapWindow
// This probe drives a REAL separate backend process (not an in-process AssemblyLoadContext swap, which
// is what E3's A1-A5 measure) through a supervisor that gates a real process replacement on the ledger.
if (args.Length < 1) { Console.Error.WriteLine("usage: supervisor <backendDllPath>"); return 2; }
string backendPath = args[0];
var observations = new List<object>();
bool failed = false;
void Check(string name, bool ok, object detail) {
    observations.Add(new { name, passed = ok, detail });
    if (!ok) failed = true;
}

// ── S1: naive swap, no gating — the control. Reproduces the create-app-section-shaped loss. ───────────
var s1 = await RunScenario(backendPath, GateMode.None, null, ("envA", 1200));
Check("S1 naive swap loses the detached operation's effect (control, reproduces the known failure)",
    !s1.Effects.Contains("envA"), new { effects = s1.Effects, swapMs = s1.SwapMs, waitedMs = s1.WaitMs });

// ── S2: global gate — the swap waits for quiescence before touching the process. ───────────────────────
var s2 = await RunScenario(backendPath, GateMode.Global, null, ("envA", 1200));
Check("S2 a globally gated swap preserves the detached operation's effect",
    s2.Effects.Contains("envA"), new { effects = s2.Effects, swapMs = s2.SwapMs, waitedMs = s2.WaitMs });

// ── S3: per-target gate on the busy target does not protect a DIFFERENT target on the same host. ──────
// This is the corrected understanding from the discussion made concrete: one host serves every target,
// so a target-scoped window is the right predicate for a runtime swap but not license for a host swap.
var s3 = await RunScenario(backendPath, GateMode.PerTarget, "envA", ("envA", 300), ("envB", 1200));
Check("S3 a per-target gate on envA does not protect envB's in-flight work on the same host",
    s3.Effects.Contains("envA") && !s3.Effects.Contains("envB"),
    new { effects = s3.Effects, swapMs = s3.SwapMs, waitedMs = s3.WaitMs });

// ── S4: global gate protects every target the host owns, at the cost of waiting for the slowest one. ──
var s4 = await RunScenario(backendPath, GateMode.Global, null, ("envA", 300), ("envB", 1200));
Check("S4 a global gate protects every target the host owns",
    s4.Effects.Contains("envA") && s4.Effects.Contains("envB"),
    new { effects = s4.Effects, swapMs = s4.SwapMs, waitedMs = s4.WaitMs });

// ── S5: work arriving DURING the handover must be refused, not silently admitted — the case ────────────
// kirillkrylov's review found the admission barrier failed on, now checked against the repaired ledger
// (Alexandr-Kravchuk/detached-operation-probe@551f25c92538) through this probe's own composed path,
// not just directly against the ledger the way E3's own A5b/A5h already do.
var s5 = await RunHandoverScenario(backendPath, "envA", 300);
Check("S5 a new operation for the gated scope is refused while the window is held, and admitted again once released",
    s5.RefusedDuringWindow && s5.AdmittedAfterRelease,
    new { refusedDuringWindow = s5.RefusedDuringWindow, admittedAfterRelease = s5.AdmittedAfterRelease });

Console.WriteLine(JsonSerializer.Serialize(new {
    os = Environment.OSVersion.VersionString,
    framework = Environment.Version.ToString(),
    cases = observations
}, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

// ────────────────────────────────────────────────────────────────────────────────────────────────────

static async Task<ScenarioResult> RunScenario(string backendPath, GateMode mode, string? gateTarget,
    params (string Target, int WorkMs)[] ops) {
    string work = Directory.CreateTempSubdirectory("supervisor-quiescence-").FullName;
    string effectPath = Path.Combine(work, "effect.log");
    var ledger = new OperationLedger(Path.Combine(work, "operations.jsonl"));

    using var backend = StartBackend(backendPath, effectPath);
    var accepted = new ConcurrentDictionary<string, byte>();
    var leases = new ConcurrentDictionary<string, IOperationLease>();
    using var readerCts = new CancellationTokenSource();
    Task readerTask = PumpBackendOutput(backend, leases, accepted, readerCts.Token);

    foreach ((string target, int workMs) in ops) {
        IOperationLease lease = ledger.Begin(target, "V1", backend);
        leases[lease.Id] = lease;
        await backend.StandardInput.WriteLineAsync($"start {target} {lease.Id} {workMs} succeed");
        await backend.StandardInput.FlushAsync();
    }

    // Wait for every operation to be acknowledged before deciding how to swap, so the naive and gated
    // scenarios diverge only on the gating decision, not on whether the response had already landed.
    var ackDeadline = DateTime.UtcNow.AddSeconds(5);
    while (accepted.Count < ops.Length && DateTime.UtcNow < ackDeadline) await Task.Delay(10);

    var swapClock = Stopwatch.StartNew();
    long waitMs = 0;
    IDisposable? window = null;
    if (mode != GateMode.None) {
        string? scope = mode == GateMode.PerTarget ? gateTarget : null;
        var waitClock = Stopwatch.StartNew();
        while ((window = ledger.TryEnterSwapWindow(scope)) is null) {
            await Task.Delay(25);
            if (waitClock.ElapsedMilliseconds > 20_000) throw new TimeoutException("swap window never opened");
        }
        waitMs = waitClock.ElapsedMilliseconds;
    }

    try {
        backend.Kill(entireProcessTree: true);
        try { await backend.WaitForExitAsync(); }
        catch (InvalidOperationException) { /* already exited */ }
    }
    finally { window?.Dispose(); }
    long swapMs = swapClock.ElapsedMilliseconds;

    readerCts.Cancel();
    try { await readerTask; }
    catch (OperationCanceledException) { }

    // A beat for any effect write that landed a moment before the kill to reach disk.
    await Task.Delay(200);

    var effects = new HashSet<string>(StringComparer.Ordinal);
    if (File.Exists(effectPath))
        foreach (string effectLine in await File.ReadAllLinesAsync(effectPath))
            effects.Add(effectLine.Split(':')[0]);

    return new ScenarioResult(effects, swapMs, waitMs);
}

static async Task<HandoverResult> RunHandoverScenario(string backendPath, string target, int workMs) {
    string work = Directory.CreateTempSubdirectory("supervisor-handover-").FullName;
    string effectPath = Path.Combine(work, "effect.log");
    var ledger = new OperationLedger(Path.Combine(work, "operations.jsonl"));

    using var backend = StartBackend(backendPath, effectPath);
    var accepted = new ConcurrentDictionary<string, byte>();
    var leases = new ConcurrentDictionary<string, IOperationLease>();
    using var readerCts = new CancellationTokenSource();
    Task readerTask = PumpBackendOutput(backend, leases, accepted, readerCts.Token);

    IOperationLease lease = ledger.Begin(target, "V1", backend);
    leases[lease.Id] = lease;
    await backend.StandardInput.WriteLineAsync($"start {target} {lease.Id} {workMs} succeed");
    await backend.StandardInput.FlushAsync();

    var ackDeadline = DateTime.UtcNow.AddSeconds(5);
    while (accepted.Count < 1 && DateTime.UtcNow < ackDeadline) await Task.Delay(10);

    IDisposable? window = null;
    var waitClock = Stopwatch.StartNew();
    while ((window = ledger.TryEnterSwapWindow(null)) is null) {
        await Task.Delay(25);
        if (waitClock.ElapsedMilliseconds > 20_000) throw new TimeoutException("swap window never opened");
    }

    // The moment being tested: a new request for the gated target arrives while the window is held —
    // it must be refused, not queued silently and not admitted underneath the swap decision.
    bool refusedDuringWindow;
    try {
        ledger.Begin(target, "handover-attempt", backend);
        refusedDuringWindow = false;
    }
    catch (SwapWindowHeldException) { refusedDuringWindow = true; }

    window.Dispose();

    bool admittedAfterRelease;
    try {
        IOperationLease postLease = ledger.Begin(target, "V2", backend);
        postLease.Complete(OperationState.Succeeded);
        admittedAfterRelease = true;
    }
    catch (SwapWindowHeldException) { admittedAfterRelease = false; }

    backend.Kill(entireProcessTree: true);
    try { await backend.WaitForExitAsync(); }
    catch (InvalidOperationException) { /* already exited */ }
    readerCts.Cancel();
    try { await readerTask; }
    catch (OperationCanceledException) { }

    return new HandoverResult(refusedDuringWindow, admittedAfterRelease);
}

static Process StartBackend(string backendDllPath, string effectPath) {
    var start = new ProcessStartInfo("dotnet") {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    start.ArgumentList.Add(backendDllPath);
    start.ArgumentList.Add(effectPath);
    return Process.Start(start) ?? throw new InvalidOperationException("backend did not start");
}

static async Task PumpBackendOutput(Process backend, ConcurrentDictionary<string, IOperationLease> leases,
    ConcurrentDictionary<string, byte> accepted, CancellationToken cancellationToken) {
    try {
        while (!cancellationToken.IsCancellationRequested) {
            string? line = await backend.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null) break; // backend exited, its stdout pipe closed
            string[] parts = line.Split(' ');
            if (parts.Length == 2 && parts[0] == "accepted") {
                accepted[parts[1]] = 0;
            }
            else if (parts.Length == 3 && parts[0] == "done" && leases.TryRemove(parts[1], out var lease)) {
                lease.Complete(OperationState.Succeeded);
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (IOException) { } // pipe torn down by the kill this task is racing against
}

enum GateMode { None, Global, PerTarget }

sealed record ScenarioResult(HashSet<string> Effects, long SwapMs, long WaitMs);

sealed record HandoverResult(bool RefusedDuringWindow, bool AdmittedAfterRelease);
