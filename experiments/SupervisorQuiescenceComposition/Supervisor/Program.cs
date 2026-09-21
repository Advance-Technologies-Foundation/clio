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

// S1-S4 are drain-before-termination measurements, not a swap proof: they kill the backend and check
// what survived, but never bring up a replacement. S6 below is the actual swap proof, per
// kirillkrylov's review: https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541164

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

// ── S6: the actual swap proof — real V1->V2 respawn, admission closure held through readiness, ─────────
// concurrent handover pressure against the outgoing backend, not just a single sampled attempt.
var s6 = await RunSwapScenario(backendPath, "envA", 400, postHandoverOutcome: "succeed");
Check("S6 a real V1->V2 respawn preserves V1's operation, serves a new one on a different PID, "
    + "and refuses every concurrent admission attempt during the handover",
    s6.V1Pid != s6.V2Pid && s6.V1EffectPresent && s6.V2EffectMatchesOutcome
        && s6.PressureAttempts > 0 && s6.PressureAdmittedDuringWindow == 0,
    new {
        v1Pid = s6.V1Pid, v2Pid = s6.V2Pid, v1EffectPresent = s6.V1EffectPresent,
        v2EffectPresent = s6.V2EffectMatchesOutcome, pressureAttempts = s6.PressureAttempts,
        pressureRefused = s6.PressureRefused, pressureAdmittedDuringWindow = s6.PressureAdmittedDuringWindow
    });

// ── S7: negative control for S6's oracle — kirillkrylov demonstrated that changing the post-handover ────
// outcome to "fail" still passed 6/6, because PumpBackendOutput ignored the reported outcome and the
// effect check matched any line ending in the right PID (which the readiness probe's own line also did).
// Both are fixed; this proves the fix by requiring the suite to now correctly fail-and-report a failure.
var s7 = await RunSwapScenario(backendPath, "envA", 400, postHandoverOutcome: "fail");
Check("S7 negative control: a failing post-handover operation is reported Failed, "
    + "and no effect line is falsely recorded for it",
    s7.V1EffectPresent && s7.V2EffectMatchesOutcome,
    new {
        v1Pid = s7.V1Pid, v2Pid = s7.V2Pid, v1EffectPresent = s7.V1EffectPresent,
        v2EffectCorrectlyAbsent = s7.V2EffectMatchesOutcome
    });

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
    try {
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

        // Wait for every operation to be acknowledged before deciding how to swap, so the naive and
        // gated scenarios diverge only on the gating decision, not on whether the response had landed.
        // Asserted, not silently fallen through: a missed ack invalidates the comparison, not passes it.
        var ackDeadline = DateTime.UtcNow.AddSeconds(5);
        while (accepted.Count < ops.Length && DateTime.UtcNow < ackDeadline) await Task.Delay(10);
        if (accepted.Count < ops.Length)
            throw new TimeoutException($"backend acknowledged {accepted.Count}/{ops.Length} operations");

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
    finally {
        // Startup/drain can throw before the normal kill path runs; never leak the child in that case.
        if (!backend.HasExited) { try { backend.Kill(entireProcessTree: true); } catch { /* best effort */ } }
    }
}

static async Task<HandoverResult> RunHandoverScenario(string backendPath, string target, int workMs) {
    string work = Directory.CreateTempSubdirectory("supervisor-handover-").FullName;
    string effectPath = Path.Combine(work, "effect.log");
    var ledger = new OperationLedger(Path.Combine(work, "operations.jsonl"));

    using var backend = StartBackend(backendPath, effectPath);
    try {
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
        if (accepted.Count < 1) throw new TimeoutException("backend never acknowledged the operation");

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
    finally {
        if (!backend.HasExited) { try { backend.Kill(entireProcessTree: true); } catch { /* best effort */ } }
    }
}

static async Task<SwapResult> RunSwapScenario(string backendPath, string target, int workMs,
    string postHandoverOutcome = "succeed") {
    string work = Directory.CreateTempSubdirectory("supervisor-swap-").FullName;
    string effectPath = Path.Combine(work, "effect.log");
    var ledger = new OperationLedger(Path.Combine(work, "operations.jsonl"));

    Process v1 = StartBackend(backendPath, effectPath);
    Process? v2 = null;
    try {
        var v1Accepted = new ConcurrentDictionary<string, byte>();
        var v1Leases = new ConcurrentDictionary<string, IOperationLease>();
        using var v1ReaderCts = new CancellationTokenSource();
        Task v1ReaderTask = PumpBackendOutput(v1, v1Leases, v1Accepted, v1ReaderCts.Token);

        IOperationLease lease = ledger.Begin(target, "V1", v1);
        v1Leases[lease.Id] = lease;
        await v1.StandardInput.WriteLineAsync($"start {target} {lease.Id} {workMs} succeed");
        await v1.StandardInput.FlushAsync();

        var ackDeadline = DateTime.UtcNow.AddSeconds(5);
        while (v1Accepted.Count < 1 && DateTime.UtcNow < ackDeadline) await Task.Delay(10);
        if (v1Accepted.Count < 1) throw new TimeoutException("V1 never acknowledged the operation");

        // Concurrent handover pressure: repeatedly attempt to admit new work for the same target,
        // starting before the window is even requested. Attempts are tagged with whether a window was
        // actually held at that instant, because only those must be refused — an attempt that lands
        // before acquisition is legitimately admitted, and conflating the two phases would over-claim
        // what this measures. None may be silently accepted into the outgoing backend during the window.
        int windowHeldFlag = 0;
        var pressureAttempts = new ConcurrentQueue<(bool DuringWindow, bool Refused)>();
        using var pressureCts = new CancellationTokenSource();
        Task pressureTask = Task.Run(async () => {
            while (!pressureCts.IsCancellationRequested) {
                bool duringWindow = Volatile.Read(ref windowHeldFlag) == 1;
                try {
                    // Completed immediately: a legitimately admitted attempt (before/after the window)
                    // must not linger as Running, or it would itself block quiescence and this task would
                    // deadlock the very window it exists to pressure-test.
                    IOperationLease pressureLease = ledger.Begin(target, "handover-pressure", v1);
                    pressureAttempts.Enqueue((duringWindow, false));
                    pressureLease.Complete(OperationState.Succeeded);
                }
                catch (SwapWindowHeldException) { pressureAttempts.Enqueue((duringWindow, true)); }
                await Task.Delay(5);
            }
        });

        // Global, not per-target: this is a host-level swap, and S3 already established that gating on
        // one target does not protect another sharing the same host.
        IDisposable? window = null;
        var waitClock = Stopwatch.StartNew();
        while ((window = ledger.TryEnterSwapWindow(null)) is null) {
            await Task.Delay(25);
            if (waitClock.ElapsedMilliseconds > 20_000) throw new TimeoutException("swap window never opened");
        }
        Volatile.Write(ref windowHeldFlag, 1);

        int v1Pid = v1.Id;
        v1.Kill(entireProcessTree: true);
        try { await v1.WaitForExitAsync(); }
        catch (InvalidOperationException) { /* already exited */ }
        v1ReaderCts.Cancel();
        try { await v1ReaderTask; }
        catch (OperationCanceledException) { }

        // Replacement readiness, not just the kill: start V2 and confirm it accepts a command before the
        // window is released, so admission closure is held through termination AND readiness.
        v2 = StartBackend(backendPath, effectPath);
        var v2Accepted = new ConcurrentDictionary<string, byte>();
        var v2Leases = new ConcurrentDictionary<string, IOperationLease>();
        using var v2ReaderCts = new CancellationTokenSource();
        Task v2ReaderTask = PumpBackendOutput(v2, v2Leases, v2Accepted, v2ReaderCts.Token);

        string readinessId = Guid.NewGuid().ToString("n");
        await v2.StandardInput.WriteLineAsync($"start __readiness__ {readinessId} 1 succeed");
        await v2.StandardInput.FlushAsync();
        var readyDeadline = DateTime.UtcNow.AddSeconds(5);
        while (!v2Accepted.ContainsKey(readinessId) && DateTime.UtcNow < readyDeadline) await Task.Delay(10);
        if (!v2Accepted.ContainsKey(readinessId)) throw new TimeoutException("V2 never became ready");

        Volatile.Write(ref windowHeldFlag, 0);
        pressureCts.Cancel();
        try { await pressureTask; }
        catch (OperationCanceledException) { }
        window.Dispose();

        // Post-handover: the same target accepts new work again, served by the new process. The outcome
        // is parameterized so S7 can run the identical path with a failing operation as a negative
        // control -- kirillkrylov's finding was that a fixed "succeed" here made the oracle unfalsifiable.
        IOperationLease postLease = ledger.Begin(target, "V2", v2);
        v2Leases[postLease.Id] = postLease;
        await v2.StandardInput.WriteLineAsync($"start {target} {postLease.Id} 50 {postHandoverOutcome}");
        await v2.StandardInput.FlushAsync();
        OperationRecord postTerminal = await WaitTerminal(ledger, postLease.Id, TimeSpan.FromSeconds(10));
        OperationState expectedState = postHandoverOutcome == "succeed"
            ? OperationState.Succeeded : OperationState.Failed;
        if (postTerminal.State != expectedState)
            throw new InvalidOperationException(
                $"post-handover operation reached {postTerminal.State}, expected {expectedState}");

        int v2Pid = v2.Id;
        v2.Kill(entireProcessTree: true);
        try { await v2.WaitForExitAsync(); }
        catch (InvalidOperationException) { /* already exited */ }
        v2ReaderCts.Cancel();
        try { await v2ReaderTask; }
        catch (OperationCanceledException) { }

        // Exact match on target:opId:done-by-pid-<pid>, not a PID suffix -- a suffix match is a false
        // positive here, since V2 also writes the unrelated __readiness__ probe's own effect line under
        // the same PID. This is the fix for the false positive kirillkrylov demonstrated.
        await Task.Delay(200);
        string[] effectLines = File.Exists(effectPath) ? await File.ReadAllLinesAsync(effectPath) : [];
        bool v1EffectPresent = effectLines.Contains($"{target}:{lease.Id}:done-by-pid-{v1Pid}");
        bool v2EffectExpected = postHandoverOutcome == "succeed";
        bool v2EffectMatchesOutcome = v2EffectExpected
            ? effectLines.Contains($"{target}:{postLease.Id}:done-by-pid-{v2Pid}")
            : !effectLines.Contains($"{target}:{postLease.Id}:done-by-pid-{v2Pid}");

        var attempts = pressureAttempts.ToArray();
        var duringWindow = attempts.Where(a => a.DuringWindow).ToArray();
        return new SwapResult(v1Pid, v2Pid, v1EffectPresent, v2EffectMatchesOutcome,
            duringWindow.Length, duringWindow.Count(a => a.Refused), duringWindow.Count(a => !a.Refused));
    }
    finally {
        if (!v1.HasExited) { try { v1.Kill(entireProcessTree: true); } catch { /* best effort */ } }
        if (v2 is { HasExited: false }) { try { v2.Kill(entireProcessTree: true); } catch { /* best effort */ } }
    }
}

static async Task<OperationRecord> WaitTerminal(IOperationLedger ledger, string id, TimeSpan budget) {
    DateTime deadline = DateTime.UtcNow + budget;
    while (DateTime.UtcNow < deadline) {
        OperationRecord record = ledger.Query(id);
        if (record.State != OperationState.Running) return record;
        await Task.Delay(20);
    }
    throw new TimeoutException($"operation {id} did not reach a terminal state in time");
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
                // The outcome word the backend actually reported, not an assumed success -- a lease
                // completed as Succeeded regardless of what happened is exactly the false positive
                // kirillkrylov's negative control (S7) exists to catch.
                lease.Complete(parts[2] == "succeed" ? OperationState.Succeeded : OperationState.Failed);
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (IOException) { } // pipe torn down by the kill this task is racing against
}

enum GateMode { None, Global, PerTarget }

sealed record ScenarioResult(HashSet<string> Effects, long SwapMs, long WaitMs);

sealed record HandoverResult(bool RefusedDuringWindow, bool AdmittedAfterRelease);

sealed record SwapResult(int V1Pid, int V2Pid, bool V1EffectPresent, bool V2EffectMatchesOutcome,
    int PressureAttempts, int PressureRefused, int PressureAdmittedDuringWindow);
