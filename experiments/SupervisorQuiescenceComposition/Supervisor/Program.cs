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

// ── S8: failed V2 startup, bounded fallback to a restarted V1, recovery. Ordering C from the ──────────
// three-way comparison, the "smallest missing proof" kirillkrylov asked for: replacement readiness
// (ping/pong, no customer-side effect), failed V2 startup (bounded attempts), and a working fallback.
var s8 = await RunFallbackScenario(backendPath, "envA", fallbackSucceeds: true);
Check("S8 V2 fails to start twice; falling back to a restarted V1 recovers and serves new work",
    s8.Outcome == "Recovered" && s8.V2Attempts == 2 && s8.RecoveredPid is not null && s8.NewWorkSucceeded,
    new {
        outcome = s8.Outcome, v2Attempts = s8.V2Attempts, fallbackAttempts = s8.FallbackAttempts,
        recoveredPid = s8.RecoveredPid, newWorkSucceeded = s8.NewWorkSucceeded
    });

// ── S9: V2 fails AND the V1 fallback also fails (shared broken config/dependency) — the case ───────────
// kirillkrylov named explicitly: a fallback attempt is not a guarantee. Must report a definite
// "Unavailable" terminal after bounded attempts, never hang waiting for a window that cannot open.
var s9 = await RunFallbackScenario(backendPath, "envA", fallbackSucceeds: false);
Check("S9 V2 and the V1 fallback both fail; reports a definite Unavailable terminal after bounded attempts",
    s9.Outcome == "Unavailable" && s9.V2Attempts == 2 && s9.FallbackAttempts == 2 && s9.RecoveredPid is null,
    new { outcome = s9.Outcome, v2Attempts = s9.V2Attempts, fallbackAttempts = s9.FallbackAttempts });

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
            postLease.Dispose(); // Complete publishes the outcome; Dispose releases ownership -- separate since the ledger's P5 change
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
                    // Completed AND disposed immediately: a legitimately admitted attempt (before/after
                    // the window) must not linger as owned, or it would itself block quiescence (via
                    // retention, per the ledger's P5 change) and this task would deadlock the very window
                    // it exists to pressure-test.
                    IOperationLease pressureLease = ledger.Begin(target, "handover-pressure", v1);
                    pressureAttempts.Enqueue((duringWindow, false));
                    pressureLease.Complete(OperationState.Succeeded);
                    pressureLease.Dispose();
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

        // Replacement readiness, not just the kill: start V2 and confirm it via a no-side-effect ping
        // before the window is released, so admission closure is held through termination AND readiness.
        // Uses "ping"/"pong" rather than a real "start" — a readiness check must not run customer work
        // or mutate shared state before selection commits (kirillkrylov's point).
        v2 = StartBackend(backendPath, effectPath);
        var v2Accepted = new ConcurrentDictionary<string, byte>();
        var v2Leases = new ConcurrentDictionary<string, IOperationLease>();
        using var v2ReaderCts = new CancellationTokenSource();
        Task v2ReaderTask = PumpBackendOutput(v2, v2Leases, v2Accepted, v2ReaderCts.Token);

        if (!await TryHandshake(v2, v2Accepted, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("V2 never became ready");

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

// Ordering C from the discussion's three-way comparison: kill V1, try V2, and on V2's failure fall
// back to restarting V1 from its retained binary -- with bounded attempts on both legs and a definite
// terminal "Unavailable" outcome if every attempt is exhausted, never a hang.
static async Task<FallbackResult> RunFallbackScenario(string backendPath, string target, bool fallbackSucceeds) {
    string work = Directory.CreateTempSubdirectory("supervisor-fallback-").FullName;
    string effectPath = Path.Combine(work, "effect.log");
    var ledger = new OperationLedger(Path.Combine(work, "operations.jsonl"));

    Process v1 = StartBackend(backendPath, effectPath);
    var v1Accepted = new ConcurrentDictionary<string, byte>();
    Process? failedV2 = null;
    Process? recovered = null;
    var recoveredAccepted = new ConcurrentDictionary<string, byte>();
    try {
        var v1Leases = new ConcurrentDictionary<string, IOperationLease>();
        using var v1ReaderCts = new CancellationTokenSource();
        Task v1ReaderTask = PumpBackendOutput(v1, v1Leases, v1Accepted, v1ReaderCts.Token);
        if (!await TryHandshake(v1, v1Accepted, TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("V1 never became ready");

        IDisposable? window = null;
        var waitClock = Stopwatch.StartNew();
        while ((window = ledger.TryEnterSwapWindow(null)) is null) {
            await Task.Delay(25);
            if (waitClock.ElapsedMilliseconds > 20_000) throw new TimeoutException("swap window never opened");
        }

        v1.Kill(entireProcessTree: true);
        try { await v1.WaitForExitAsync(); }
        catch (InvalidOperationException) { /* already exited */ }
        v1ReaderCts.Cancel();
        try { await v1ReaderTask; }
        catch (OperationCanceledException) { }

        // Bounded attempts to bring up V2. Each is configured to fail, standing in for a genuinely
        // broken build rather than a transient hiccup.
        const int maxV2Attempts = 2;
        int v2Attempts;
        for (v2Attempts = 1; v2Attempts <= maxV2Attempts; v2Attempts++) {
            failedV2 = StartFailingBackend(backendPath);
            try { await failedV2.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { /* still counts as not ready within budget */ }
        }

        // Bounded fallback: attempt to restart V1 from its retained binary. `fallbackSucceeds` selects
        // which branch of the comparison this run measures -- kirillkrylov's point that the fallback
        // attempt can itself fail, including for the same reason V2 did. A reader must be pumping each
        // attempt's stdout *before* the handshake, or "pong" has nowhere to land and every attempt looks
        // like a failure regardless of whether the process actually started (the bug the first version
        // of this scenario had).
        const int maxFallbackAttempts = 2;
        int fallbackAttempts;
        bool fallbackReady = false;
        var recoveredLeases = new ConcurrentDictionary<string, IOperationLease>();
        CancellationTokenSource? recoveredReaderCts = null;
        Task? recoveredReaderTask = null;
        for (fallbackAttempts = 1; fallbackAttempts <= maxFallbackAttempts && !fallbackReady; fallbackAttempts++) {
            recovered = fallbackSucceeds ? StartBackend(backendPath, effectPath) : StartFailingBackend(backendPath);
            recoveredReaderCts = new CancellationTokenSource();
            recoveredReaderTask = PumpBackendOutput(recovered, recoveredLeases, recoveredAccepted, recoveredReaderCts.Token);
            fallbackReady = await TryHandshake(recovered, recoveredAccepted, TimeSpan.FromSeconds(5));
            if (!fallbackReady) {
                recoveredReaderCts.Cancel();
                try { await recoveredReaderTask; }
                catch (OperationCanceledException) { }
                try { recovered.Kill(entireProcessTree: true); }
                catch { /* best effort */ }
            }
        }

        // Released regardless of outcome: attempts are exhausted either way, and a window left held
        // forever after giving up would be a second bug layered on top of the first.
        window.Dispose();

        if (!fallbackReady) {
            return new FallbackResult("Unavailable", v2Attempts - 1, fallbackAttempts - 1, null, false);
        }

        // Confirm the recovered backend actually serves new work, not just answers a ping. Reuses the
        // reader already pumping its stdout from the successful attempt above.
        Process recoveredBackend = recovered!; // non-null: fallbackReady only becomes true after this assignment
        IOperationLease lease = ledger.Begin(target, "V1-recovered", recoveredBackend);
        recoveredLeases[lease.Id] = lease;
        await recoveredBackend.StandardInput.WriteLineAsync($"start {target} {lease.Id} 50 succeed");
        await recoveredBackend.StandardInput.FlushAsync();
        OperationRecord terminal = await WaitTerminal(ledger, lease.Id, TimeSpan.FromSeconds(10));
        recoveredReaderCts!.Cancel();
        try { await recoveredReaderTask!; }
        catch (OperationCanceledException) { }

        return new FallbackResult("Recovered", v2Attempts - 1, fallbackAttempts - 1, recoveredBackend.Id,
            terminal.State == OperationState.Succeeded);
    }
    finally {
        if (!v1.HasExited) { try { v1.Kill(entireProcessTree: true); } catch { /* best effort */ } }
        if (failedV2 is { HasExited: false }) { try { failedV2.Kill(entireProcessTree: true); } catch { /* best effort */ } }
        if (recovered is { HasExited: false }) { try { recovered.Kill(entireProcessTree: true); } catch { /* best effort */ } }
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

// Simulates a backend that cannot start at all (bad config, missing dependency) -- exits immediately
// with a nonzero code before reading anything. Used for the failed-startup/bounded-fallback scenario.
static Process StartFailingBackend(string backendDllPath) {
    var start = new ProcessStartInfo("dotnet") {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    start.ArgumentList.Add(backendDllPath);
    start.ArgumentList.Add("--fail-startup");
    return Process.Start(start) ?? throw new InvalidOperationException("backend did not start");
}

static async Task PumpBackendOutput(Process backend, ConcurrentDictionary<string, IOperationLease> leases,
    ConcurrentDictionary<string, byte> accepted, CancellationToken cancellationToken) {
    try {
        while (!cancellationToken.IsCancellationRequested) {
            string? line = await backend.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null) break; // backend exited, its stdout pipe closed
            if (line == "pong") { accepted["pong"] = 0; continue; }
            string[] parts = line.Split(' ');
            if (parts.Length == 2 && parts[0] == "accepted") {
                accepted[parts[1]] = 0;
            }
            else if (parts.Length == 3 && parts[0] == "done" && leases.TryRemove(parts[1], out var lease)) {
                // The outcome word the backend actually reported, not an assumed success -- a lease
                // completed as Succeeded regardless of what happened is exactly the false positive
                // kirillkrylov's negative control (S7) exists to catch.
                lease.Complete(parts[2] == "succeed" ? OperationState.Succeeded : OperationState.Failed);
                // Disposed immediately after: this probe's backend has no separate owned-cleanup phase,
                // so completion and ownership release happen together (unlike P5's case, where a runtime
                // legitimately holds the lease for cleanup after publishing its outcome).
                lease.Dispose();
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (IOException) { } // pipe torn down by the kill this task is racing against
}

// Sends "ping" and waits for PumpBackendOutput (already reading this backend's stdout) to observe
// "pong", rather than reading the stream directly -- a second concurrent reader would race the pump.
static async Task<bool> TryHandshake(Process backend, ConcurrentDictionary<string, byte> accepted, TimeSpan budget) {
    try {
        await backend.StandardInput.WriteLineAsync("ping");
        await backend.StandardInput.FlushAsync();
    }
    catch (IOException) { return false; }
    DateTime deadline = DateTime.UtcNow + budget;
    while (DateTime.UtcNow < deadline) {
        if (accepted.ContainsKey("pong")) return true;
        await Task.Delay(10);
    }
    return false;
}

enum GateMode { None, Global, PerTarget }

sealed record ScenarioResult(HashSet<string> Effects, long SwapMs, long WaitMs);

sealed record HandoverResult(bool RefusedDuringWindow, bool AdmittedAfterRelease);

sealed record SwapResult(int V1Pid, int V2Pid, bool V1EffectPresent, bool V2EffectMatchesOutcome,
    int PressureAttempts, int PressureRefused, int PressureAdmittedDuringWindow);

sealed record FallbackResult(string Outcome, int V2Attempts, int FallbackAttempts, int? RecoveredPid,
    bool NewWorkSucceeded);
