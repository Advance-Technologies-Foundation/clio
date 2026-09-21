using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Clio10.DetachedOperations;
using Clio10.DetachedOperations.Host;

// Two modes. "child" exists only so the parent can kill a process that genuinely has an operation
// in flight; everything else runs in "run".
if (args.Length >= 2 && args[0] == "child") { Child(args[1]); return 0; }
if (args.Length < 3) { Console.Error.WriteLine("usage: host run <v1dir> <v2dir>"); return 2; }

string v1Dir = Path.GetFullPath(args[1]), v2Dir = Path.GetFullPath(args[2]);
string work = Directory.CreateTempSubdirectory("detached-ops-").FullName;
string evidence = Path.Combine(work, "operations.jsonl");
string effect = Path.Combine(work, "effect.log");
var observations = new List<object>();
bool failed = false;

void Check(string name, bool ok, object detail) {
    observations.Add(new { name, passed = ok, detail });
    if (!ok) failed = true;
}

var ledger = new OperationLedger(evidence);

// A1/A2 run inside their own frame so no local keeps the V1 release alive afterwards.
var (v1Weak, v2Ctx, v2) = await PhaseUpdate(ledger, v1Dir, v2Dir, effect, Check);

for (int i = 0; i < 40 && v1Weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(50); }
Check("R1 the V1 release becomes collectible once no lease retains it",
    !v1Weak.IsAlive, new { collected = !v1Weak.IsAlive });

// ── A3: failure and cancellation reach a terminal state rather than going silent ────────────────────
string failId = v2.StartDetached(ledger, "envA", effect, 200, "fail", CancellationToken.None);
var failTerminal = await WaitTerminal(ledger, failId, TimeSpan.FromSeconds(20));
Check("A3a a failing operation reports Failed, not silence",
    failTerminal.State == OperationState.Failed,
    new { state = failTerminal.State.ToString(), code = failTerminal.Code });

using var cancelSource = new CancellationTokenSource();
string cancelId = v2.StartDetached(ledger, "envB", effect, 10_000, "succeed", cancelSource.Token);
await Task.Delay(250);
await cancelSource.CancelAsync();
var cancelTerminal = await WaitTerminal(ledger, cancelId, TimeSpan.FromSeconds(20));
Check("A3b a cancelled operation reports Cancelled, not silence",
    cancelTerminal.State == OperationState.Cancelled,
    new { state = cancelTerminal.State.ToString(), code = cancelTerminal.Code });

Check("A3c neither the failure nor the cancellation added an external effect",
    File.ReadAllLines(effect).Length == 1, new { effectLines = File.ReadAllLines(effect).Length });

// ── A4: process loss reports uncertainty, never "nothing was started" ───────────────────────────────
string lostEvidence = Path.Combine(work, "lost.jsonl");
string lostId = await StartThenKillChild(v1Dir, lostEvidence, Path.Combine(work, "lost-effect.log"));
var afterLoss = new OperationLedger(lostEvidence);
var lostRecord = afterLoss.Query(lostId);
var neverIssued = afterLoss.Query("0000000000000000deadbeefdeadbeef");

Check("A4a an operation lost with its process reports Unknown, with its target preserved",
    lostRecord.State == OperationState.Unknown && lostRecord.Code == "history-unavailable"
        && lostRecord.Target == "envA",
    new { state = lostRecord.State.ToString(), code = lostRecord.Code, target = lostRecord.Target,
          ownedBy = lostRecord.RuntimeVersion });

Check("A4b an identifier that was never issued is still reported as NotFound",
    neverIssued.State == OperationState.NotFound, new { state = neverIssued.State.ToString() });

Check("A4c the recovered host surfaces the lost operation rather than reporting a clean slate",
    afterLoss.RecoveredUnknown.Count == 1, new { recoveredUnknown = afterLoss.RecoveredUnknown.Count });

// ── A5: observing quiescence is not enough to act on it ────────────────────────────────────────────
string raceEffect = Path.Combine(work, "race-effect.log");

// Control first: show the check-then-act gap is real, not hypothetical.
bool observedQuiescent = ledger.IsQuiescent("envD");
string raceId = v2.StartDetached(ledger, "envD", raceEffect, 1200, "succeed", CancellationToken.None);
Check("A5a control: reading quiescence does not hold it — work starts in the check-then-act gap",
    observedQuiescent && !ledger.IsQuiescent("envD"),
    new { observedQuiescent, quiescentAfterwards = ledger.IsQuiescent("envD"),
          note = "a swap acting on the earlier read would destroy work that began after it" });

Check("A5b a swap window is refused while the target has work in flight",
    ledger.TryEnterSwapWindow("envD") is null, new { held = false });

await WaitTerminal(ledger, raceId, TimeSpan.FromSeconds(20));

// Now the same sequence with the window held.
bool windowTaken, newWorkRefused = false, otherTargetUnaffected;
using (IDisposable? window = ledger.TryEnterSwapWindow("envD")) {
    windowTaken = window is not null;
    try { v2.StartDetached(ledger, "envD", raceEffect, 200, "succeed", CancellationToken.None); }
    catch (SwapWindowHeldException) { newWorkRefused = true; }
    string otherId = v2.StartDetached(ledger, "envE", raceEffect, 200, "succeed", CancellationToken.None);
    otherTargetUnaffected = otherId.Length > 0;
    await WaitTerminal(ledger, otherId, TimeSpan.FromSeconds(20));
}

Check("A5c holding a swap window closes the gap for its scope and leaves other targets alone",
    windowTaken && newWorkRefused && otherTargetUnaffected,
    new { windowTaken, newWorkRefused, otherTargetUnaffected });

// A5d tests the behaviour it is named for: real work, started after release, that actually completes.
string afterReleaseId = v2.StartDetached(ledger, "envD", raceEffect, 150, "succeed", CancellationToken.None);
var afterRelease = await WaitTerminal(ledger, afterReleaseId, TimeSpan.FromSeconds(20));
Check("A5d the scope accepts real work again once the window is released",
    afterRelease.State == OperationState.Succeeded,
    new { state = afterRelease.State.ToString() });

// A5e: exclusion must hold in both directions, not only global-blocks-global.
using (IDisposable? globalWindow = ledger.TryEnterSwapWindow()) {
    Check("A5e a target window is refused while a global window is held",
        globalWindow is not null && ledger.TryEnterSwapWindow("envD") is null,
        new { globalTaken = globalWindow is not null, targetGranted = false });
}
using (IDisposable? targetWindow = ledger.TryEnterSwapWindow("envD")) {
    Check("A5f a global window is refused while a target window is held",
        targetWindow is not null && ledger.TryEnterSwapWindow() is null,
        new { targetTaken = targetWindow is not null, globalGranted = false });
}

// A5g: a window must never be granted while a terminal record is still unwritten.
string evidenceId = v2.StartDetached(ledger, "envG", raceEffect, 150, "succeed", CancellationToken.None);
await WaitTerminal(ledger, evidenceId, TimeSpan.FromSeconds(20));
bool endPersisted;
using (IDisposable? window = ledger.TryEnterSwapWindow("envG")) {
    string[] lines = File.ReadAllLines(evidence);
    endPersisted = lines.Any(l => l.Contains(evidenceId, StringComparison.Ordinal)
        && l.Contains("\"kind\":\"end\"", StringComparison.Ordinal));
    Check("A5g the terminal record is already on disk when a window is granted",
        window is not null && endPersisted, new { windowTaken = window is not null, endPersisted });
}

// A5h: the admission barrier under contention. The invariant is that no Running record may exist for
// a scope while that scope's window is held, sampled THROUGHOUT the hold rather than once on
// acquisition — an admission that slips in later is exactly what a single sample misses.
var repaired = await StressAdmission(ledger, v2, raceEffect, "envR", TimeSpan.FromSeconds(2));
// `refused` is reported but deliberately not asserted: whether a start lands inside a held window is
// timing-dependent, and asserting it would turn an invariant test into a flaky one.
Check("A5h under contention, no operation is ever admitted for a scope whose window is held",
    repaired.Violations == 0 && repaired.Admitted > 0 && repaired.Windows > 0,
    new { repaired.Violations, repaired.Admitted, repaired.Refused, repaired.Windows });

// A5i: the mutation control. A concurrency test that has never been seen to fail proves nothing, so the
// same harness runs against a ledger that deliberately restores the original split admission.
var brokenLedger = new OperationLedger(Path.Combine(work, "split.jsonl"), splitAdmissionForTests: true);
var mutated = await StressAdmission(brokenLedger, v2, Path.Combine(work, "split-effect.log"), "envS",
    TimeSpan.FromSeconds(2));
Check("A5i mutation control: the same regression detects a deliberately split admission",
    mutated.Violations > 0,
    new { mutated.Violations, mutated.Admitted, mutated.Refused, mutated.Windows,
          note = "non-zero here is the point: it shows A5h can fail" });

// ── C1: the negative control. Without durable evidence the same question gets today's wrong answer ──
var withoutEvidence = new OperationLedger(Path.Combine(work, "no-evidence.jsonl"));
var blindAnswer = withoutEvidence.Query(lostId);
Check("C1 control: a host with no evidence answers NotFound for the very same lost operation",
    blindAnswer.State == OperationState.NotFound,
    new { withEvidence = lostRecord.State.ToString(), withoutEvidence = blindAnswer.State.ToString(),
          note = "this is the clio 8 behaviour the experiment exists to replace" });

// ── C2: retention control. A release with live work must not be collectible ─────────────────────────
var (aliveWhileRunning, collectedAfter) = await PhaseRetention(ledger, v1Dir, Path.Combine(work, "c2-effect.log"));
Check("C2 control: the release is retained while its operation runs, and only then collectible",
    aliveWhileRunning && collectedAfter,
    new { aliveWhileRunning, collectedAfter });

GC.KeepAlive(v2Ctx);
Console.WriteLine(JsonSerializer.Serialize(new {
    os = Environment.OSVersion.VersionString,
    framework = Environment.Version.ToString(),
    workDirectory = work,
    cases = observations
}, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

// ────────────────────────────────────────────────────────────────────────────────────────────────────

// Shared contention harness: one starter against one swapper, so the repaired and the deliberately
// broken ledger are measured by identical code rather than by two hand-written loops.
static async Task<(int Violations, int Admitted, int Refused, int Windows)> StressAdmission(
    OperationLedger ledger, IDetachedRuntime runtime, string effectPath, string target, TimeSpan duration) {
    int violations = 0, admitted = 0, refused = 0, windows = 0;
    using var stress = new CancellationTokenSource(duration);
    Task starter = Task.Run(async () => {
        while (!stress.IsCancellationRequested) {
            // Work must be SHORTER than the gap between starts, or the scope is never idle and the
            // swapper never wins a window — then the test measures nothing.
            try {
                runtime.StartDetached(ledger, target, effectPath, 3, "succeed", CancellationToken.None);
                Interlocked.Increment(ref admitted);
            }
            catch (SwapWindowHeldException) { Interlocked.Increment(ref refused); }
            await Task.Delay(12);
        }
    });
    Task swapper = Task.Run(async () => {
        while (!stress.IsCancellationRequested) {
            // Scoped block, NOT a using-statement: a statement-scoped using would hold the window until
            // the end of the loop body, across the delay below, so nothing would ever be admitted.
            using (IDisposable? window = ledger.TryEnterSwapWindow(target)) {
                if (window is not null) {
                    Interlocked.Increment(ref windows);
                    for (int sample = 0; sample < 6; sample++) {
                        if (ledger.Running.Select(ledger.Query).Any(r => r.Target == target)) {
                            Interlocked.Increment(ref violations);
                        }
                        await Task.Delay(1);
                    }
                }
            }
            await Task.Delay(8);
        }
    });
    await Task.WhenAll(starter, swapper);
    return (violations, admitted, refused, windows);
}

// Owns the V1 release for exactly as long as the operation does, then releases and unloads it. Kept in
// its own non-inlined frame so the caller holds no reference that would defeat the collectibility check.
[MethodImpl(MethodImplOptions.NoInlining)]
static async Task<(WeakReference V1Weak, AssemblyLoadContext V2Context, IDetachedRuntime V2)> PhaseUpdate(
    OperationLedger ledger, string v1Directory, string v2Directory, string effectPath,
    Action<string, bool, object> check) {
    var (v1Context, v1) = Load(v1Directory);
    string operationId = v1.StartDetached(ledger, "envA", effectPath, 2500, "succeed", CancellationToken.None);
    var atStart = ledger.Query(operationId);

    // Activate the newer release while the operation is still running. This is the update moment.
    var (v2Context, v2) = Load(v2Directory);
    var afterActivation = ledger.Query(operationId);

    check("A1a the original operation still answers after a newer runtime is activated",
        afterActivation.State == OperationState.Running && afterActivation.RuntimeVersion == v1.Version,
        new { started = atStart.State.ToString(), afterActivation = afterActivation.State.ToString(),
              ownedBy = afterActivation.RuntimeVersion, activated = v2.Version });

    // Quiescence has to be answerable per target, not only per process: a session driving several
    // environments would otherwise almost never report idle.
    check("A1b quiescence is scoped per target while the operation runs",
        !ledger.IsQuiescent("envA") && ledger.IsQuiescent("envB") && !ledger.IsQuiescent(),
        new { envA = ledger.IsQuiescent("envA"), envB = ledger.IsQuiescent("envB"), global = ledger.IsQuiescent() });

    var terminal = await WaitTerminal(ledger, operationId, TimeSpan.FromSeconds(20));
    string[] effectLines = File.Exists(effectPath) ? File.ReadAllLines(effectPath) : [];

    check("A2a the terminal state is truthful and the external effect happened exactly once",
        terminal.State == OperationState.Succeeded && effectLines.Length == 1
            && effectLines[0].StartsWith("v1-", StringComparison.Ordinal),
        new { state = terminal.State.ToString(), code = terminal.Code, effectLines });

    check("A2b the target is quiescent once the operation terminates",
        ledger.IsQuiescent("envA") && ledger.IsQuiescent(),
        new { envA = ledger.IsQuiescent("envA"), global = ledger.IsQuiescent() });

    var weak = new WeakReference(v1Context, trackResurrection: true);
    v1Context.Unload();
    return (weak, v2Context, v2);
}

// Control for the retention claim: unload a release that still has work in flight and show it is NOT
// collectible, then show it becomes collectible once the operation terminates. Retention here comes from
// both the ledger's owner reference and the detached work's own closure — the point is that the release
// outlives the update, not which of the two references achieves it.
[MethodImpl(MethodImplOptions.NoInlining)]
static async Task<(bool AliveWhileRunning, bool CollectedAfter)> PhaseRetention(
    OperationLedger ledger, string releaseDirectory, string effectPath) {
    var (context, runtime) = Load(releaseDirectory);
    string id = runtime.StartDetached(ledger, "envC", effectPath, 3000, "succeed", CancellationToken.None);
    var weak = new WeakReference(context, trackResurrection: true);
    runtime = null!;
    context.Unload();
    for (int i = 0; i < 12 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(50); }
    bool aliveWhileRunning = weak.IsAlive;                 // must still be alive: the operation is running
    await WaitTerminal(ledger, id, TimeSpan.FromSeconds(20));
    for (int i = 0; i < 40 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(50); }
    return (aliveWhileRunning, !weak.IsAlive);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static (AssemblyLoadContext Context, IDetachedRuntime Runtime) Load(string releaseDirectory) {
    var context = new ReleaseContext(releaseDirectory);
    Assembly assembly = context.LoadFromAssemblyPath(
        Path.Combine(releaseDirectory, "Clio10.DetachedRuntimeFixture.dll"));
    Type type = assembly.GetType("Clio10.DetachedOperationsFixture.DetachedRuntime")
        ?? throw new InvalidOperationException("The release does not expose DetachedRuntime.");
    return (context, (IDetachedRuntime)Activator.CreateInstance(type)!);
}

static async Task<OperationRecord> WaitTerminal(IOperationLedger ledger, string id, TimeSpan budget) {
    DateTime deadline = DateTime.UtcNow + budget;
    while (DateTime.UtcNow < deadline) {
        var record = ledger.Query(id);
        if (record.State != OperationState.Running) return record;
        await Task.Delay(50);
    }
    return ledger.Query(id);
}

// Starts an operation in a child process and kills that process while the work is in flight, so
// nothing in the child gets a chance to write a terminal state.
static async Task<string> StartThenKillChild(string releaseDirectory, string evidencePath, string effectPath) {
    var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true };
    // Only the dotnet muxer needs the assembly as its first argument; an apphost must not get one,
    // or the argument shifts and the child reads a directory where it expects its mode.
    if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet",
            StringComparison.OrdinalIgnoreCase)) {
        start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
    }
    start.ArgumentList.Add("child");
    start.ArgumentList.Add(string.Join('|', releaseDirectory, evidencePath, effectPath));
    using var child = Process.Start(start)!;
    string id = (await child.StandardOutput.ReadLineAsync())!.Trim();
    child.Kill(entireProcessTree: true);
    await child.WaitForExitAsync();
    return id;
}

static void Child(string packed) {
    string[] parts = packed.Split('|');
    var ledger = new OperationLedger(parts[1]);
    var (_, runtime) = Load(parts[0]);
    string id = runtime.StartDetached(ledger, "envA", parts[2], 60_000, "succeed", CancellationToken.None);
    Console.WriteLine(id);
    Console.Out.Flush();
    Thread.Sleep(Timeout.Infinite);             // wait to be killed with the operation still running
}

/// <summary>A collectible context that resolves the release's own assemblies from its directory.</summary>
file sealed class ReleaseContext(string directory) : AssemblyLoadContext(isCollectible: true) {
    protected override Assembly? Load(AssemblyName assemblyName) {
        string candidate = Path.Combine(directory, assemblyName.Name + ".dll");
        // The contract assembly stays shared with the host, or the cast across the boundary fails.
        return assemblyName.Name == "Clio10.DetachedOperations.Contract" || !File.Exists(candidate)
            ? null
            : LoadFromAssemblyPath(candidate);
    }
}
