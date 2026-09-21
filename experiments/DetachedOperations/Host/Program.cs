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
if (args.Length >= 1 && args[0] == "idle") { Console.WriteLine("ready"); Console.Out.Flush(); Thread.Sleep(Timeout.Infinite); return 0; }
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
await WaitQuiescent(ledger, "envD", TimeSpan.FromSeconds(10));

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
await WaitQuiescent(ledger, "envD", TimeSpan.FromSeconds(10));
Check("A5d the scope accepts real work again once the window is released",
    afterRelease.State == OperationState.Succeeded,
    new { state = afterRelease.State.ToString() });

// A5e: exclusion must hold in both directions, not only global-blocks-global.
await WaitQuiescent(ledger, null, TimeSpan.FromSeconds(10));
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
await WaitQuiescent(ledger, "envG", TimeSpan.FromSeconds(10));
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

// ── N: what reconciliation does NOT give you ───────────────────────────────────────────────────────
// Raised by @kirillkrylov: "artifact identity is not request attribution". These are controlled models
// of the INFERENCE, not measurements of Creatio. Each shows the naive rule — "the artefact I asked for
// is present, therefore my operation completed" — returning the wrong answer, while the ledger does not.

// N1: the artefact pre-exists. Nothing this operation did put it there.
string n1Effect = Path.Combine(work, "n1-effect.log");
await File.WriteAllTextAsync(n1Effect, "v1-executed-by-10.0.0.0" + Environment.NewLine);
string n1Lost = await StartThenKillChild(v1Dir, Path.Combine(work, "n1.jsonl"), n1Effect);
var n1Ledger = new OperationLedger(Path.Combine(work, "n1.jsonl"));
bool n1NaiveSaysDone = File.ReadAllLines(n1Effect).Any(l => l.StartsWith("v1-", StringComparison.Ordinal));
var n1Record = n1Ledger.Query(n1Lost);
Check("N1 a pre-existing artefact makes presence a FALSE positive for attribution",
    n1NaiveSaysDone && n1Record.State == OperationState.Unknown,
    new { naivePresenceCheck = n1NaiveSaysDone ? "done" : "not done",
          ledger = n1Record.State.ToString(),
          note = "the artefact was written before the operation started and the operation was killed" });

// N2: the operation half-finished. Its first artefact exists; it did not complete.
string n2Effect = Path.Combine(work, "n2-effect.log");
string n2Id = v2.StartDetached(ledger, "envN2", n2Effect, 100, "partial", CancellationToken.None);
var n2Terminal = await WaitTerminal(ledger, n2Id, TimeSpan.FromSeconds(20));
bool n2NaiveSaysDone = File.Exists(n2Effect)
    && File.ReadAllLines(n2Effect).Any(l => l.Contains("-part1", StringComparison.Ordinal));
Check("N2 a partially completed operation makes presence a FALSE positive",
    n2NaiveSaysDone && n2Terminal.State == OperationState.Failed,
    new { naivePresenceCheck = n2NaiveSaysDone ? "done" : "not done",
          ledger = n2Terminal.State.ToString(), code = n2Terminal.Code });

// N3: an intervening operation. A dies; B runs and completes; a "last result" read answers about B.
string n3Effect = Path.Combine(work, "n3-effect.log");
string n3Lost = await StartThenKillChild(v1Dir, Path.Combine(work, "n3.jsonl"), n3Effect);
string n3Later = v2.StartDetached(ledger, "envN3", n3Effect, 100, "succeed", CancellationToken.None);
await WaitTerminal(ledger, n3Later, TimeSpan.FromSeconds(20));
string[] n3Lines = File.Exists(n3Effect) ? File.ReadAllLines(n3Effect) : [];
string? n3LastResult = n3Lines.LastOrDefault();
var n3Record = new OperationLedger(Path.Combine(work, "n3.jsonl")).Query(n3Lost);
Check("N3 an intervening operation makes a last-result read answer about the wrong one",
    n3LastResult is not null && n3LastResult.StartsWith("v2-", StringComparison.Ordinal)
        && n3Record.State == OperationState.Unknown,
    new { lastResultSays = n3LastResult, askedAbout = "the killed v1 operation",
          ledger = n3Record.State.ToString(),
          note = "attributing the last result to the interrupted operation would report v2's outcome as v1's" });

// ── P1/P2/P3: a failed evidence write degrades storage without rewriting the outcome ───────────────
// @kirillkrylov: execution outcome and evidence health are different axes. Successful work must not
// become a failed or retryable business operation because storage failed.
var (faultLedger, faultId, faultWeak) = await PhaseDegradedPersistence(v1Dir, work);
var faultTerminal = faultLedger.Query(faultId);

Check("P1 a failed evidence write does NOT rewrite the execution outcome",
    faultTerminal.State == OperationState.Succeeded,
    new { state = faultTerminal.State.ToString(),
          note = "the work succeeded; only its record failed to persist" });

Check("P2 the scope is visibly degraded and automatic retirement is refused",
    faultLedger.DegradedScopes.Contains("envP")
        && faultLedger.IsQuiescent("envP")
        && faultLedger.TryEnterSwapWindow("envP") is null
        && faultLedger.TryEnterSwapWindow() is null,
    new { degradedScopes = faultLedger.DegradedScopes, quiescent = faultLedger.IsQuiescent("envP"),
          targetWindow = "refused", globalWindow = "refused",
          note = "refusal is conservative POLICY, not a consequence of the evidence living in the runtime - see P3" });

// P3: the correction @kirillkrylov asked for. The evidence is host-owned, so unloading the runtime
// does not destroy it. Only replacing the process that owns the ledger does.
for (int i = 0; i < 40 && faultWeak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(50); }
var afterRetirement = faultLedger.Query(faultId);
Check("P3 retiring the runtime does not destroy degraded evidence; only replacing its owner would",
    !faultWeak.IsAlive
        && afterRetirement.State == OperationState.Succeeded
        && faultLedger.DegradedScopes.Contains("envP"),
    new { runtimeCollected = !faultWeak.IsAlive, stateAfterRetirement = afterRetirement.State.ToString(),
          stillDegraded = faultLedger.DegradedScopes.Contains("envP"),
          note = "OperationRecord is a host-contract type held in a host collection, so it survives the release that produced it" });

// P4: a failed ADMISSION write must admit nothing — no work, no unfinishable live owner.
var admitFail = new OperationLedger(Path.Combine(work, "admit.jsonl")) { FailBeginPersistenceForTests = true };
string p4Effect = Path.Combine(work, "p4-effect.log");
bool p4Threw = false;
try { v2.StartDetached(admitFail, "envQ", p4Effect, 100, "succeed", CancellationToken.None); }
catch (IOException) { p4Threw = true; }
await Task.Delay(400);
Check("P4 a failed admission write starts no work and leaves no unfinishable owner",
    p4Threw && admitFail.Running.Count == 0 && admitFail.IsQuiescent("envQ") && !File.Exists(p4Effect),
    new { threw = p4Threw, running = admitFail.Running.Count, quiescent = admitFail.IsQuiescent("envQ"),
          effectWritten = File.Exists(p4Effect),
          note = "evidence is written before registration, so nothing is registered when it fails" });

// P5: owned cleanup held after the outcome is published. Ownership ends at Dispose, not at Complete.
string p5Id = v2.StartDetached(ledger, "envC2", Path.Combine(work, "p5-effect.log"), 100, "cleanup",
    CancellationToken.None);
var p5Terminal = await WaitTerminal(ledger, p5Id, TimeSpan.FromSeconds(20));
bool duringCleanup = !ledger.IsQuiescent("envC2") && ledger.TryEnterSwapWindow("envC2") is null;
bool idleAfter = await WaitQuiescent(ledger, "envC2", TimeSpan.FromSeconds(10));
bool windowAfter;
using (IDisposable? w = ledger.TryEnterSwapWindow("envC2")) { windowAfter = w is not null; }
Check("P5 retirement is refused while owned cleanup is held, and allowed once ownership is released",
    p5Terminal.State == OperationState.Succeeded && duringCleanup && idleAfter && windowAfter,
    new { outcomePublished = p5Terminal.State.ToString(), refusedDuringCleanup = duringCleanup,
          idleAfterRelease = idleAfter, windowGrantedAfter = windowAfter,
          note = "the outcome was Succeeded throughout; only ownership changed" });

// ── L1/L2: lease hazards found by review of the published source ───────────────────────────────────
// Both had the same root: the "already reported" flag moved before the work it guarded.

// L1: a concurrent Dispose must not release ownership while an outcome is still being recorded.
// The delay makes the window deterministic rather than something to race for; the property asserted is
// that Dispose cannot RETURN before the outcome exists.
var leaseLedger = new OperationLedger(Path.Combine(work, "lease.jsonl")) { CompleteDelayMsForTests = 300 };
object leaseOwner = new();
IOperationLease raceLease = leaseLedger.Begin("envL", "10.0.0.0", leaseOwner);
Task completing = Task.Run(() => raceLease.Complete(OperationState.Succeeded, "l1"));
await Task.Delay(60);                                  // let Complete get inside the delayed window
raceLease.Dispose();
var atDisposeReturn = leaseLedger.Query(raceLease.Id);
await completing;
Check("L1 a concurrent disposal cannot release ownership before the outcome is recorded",
    atDisposeReturn.State == OperationState.Succeeded && leaseLedger.IsQuiescent("envL"),
    new { stateWhenDisposeReturned = atDisposeReturn.State.ToString(),
          quiescentAfter = leaseLedger.IsQuiescent("envL"),
          note = "Dispose blocks on the lease gate until the in-flight completion has recorded its outcome" });

// L2: a rejected completion must not consume the one report the lease is allowed.
IOperationLease rejectLease = leaseLedger.Begin("envL2", "10.0.0.0", leaseOwner);
bool rejected = false;
try { rejectLease.Complete(OperationState.Running, "invalid"); }
catch (ArgumentOutOfRangeException) { rejected = true; }
rejectLease.Complete(OperationState.Succeeded, "l2");
var afterReject = leaseLedger.Query(rejectLease.Id);
rejectLease.Dispose();
Check("L2 a rejected completion does not burn the report; the operation can still finish",
    rejected && afterReject.State == OperationState.Succeeded,
    new { rejectedInvalidState = rejected, finalState = afterReject.State.ToString(),
          note = "validation happens before the single report is consumed" });

// M1: the mutation arm L1 was missing. The same sequence against a lease that moves its flag first
// must be DETECTED — a concurrency test never seen to fail proves nothing about what it guards.
var flagFirst = new OperationLedger(Path.Combine(work, "flagfirst.jsonl")) {
    CompleteDelayMsForTests = 300, UseFlagFirstLeaseForTests = true };
IOperationLease mutatedLease = flagFirst.Begin("envM", "10.0.0.0", leaseOwner);
Task mutatedCompleting = Task.Run(() => mutatedLease.Complete(OperationState.Succeeded, "m1"));
await Task.Delay(60);
mutatedLease.Dispose();
var mutatedAtReturn = flagFirst.Query(mutatedLease.Id);
await mutatedCompleting;
Check("M1 mutation control: the same check detects a lease that reports its flag before the outcome",
    mutatedAtReturn.State == OperationState.Running,
    new { stateWhenDisposeReturned = mutatedAtReturn.State.ToString(),
          note = "Running here is the defect: ownership was released while the outcome was still unrecorded" });

// M2: failure part-way THROUGH the write, not before it. A torn line must not read as a terminal.
var tornLedger = new OperationLedger(Path.Combine(work, "torn.jsonl")) { FailMidWriteForTests = true };
string tornId = v2.StartDetached(tornLedger, "envT", Path.Combine(work, "torn-effect.log"), 100,
    "succeed", CancellationToken.None);
var tornTerminal = await WaitTerminal(tornLedger, tornId, TimeSpan.FromSeconds(20));
var tornRecovered = new OperationLedger(Path.Combine(work, "torn.jsonl")).Query(tornId);
Check("M2 a torn terminal write keeps the outcome in memory and is never recovered as terminal",
    tornTerminal.State == OperationState.Succeeded
        && tornLedger.DegradedScopes.Contains("envT")
        && tornRecovered.State == OperationState.Unknown,
    new { inMemory = tornTerminal.State.ToString(), degraded = tornLedger.DegradedScopes.Contains("envT"),
          recoveredFromDisk = tornRecovered.State.ToString(),
          note = "the half-written line is skipped, so recovery sees begin-without-end" });

// M3/E3/E4/E5: what "clearing" actually was, and what durable repair actually is.
// @kirillkrylov: removing the storage fault or clearing a flag is not itself repair. He is right — the
// outcome still exists only in memory, so replacing the evidence owner loses it either way.
string clearPath = Path.Combine(work, "clear.jsonl");
var clearLedger = new OperationLedger(clearPath) { FailEndPersistenceForTests = true };
string clearId = v2.StartDetached(clearLedger, "envK", Path.Combine(work, "clear-effect.log"), 100,
    "succeed", CancellationToken.None);
await WaitTerminal(clearLedger, clearId, TimeSpan.FromSeconds(20));

Check("M3 a degraded outcome is tracked as un-persisted, not merely as a degraded scope",
    clearLedger.DegradedScopes.Contains("envK") && clearLedger.UnpersistedOperations.Contains(clearId)
        && clearLedger.TryEnterSwapWindow("envK") is null,
    new { degraded = true, unpersisted = clearLedger.UnpersistedOperations.Count, windowRefused = true });

// E3: the owner is replaced while the outcome is still memory-only. Nothing survives.
var beforeRepairOwner = new OperationLedger(clearPath).Query(clearId);
Check("E3 an un-persisted outcome does NOT survive replacement of the evidence owner",
    beforeRepairOwner.State == OperationState.Unknown,
    new { inMemory = "Succeeded", afterOwnerReplacement = beforeRepairOwner.State.ToString(),
          note = "clearing a flag would not have changed this — the record was never on disk" });

// E4: repair is the only thing that makes it durable, and it re-persists rather than re-executes.
clearLedger.FailEndPersistenceForTests = false;                   // the storage fault is removed
var faultRemovedOnly = new OperationLedger(clearPath).Query(clearId);
var repair = clearLedger.RepairDegraded("envK");
var afterRepairOwner = new OperationLedger(clearPath).Query(clearId);
bool windowAfterRepair;
using (IDisposable? w = clearLedger.TryEnterSwapWindow("envK")) { windowAfterRepair = w is not null; }
Check("E4 repair is what makes an outcome durable; removing the fault alone changes nothing",
    faultRemovedOnly.State == OperationState.Unknown
        && repair is { Repaired: 1, Remaining: 0, ScopeCleared: true }
        && afterRepairOwner.State == OperationState.Succeeded
        && windowAfterRepair,
    new { afterFaultRemovedOnly = faultRemovedOnly.State.ToString(),
          repaired = repair.Repaired, remaining = repair.Remaining, scopeCleared = repair.ScopeCleared,
          afterRepair = afterRepairOwner.State.ToString(), windowGranted = windowAfterRepair,
          note = "the record written is the original outcome, once; nothing is re-executed" });

// E5: the same, but the fault was a TORN write — repair must survive restart readback.
string tornPath = Path.Combine(work, "torn-repair.jsonl");
var tornRepair = new OperationLedger(tornPath) { FailMidWriteForTests = true };
string tornRepairId = v2.StartDetached(tornRepair, "envTR", Path.Combine(work, "tr-effect.log"), 100,
    "succeed", CancellationToken.None);
await WaitTerminal(tornRepair, tornRepairId, TimeSpan.FromSeconds(20));
var tornBefore = new OperationLedger(tornPath).Query(tornRepairId);
tornRepair.FailMidWriteForTests = false;
var tornRepairResult = tornRepair.RepairDegraded("envTR");
var tornAfter = new OperationLedger(tornPath).Query(tornRepairId);
Check("E5 repair after a torn write survives restart readback",
    tornBefore.State == OperationState.Unknown && tornRepairResult.Repaired == 1
        && tornAfter.State == OperationState.Succeeded,
    new { beforeRepair = tornBefore.State.ToString(), repaired = tornRepairResult.Repaired,
          afterRepairReadback = tornAfter.State.ToString(),
          note = "the half-line stays on disk and is skipped; the repaired line is the one that reads back" });

// E6: when repair is impossible, explicit loss is the honest alternative — and it is named as loss.
string lossPath = Path.Combine(work, "loss.jsonl");
var lossLedger = new OperationLedger(lossPath);
string lossId = v2.StartDetached(lossLedger, "envX", Path.Combine(work, "loss-effect.log"), 400,
    "succeed", CancellationToken.None);
lossLedger.FailAppendForTests = true;             // admission persisted; the terminal write will not
await WaitTerminal(lossLedger, lossId, TimeSpan.FromSeconds(20));
var stillFaulty = lossLedger.RepairDegraded("envX");              // fault still present
int abandoned = lossLedger.AcceptLoss("envX");
var afterLoss2 = new OperationLedger(lossPath).Query(lossId);
bool windowAfterLoss;
using (IDisposable? w = lossLedger.TryEnterSwapWindow("envX")) { windowAfterLoss = w is not null; }
// The fault here fails the APPEND itself, so a repair attempt genuinely cannot succeed.
Check("E6 with the fault still present, repair fails and explicit loss is the only way forward",
    stillFaulty is { Repaired: 0, ScopeCleared: false } && abandoned == 1
        && afterLoss2.State == OperationState.Unknown && windowAfterLoss,
    new { repairedWhileFaulty = stillFaulty.Repaired, scopeClearedByFailedRepair = stillFaulty.ScopeCleared,
          abandoned, afterLossReadback = afterLoss2.State.ToString(), windowGranted = windowAfterLoss,
          note = "the outcome is gone and later readers are told Unknown rather than a guess" });

// M4: an update that cannot get its window defers. It never kills the work and never claims success.
string busyId = v2.StartDetached(ledger, "envU", Path.Combine(work, "defer-effect.log"), 2000,
    "succeed", CancellationToken.None);
DateTime deferDeadline = DateTime.UtcNow.AddMilliseconds(400);
bool applied = false;
while (DateTime.UtcNow < deferDeadline) {
    using IDisposable? w = ledger.TryEnterSwapWindow("envU");
    if (w is not null) { applied = true; break; }
    await Task.Delay(25);
}
var busyDuring = ledger.Query(busyId);
var busyTerminal = await WaitTerminal(ledger, busyId, TimeSpan.FromSeconds(20));
string[] deferLines = File.ReadAllLines(Path.Combine(work, "defer-effect.log"));
Check("M4 an update that cannot take its window defers, leaving the work untouched",
    !applied && busyDuring.State == OperationState.Running
        && busyTerminal.State == OperationState.Succeeded && deferLines.Length == 1,
    new { updateApplied = applied, stateDuringWait = busyDuring.State.ToString(),
          finalState = busyTerminal.State.ToString(), effectLines = deferLines.Length,
          note = "deferred rather than applied; no implicit kill, and the work completed normally" });

// D1/D2: attacking my own recommendation. "Defer, never kill" is only a policy if the deferral ends.
// A continuously busy scope is the case that would overturn it, so it is measured rather than named.
using (var load = new CancellationTokenSource(TimeSpan.FromSeconds(4))) {
    string loadEffect = Path.Combine(work, "load-effect.log");
    Task pressure = Task.Run(async () => {
        while (!load.IsCancellationRequested) {
            try { v2.StartDetached(ledger, "envS", loadEffect, 120, "succeed", CancellationToken.None); }
            catch (SwapWindowHeldException) { }
            await Task.Delay(40);                 // overlapping work: the scope is never idle
        }
    });

    // Establish the load first. Polling before the scope is actually busy measures a test artifact:
    // the first poll wins on an empty scope and the case proves nothing.
    DateTime loadDeadline = DateTime.UtcNow.AddSeconds(2);
    while (DateTime.UtcNow < loadDeadline && ledger.IsQuiescent("envS")) {
        await Task.Delay(20);
    }
    bool loadEstablished = !ledger.IsQuiescent("envS");

    // D1: poll for an idle moment, the way TryEnterSwapWindow alone requires.
    DateTime pollUntil = DateTime.UtcNow.AddSeconds(2);
    bool everIdle = false;
    while (DateTime.UtcNow < pollUntil) {
        using IDisposable? w = ledger.TryEnterSwapWindow("envS");
        if (w is not null) { everIdle = true; break; }
        await Task.Delay(25);
    }
    Check("D1 disproof: polling for an idle moment starves against a continuously busy scope",
        loadEstablished && !everIdle,
        new { loadEstablished, windowEverGranted = everIdle, observedFor = "2s under continuous load",
              note = "this is the case that would overturn 'defer, never kill' — deferral that never ends" });

    // D2: reserve first, then drain. Nothing new is admitted, so the scope necessarily empties.
    bool busyBeforeReserving = !ledger.IsQuiescent("envS");
    var reserveStart = DateTime.UtcNow;
    using (IDisposable? reservation = ledger.TryReserveAdmission("envS")) {
        bool reserved = reservation is not null;
        bool drained = await WaitQuiescent(ledger, "envS", TimeSpan.FromSeconds(5));
        Check("D2 reserving first and draining second ends the wait under the same load",
            busyBeforeReserving && reserved && drained,
            new { busyWhenReserved = busyBeforeReserving, reservedImmediately = reserved, drained,
                  drainMs = (int)(DateTime.UtcNow - reserveStart).TotalMilliseconds,
                  note = "new admissions are refused under the reservation, so in-flight work is finite" });
    }
    await pressure;
}

// ── E1/E2: a finite set of in-flight operations need not terminate ─────────────────────────────────
// D2 showed reserve-then-drain ends the wait. It assumed the in-flight work finishes. Hung work is the
// case where it does not, and the reservation must then expire rather than hold the scope shut forever.
using var hungSource = new CancellationTokenSource();
string hungId = v2.StartDetached(ledger, "envH", Path.Combine(work, "hung-effect.log"), 0, "hang",
    hungSource.Token);
await Task.Delay(150);

bool reservedOverHung, drainedWithinBudget, hungStillRunning, reopened;
var reserveClock = System.Diagnostics.Stopwatch.StartNew();
using (IDisposable? reservation = ledger.TryReserveAdmission("envH")) {
    reservedOverHung = reservation is not null;
    // A bounded drain. The reservation is released by leaving this block when it expires — deferring
    // the update again rather than escalating to a kill.
    drainedWithinBudget = await WaitQuiescent(ledger, "envH", TimeSpan.FromMilliseconds(600));
    bool refusedDuringReservation = false;
    try { v2.StartDetached(ledger, "envH", Path.Combine(work, "hung-effect.log"), 10, "succeed",
              CancellationToken.None); }
    catch (SwapWindowHeldException) { refusedDuringReservation = true; }
    hungStillRunning = ledger.Query(hungId).State == OperationState.Running && refusedDuringReservation;
}
reserveClock.Stop();

string afterReopenId = v2.StartDetached(ledger, "envH", Path.Combine(work, "hung-effect.log"), 50,
    "succeed", CancellationToken.None);
var afterReopen = await WaitTerminal(ledger, afterReopenId, TimeSpan.FromSeconds(20));
reopened = afterReopen.State == OperationState.Succeeded;

Check("E1 a bounded drain over hung work expires instead of holding the scope shut",
    reservedOverHung && !drainedWithinBudget && hungStillRunning
        && reserveClock.ElapsedMilliseconds < 3000,
    new { reserved = reservedOverHung, drainedWithinBudget, hungStillRunning,
          reservationHeldMs = reserveClock.ElapsedMilliseconds,
          note = "the hung operation was neither killed nor completed; the update defers again" });

Check("E2 admissions reopen once the reservation expires, with the hung work still untouched",
    reopened && ledger.Query(hungId).State == OperationState.Running,
    new { newWorkAdmitted = afterReopen.State.ToString(),
          hungOperation = ledger.Query(hungId).State.ToString(),
          note = "no implicit kill and no replay; the scope is usable again" });

await hungSource.CancelAsync();
await WaitTerminal(ledger, hungId, TimeSpan.FromSeconds(10));

// ── O1: terminal status is NOT sufficient for reclamation ──────────────────────────────────────────
// @kirillkrylov's returned-value-ownership finding, measured against my own invariant I3. A caller that
// holds a runtime-defined result also holds the release that defined its type, however finished the
// operation is and however empty the ledger's retention.
var (o1Weak, escaped) = await PhaseEscapedResult(ledger, v1Dir, Path.Combine(work, "o1-effect.log"));
for (int i = 0; i < 20 && o1Weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(50); }
bool aliveWhileHeld = o1Weak.IsAlive;
string escapedType = escaped.GetType().FullName ?? "?";
escaped = null!;                                   // drop the only remaining reference
for (int i = 0; i < 40 && o1Weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(50); }
Check("O1 a runtime-defined result held by the caller keeps the release alive after the operation ended",
    aliveWhileHeld && !o1Weak.IsAlive,
    new { aliveWhileResultHeld = aliveWhileHeld, collectedAfterResultDropped = !o1Weak.IsAlive,
          escapedType,
          note = "the ledger reported a terminal state and released its own retention; the escaped value did not" });

// ── G: the portable boundary for errors, progress and callbacks ────────────────────────────────────
static async Task<bool> Collect(WeakReference weak, int rounds) {
    for (int i = 0; i < rounds && weak.IsAlive; i++) {
        GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(50);
    }
    return !weak.IsAlive;
}

var (g1Weak, g1Escaped, g1Code, g1Held, g1Progress) = await PhasePortableBoundary(v1Dir);
bool g1AliveWhileHeld = !await Collect(g1Weak, 12);
string g1Type = g1Escaped!.GetType().FullName!;
g1Escaped = null;
bool g1CollectedAfter = await Collect(g1Weak, 40);

Check("G1 a runtime-defined exception retains its release exactly like a returned value does",
    g1AliveWhileHeld && g1CollectedAfter && g1Type.Contains("ReleaseFault", StringComparison.Ordinal),
    new { escapedType = g1Type, aliveWhileCaughtExceptionHeld = g1AliveWhileHeld,
          collectedAfterDropped = g1CollectedAfter,
          note = "an escaping exception is a returned value with extra steps" });

var (g2Weak, g2Code) = await PhasePortableOnly(v1Dir);
bool g2Collected = await Collect(g2Weak, 40);
Check("G2 the portable error form lets the release go while the information survives",
    g2Collected && g2Code == "release-fault",
    new { releaseCollected = g2Collected, codeStillUsable = g2Code,
          note = "same failure, expressed as strings; nothing retains the release" });

Check("G3 a host progress delegate is not retained by the release after the call",
    !g1Held && g1Progress.Count == 3 && g1Progress[0].StartsWith("10.0.0.0", StringComparison.Ordinal),
    new { heldAfterCall = g1Held, reports = g1Progress.Count, first = g1Progress[0],
          note = "the mirror case: a retained host delegate would tie the host graph to the release" });

var (g4Weak, g4Callback) = await PhaseEscapedCallback(v1Dir);
// The callback must actually be USED across the check. A variable that is never read can be treated as
// dead immediately, and the case would then measure JIT liveness instead of the retention property.
string g4Invoked = ((Func<string>)g4Callback)();
bool g4AliveWhileHeld = !await Collect(g4Weak, 12);
GC.KeepAlive(g4Callback);
g4Callback = null!;
bool g4CollectedAfter = await Collect(g4Weak, 40);
Check("G4 negative control: a runtime-defined callback keeps the release alive until dropped",
    g4AliveWhileHeld && g4CollectedAfter && g4Invoked.StartsWith("callback", StringComparison.Ordinal),
    new { invoked = g4Invoked, aliveWhileCallbackHeld = g4AliveWhileHeld,
          collectedAfterDropped = g4CollectedAfter,
          note = "delegates behave exactly as DTOs and exceptions do — the rule is one rule" });

// ── F: embedding, concurrent version pinning, and incompatible-release rejection ───────────────────
string partnerDir = Path.GetFullPath(Path.Combine(v1Dir, "..", "partner"));
string v3Dir = Path.GetFullPath(Path.Combine(v1Dir, "..", "10.2.0.0"));
string fEffect = Path.Combine(work, "f-effect.log");

// F1: two operations pinned to different releases at the same time.
var (fV1Ctx, fV1) = LoadCompatible(v1Dir, 1);
string onV1 = fV1.StartDetached(ledger, "envF1", fEffect, 700, "succeed", CancellationToken.None);
string onV2 = v2.StartDetached(ledger, "envF2", fEffect, 700, "succeed", CancellationToken.None);
var pinnedV1 = ledger.Query(onV1);
var pinnedV2 = ledger.Query(onV2);
// Overlap has to be OBSERVED, not inferred from the order of the two calls above. Without this the
// case passes identically on a strictly sequential run, which is the D1 mistake in a new place.
IReadOnlyCollection<string> runningTogether = ledger.Running;
bool bothInFlight = runningTogether.Contains(onV1) && runningTogether.Contains(onV2);
var doneV1 = await WaitTerminal(ledger, onV1, TimeSpan.FromSeconds(20));
var doneV2 = await WaitTerminal(ledger, onV2, TimeSpan.FromSeconds(20));
string[] fLines = File.ReadAllLines(fEffect);
Check("F1 two operations run concurrently, each pinned to the release that admitted it",
    bothInFlight
        && pinnedV1.RuntimeVersion == "10.0.0.0" && pinnedV2.RuntimeVersion == "10.1.0.0"
        && doneV1.State == OperationState.Succeeded && doneV2.State == OperationState.Succeeded
        && fLines.Any(l => l.StartsWith("v1-", StringComparison.Ordinal))
        && fLines.Any(l => l.StartsWith("v2-", StringComparison.Ordinal)),
    new { observedRunningTogether = bothInFlight, firstOwnedBy = pinnedV1.RuntimeVersion,
          secondOwnedBy = pinnedV2.RuntimeVersion, effects = fLines.Length,
          note = "both ids were seen in Running before either finished, so the overlap is measured" });

// F2: an incompatible release is refused, and work already running is untouched.
string duringRejection = fV1.StartDetached(ledger, "envF3", fEffect, 600, "succeed", CancellationToken.None);
bool releaseRejected = false;
int declaredGeneration = 0;
try { LoadCompatible(v3Dir, 1); }
catch (IncompatibleReleaseException error) { releaseRejected = true; declaredGeneration = error.Declared; }
var survived = await WaitTerminal(ledger, duringRejection, TimeSpan.FromSeconds(20));
string afterRejectionId = fV1.StartDetached(ledger, "envF3", fEffect, 80, "succeed", CancellationToken.None);
var afterRejection = await WaitTerminal(ledger, afterRejectionId, TimeSpan.FromSeconds(20));
Check("F2 an incompatible release is rejected before activation without disturbing V1",
    releaseRejected && declaredGeneration == 2
        && survived.State == OperationState.Succeeded
        && afterRejection.State == OperationState.Succeeded,
    new { rejected = releaseRejected, declaredGeneration, inFlightAcrossRejection = survived.State.ToString(),
          startedAfterRejection = afterRejection.State.ToString(),
          note = "the release is inspected then discarded; the running release is never consulted" });

// F3: a partner workflow composes vendor capability it does not reference.
IPartnerWorkflow partner = LoadPartner(partnerDir);
var composed = partner.Compose(fV1);
string[] partnerRefs = typeof(IPartnerWorkflow).Assembly.GetReferencedAssemblies()
    .Select(a => a.Name ?? string.Empty).ToArray();
Check("F3 a partner workflow composes the pinned release and returns portable data only",
    composed.Partner == "partner.reporting" && composed.RuntimeVersion == "10.0.0.0"
        && composed.Outcome.Contains("2 steps", StringComparison.Ordinal),
    new { partner = composed.Partner, ranAgainst = composed.RuntimeVersion, outcome = composed.Outcome,
          note = "the partner assembly references the contract and no vendor release" });

// F4: the assembly that crosses the boundary must not drag CLI or MCP in with it.
string[] forbidden = ["Cli", "Mcp", "Spectre", "CommandLine", "ModelContextProtocol"];
string[] offending = partnerRefs
    .Where(name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
    .ToArray();
Check("F4 the shared contract assembly references no CLI or MCP dependency",
    offending.Length == 0,
    new { referenced = partnerRefs, offending,
          note = "checked on the assembly that actually crosses the boundary, not on the host" });


// ── H: cross-process ownership — an owner that is gone cannot report, so say so ───────────────────
// Ownership normally ends at Dispose. When the owner is a PROCESS, a kill disposes nothing, and the
// ledger keeps answering Running for work that has no process. Measured on @vladimir-nikonov's MCP
// host in #1643 before it was measured here: eight polls, eight Running, forever.
string hEffect = Path.Combine(work, "h-effect.log");
string hEvidence = Path.Combine(work, "h-operations.jsonl");
var hLedger = new OperationLedger(hEvidence);

// H1: the owner process is killed while its operation is in flight.
Process aliveOwner = StartIdleOwner();
Process doomedOwner = StartIdleOwner();
IOperationLease keptLease = hLedger.Begin("envH", "10.0.0.0", new ProcessOwner(aliveOwner));
IOperationLease orphanLease = hLedger.Begin("envH", "10.0.0.0", new ProcessOwner(doomedOwner));
string beforeKill = hLedger.Query(orphanLease.Id).State.ToString();
doomedOwner.Kill(entireProcessTree: true);
await doomedOwner.WaitForExitAsync();
var orphaned = hLedger.Query(orphanLease.Id);
Check("H1 an operation whose owner process is gone resolves to Unknown, not Running",
    beforeKill == "Running" && orphaned.State == OperationState.Unknown && orphaned.Code == "owner-lost",
    new { beforeKill, afterKill = orphaned.State.ToString(), code = orphaned.Code,
          note = "a killed process disposes nothing, so disposal cannot be what ends ownership" });

// H2: the resolution is durable, so a later reader is told the same thing.
string[] hLines = File.ReadAllLines(hEvidence);
bool durable = hLines.Any(l => l.Contains(orphanLease.Id, StringComparison.Ordinal)
    && l.Contains("\"state\":\"Unknown\"", StringComparison.Ordinal));
Check("H2 the Unknown resolution is written to evidence, not only held in memory",
    durable,
    new { evidenceLines = hLines.Length, unknownPersisted = durable,
          note = "a resolution only in memory would be lost by the very replacement that caused it" });

// H3: the orphan stops retaining, so a drain can actually finish. Without this a swap can never
// complete after an owner dies — the scope is blocked by an operation that can never report.
bool quiescentWithLiveOwner = hLedger.IsQuiescent("envH");
keptLease.Complete(OperationState.Succeeded, "done");
keptLease.Dispose();
bool quiescentAfter = hLedger.IsQuiescent("envH");
Check("H3 an orphaned operation stops blocking quiescence, so a drain can finish",
    !quiescentWithLiveOwner && quiescentAfter,
    new { withLiveOwnerStillRunning = quiescentWithLiveOwner, afterLiveOwnerFinished = quiescentAfter,
          note = "the only thing still retaining was the live owner; the orphan released itself" });

// H4 negative control: a live owner is NOT resolved. Without this, H1 passes for a sweep that simply
// marks everything Unknown.
Process survivingOwner = StartIdleOwner();
IOperationLease survivingLease = hLedger.Begin("envH4", "10.0.0.0", new ProcessOwner(survivingOwner));
var stillRunning = hLedger.Query(survivingLease.Id);
Check("H4 negative control: an operation whose owner process is alive is left Running",
    stillRunning.State == OperationState.Running && !hLedger.IsQuiescent("envH4"),
    new { state = stillRunning.State.ToString(), stillRetains = !hLedger.IsQuiescent("envH4"),
          note = "the sweep resolves absence, it does not resolve everything" });

// H5: a genuine outcome arriving after the resolution supersedes it — the owner's last output can
// still be in a pipe buffer when the process is declared gone. Disposal alone must NOT supersede it,
// because disposal is not knowledge.
orphanLease.Complete(OperationState.Succeeded, "late-truth");
var superseded = hLedger.Query(orphanLease.Id);
IOperationLease disposeOnlyLease = hLedger.Begin("envH5", "10.0.0.0", new ProcessOwner(survivingOwner));
Process doomedTwo = StartIdleOwner();
IOperationLease disposeOnly = hLedger.Begin("envH5b", "10.0.0.0", new ProcessOwner(doomedTwo));
doomedTwo.Kill(entireProcessTree: true);
await doomedTwo.WaitForExitAsync();
hLedger.Query(disposeOnly.Id);                       // resolve it to Unknown
disposeOnly.Dispose();                               // disposal must not turn "I do not know" into Failed
var afterDispose = hLedger.Query(disposeOnly.Id);
Check("H5 a genuine late outcome supersedes Unknown, but disposal alone does not",
    superseded.State == OperationState.Succeeded && superseded.Code == "late-truth"
        && afterDispose.State == OperationState.Unknown,
    new { lateOutcome = superseded.State.ToString(), lateCode = superseded.Code,
          afterDisposalAlone = afterDispose.State.ToString(),
          note = "Unknown is an admission, so knowledge replaces it and a fabricated Failed does not" });

foreach (Process owner in new[] { aliveOwner, survivingOwner }) {
    try { owner.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already gone */ }
}
survivingLease.Dispose();
disposeOnlyLease.Dispose();


// ── J: the settings seam — a configuration snapshot has a portable identity and nothing more ──────
// Clause 6. The lifetime side needs exactly one thing from @vladimir-nikonov's lane: that a snapshot
// can be NAMED by a string, so a record naming it outlives the release and the process. Preparation,
// migration, rollback and retention are his and are deliberately absent here.
string jEvidence = Path.Combine(work, "j-operations.jsonl");
var jLedger = new OperationLedger(jEvidence) { CurrentSnapshotForTests = "cfg-2" };
Process jOwnerOne = StartIdleOwner();
Process jOwnerTwo = StartIdleOwner();

// J1: admitted under cfg-1, then cfg-2 is activated underneath. The operation keeps cfg-1.
IOperationLease underOne = jLedger.Begin("envJ", "10.0.0.0", new ProcessOwner(jOwnerOne), "cfg-1");
IOperationLease underTwo = jLedger.Begin("envJ", "10.0.0.0", new ProcessOwner(jOwnerTwo), "cfg-2");
var keptOne = jLedger.Query(underOne.Id);
var tookTwo = jLedger.Query(underTwo.Id);
Check("J1 an operation keeps the configuration snapshot it was admitted under",
    keptOne.ConfigurationSnapshot == "cfg-1" && tookTwo.ConfigurationSnapshot == "cfg-2",
    new { admittedBeforeActivation = keptOne.ConfigurationSnapshot,
          admittedAfterActivation = tookTwo.ConfigurationSnapshot,
          note = "captured at admission and never re-read; same rule as the runtime release" });

// J2: the identity survives into evidence and back out of it, so a later reader knows which
// configuration an operation it cannot establish the outcome of was running under.
underOne.Complete(OperationState.Succeeded, "done");
underOne.Dispose();
var reread = new OperationLedger(jEvidence);           // a fresh ledger over the same evidence
var recoveredOne = reread.Query(underOne.Id);
var recoveredTwo = reread.Query(underTwo.Id);
Check("J2 the snapshot identity survives into durable evidence and is recovered with the record",
    recoveredOne.ConfigurationSnapshot == "cfg-1" && recoveredOne.State == OperationState.Succeeded
        && recoveredTwo.ConfigurationSnapshot == "cfg-2" && recoveredTwo.State == OperationState.Unknown,
    new { finished = new { recoveredOne.ConfigurationSnapshot, state = recoveredOne.State.ToString() },
          unfinished = new { recoveredTwo.ConfigurationSnapshot, state = recoveredTwo.State.ToString() },
          note = "an outcome this host cannot establish still says which configuration produced it" });

// J3: adding the settings seam added nothing to the boundary.
var snapshotProperty = typeof(OperationRecord).GetProperty(nameof(OperationRecord.ConfigurationSnapshot))!;
string[] refsAfterSeam = typeof(IOperationLedger).Assembly.GetReferencedAssemblies()
    .Select(a => a.Name!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
Check("J3 the settings seam crosses as a string and adds no dependency to the contract",
    snapshotProperty.PropertyType == typeof(string)
        && refsAfterSeam.SequenceEqual(new[] { "System.Collections", "System.Runtime" }),
    new { seamType = snapshotProperty.PropertyType.Name, contractReferences = refsAfterSeam,
          note = "no settings type crosses; the lifetime side never interprets the value" });

// J4: an outcome that could not be persisted still leaves its configuration on the record, so the
// degraded case does not also lose which configuration was in force.
Process jOwnerThree = StartIdleOwner();
IOperationLease degraded = jLedger.Begin("envJ4", "10.0.0.0", new ProcessOwner(jOwnerThree), "cfg-3");
jLedger.FailEndPersistenceForTests = true;
degraded.Complete(OperationState.Succeeded, "outcome-lost-to-storage");
jLedger.FailEndPersistenceForTests = false;
string[] jLines = File.ReadAllLines(jEvidence);
bool beginCarriesSnapshot = jLines.Any(l => l.Contains(degraded.Id, StringComparison.Ordinal)
    && l.Contains("\"configurationSnapshot\":\"cfg-3\"", StringComparison.Ordinal));
Check("J4 a scope degraded by a storage failure still records which configuration was in force",
    beginCarriesSnapshot && jLedger.DegradedScopes.Contains("envJ4")
        && jLedger.UnpersistedOperations.Contains(degraded.Id),
    new { snapshotOnDisk = beginCarriesSnapshot, degraded = jLedger.DegradedScopes.Contains("envJ4"),
          unpersisted = jLedger.UnpersistedOperations.Contains(degraded.Id),
          note = "the admission line carries it, so losing the outcome does not also lose the context" });

// J5: the seam did not break anything already built. The two releases under artifacts/ were compiled
// against the PREVIOUS contract and are not recompiled by this run; they call the three-argument
// Begin. An optional parameter would have been source compatible and binary incompatible -- the first
// attempt at this seam did exactly that, and every prebuilt release died with MissingMethodException.
bool oldSignatureKept = typeof(IOperationLedger).GetMethod(nameof(IOperationLedger.Begin),
    new[] { typeof(string), typeof(string), typeof(object) }) is not null;
string[] prebuiltEffects = File.Exists(fEffect) ? File.ReadAllLines(fEffect) : [];
bool prebuiltReleasesExecuted = prebuiltEffects.Any(l => l.Contains("v1-executed-by-10.0.0.0", StringComparison.Ordinal))
    && prebuiltEffects.Any(l => l.Contains("v2-executed-by-10.1.0.0", StringComparison.Ordinal));
Check("J5 a release built against the previous contract still admits operations",
    oldSignatureKept && prebuiltReleasesExecuted,
    new { threeArgumentOverloadKept = oldSignatureKept, prebuiltReleasesExecuted,
          note = "these effect lines were written by binaries this run never recompiled" });

degraded.Dispose();
foreach (Process owner in new[] { jOwnerOne, jOwnerTwo, jOwnerThree }) {
    try { owner.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already gone */ }
}


// ── K: making the two silent failures loud ────────────────────────────────────────────────────────
// Both found by @vladimir-nikonov wiring the liveness correction into his own harness: a bare Process
// owner is skipped by resolution SILENTLY, and a snapshot reference set that counts unresolved owners
// pins a snapshot forever. Neither shows up until a drain or a cleanup simply never finishes.
string kEvidence = Path.Combine(work, "k-operations.jsonl");
var kLedger = new OperationLedger(kEvidence);
Process kWrapped = StartIdleOwner();
Process kBare = StartIdleOwner();
IOperationLease wrapped = kLedger.Begin("envK", "10.0.0.0", new ProcessOwner(kWrapped), "cfg-A");
IOperationLease bare = kLedger.Begin("envK", "10.0.0.0", kBare, "cfg-B");   // the mistake, on purpose

// K1: the ledger says which operations it can never resolve, before anyone waits on them.
Check("K1 an owner that cannot be asked about liveness is reported, not silently skipped",
    kLedger.UnresolvableOwners.Contains(bare.Id) && !kLedger.UnresolvableOwners.Contains(wrapped.Id),
    new { unresolvable = kLedger.UnresolvableOwners.Count, bareListed = kLedger.UnresolvableOwners.Contains(bare.Id),
          wrappedListed = kLedger.UnresolvableOwners.Contains(wrapped.Id),
          note = "visible before the drain rather than as a drain that never ends" });

// K2: killing both owners resolves only the one that can be asked — the bare owner stays Running,
// which is exactly the behaviour K1 warns about, measured rather than described.
kWrapped.Kill(entireProcessTree: true);
kBare.Kill(entireProcessTree: true);
await kWrapped.WaitForExitAsync();
await kBare.WaitForExitAsync();
var wrappedAfter = kLedger.Query(wrapped.Id);
var bareAfter = kLedger.Query(bare.Id);
Check("K2 only the owner that can be asked is resolved; the bare one is the warned-about failure",
    wrappedAfter.State == OperationState.Unknown && bareAfter.State == OperationState.Running,
    new { wrapped = wrappedAfter.State.ToString(), bare = bareAfter.State.ToString(),
          note = "the warning in K1 is not hypothetical; this is what ignoring it costs" });

// K3: snapshot cleanup is safe only against the RESOLVED reference set. cfg-A's owner died and was
// resolved, so cfg-A is free; cfg-B is pinned by an operation nothing can ever resolve.
var referenced = kLedger.ReferencedSnapshots;
Check("K3 a resolved orphan releases its snapshot; an unresolvable one pins it forever",
    !referenced.Contains("cfg-A") && referenced.Contains("cfg-B"),
    new { referencedSnapshots = referenced,
          note = "the cleanup reference set is derived from retention, so it cannot disagree with it" });

fV1 = null!;
fV1Ctx.Unload();

GC.KeepAlive(v2Ctx);
Console.WriteLine(JsonSerializer.Serialize(new {
    os = Environment.OSVersion.VersionString,
    framework = Environment.Version.ToString(),
    workDirectory = work,
    cases = observations
}, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

// ────────────────────────────────────────────────────────────────────────────────────────────────────

// Runs an operation to completion, then lets ONE runtime-defined value escape to the caller. The
// release is unloaded before returning, so anything keeping it alive afterwards is that value.
[MethodImpl(MethodImplOptions.NoInlining)]
static async Task<(WeakReference Weak, object Escaped)> PhaseEscapedResult(
    OperationLedger ledger, string releaseDirectory, string effectPath) {
    var (context, runtime) = Load(releaseDirectory);
    string id = runtime.StartDetached(ledger, "envO", effectPath, 100, "succeed", CancellationToken.None);
    await WaitTerminal(ledger, id, TimeSpan.FromSeconds(20));
    object escaped = runtime.CreateRuntimeDefinedResult();
    var weak = new WeakReference(context, trackResurrection: true);
    runtime = null!;
    context.Unload();
    return (weak, escaped);
}

// Runs one operation whose terminal evidence write fails, then retires the release that produced it.
// Kept in its own frame so no local keeps that release alive for the collectibility assertion.
[MethodImpl(MethodImplOptions.NoInlining)]
static async Task<(OperationLedger Ledger, string Id, WeakReference Weak)> PhaseDegradedPersistence(
    string releaseDirectory, string work) {
    var ledger = new OperationLedger(Path.Combine(work, "fault.jsonl")) { FailEndPersistenceForTests = true };
    var (context, runtime) = Load(releaseDirectory);
    string id = runtime.StartDetached(ledger, "envP", Path.Combine(work, "p1-effect.log"), 100,
        "succeed", CancellationToken.None);
    await WaitTerminal(ledger, id, TimeSpan.FromSeconds(20));
    var weak = new WeakReference(context, trackResurrection: true);
    runtime = null!;
    context.Unload();
    return (ledger, id, weak);
}

// Quiescence now trails the published outcome: ownership ends at Dispose, not at Complete. Anything
// asserting "idle after it finished" has to wait for that, or it races the cleanup window by design.
static async Task<bool> WaitQuiescent(IOperationLedger ledger, string? target, TimeSpan budget) {
    DateTime deadline = DateTime.UtcNow + budget;
    while (DateTime.UtcNow < deadline) {
        if (ledger.IsQuiescent(target)) return true;
        await Task.Delay(25);
    }
    return ledger.IsQuiescent(target);
}

// Item 2: the portable boundary beyond returned values. Each phase lets exactly one thing escape and
// reports whether the release outlived it, so the comparison is between forms, not between fixtures.
[MethodImpl(MethodImplOptions.NoInlining)]
static async Task<(WeakReference Weak, Exception? Escaped, string PortableCode, bool HeldHostCallback,
        List<string> Progress)> PhasePortableBoundary(string releaseDirectory) {
    var (context, runtime) = Load(releaseDirectory);
    var progress = new List<string>();
    runtime.ReportProgressTo(progress.Add, 3);            // a HOST delegate crosses in
    bool heldAfterCall = runtime.HoldsHostCallback;
    string portableCode = runtime.TryRuntimeDefinedError().Code;
    Exception? escaped = null;
    try { runtime.ThrowRuntimeDefinedError(); }
    catch (Exception error) { escaped = error; }          // a RUNTIME type crosses out
    var weak = new WeakReference(context, trackResurrection: true);
    runtime = null!;
    context.Unload();
    await Task.Yield();
    return (weak, escaped, portableCode, heldAfterCall, progress);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static async Task<(WeakReference Weak, string PortableOnly)> PhasePortableOnly(string releaseDirectory) {
    var (context, runtime) = Load(releaseDirectory);
    string code = runtime.TryRuntimeDefinedError().Code;   // only strings cross out
    var weak = new WeakReference(context, trackResurrection: true);
    runtime = null!;
    context.Unload();
    await Task.Yield();
    return (weak, code);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static async Task<(WeakReference Weak, object Callback)> PhaseEscapedCallback(string releaseDirectory) {
    var (context, runtime) = Load(releaseDirectory);
    object callback = runtime.CreateRuntimeDefinedCallback();
    var weak = new WeakReference(context, trackResurrection: true);
    runtime = null!;
    context.Unload();
    await Task.Yield();
    return (weak, callback);
}

// Compatibility is decided BEFORE activation: the release is loaded to be inspected, then discarded if
// it is the wrong generation. Nothing already running is consulted or disturbed.
[MethodImpl(MethodImplOptions.NoInlining)]
static (AssemblyLoadContext Context, IDetachedRuntime Runtime) LoadCompatible(string releaseDirectory,
    int supportedGeneration) {
    var (context, runtime) = Load(releaseDirectory);
    if (runtime.ContractVersion != supportedGeneration) {
        int declared = runtime.ContractVersion;
        string version = runtime.Version;
        context.Unload();
        throw new IncompatibleReleaseException(version, declared);
    }
    return (context, runtime);
}

// The partner assembly is loaded into its own context and given the vendor runtime to compose. It
// references the contract and nothing else of ours.
[MethodImpl(MethodImplOptions.NoInlining)]
static IPartnerWorkflow LoadPartner(string partnerDirectory) {
    var context = new ReleaseContext(partnerDirectory);
    Assembly assembly = context.LoadFromAssemblyPath(Path.Combine(partnerDirectory, "Partner.Workflow.dll"));
    Type type = assembly.GetType("Partner.Workflow.ReportingWorkflow")
        ?? throw new InvalidOperationException("The partner assembly does not expose ReportingWorkflow.");
    return (IPartnerWorkflow)Activator.CreateInstance(type)!;
}

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

    bool a2bIdle = await WaitQuiescent(ledger, "envA", TimeSpan.FromSeconds(10));
    check("A2b the target is quiescent once the operation releases ownership",
        a2bIdle && ledger.IsQuiescent(),
        new { envA = a2bIdle, global = ledger.IsQuiescent() });

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

/// <summary>Starts a real process whose only job is to exist until it is killed.</summary>
static Process StartIdleOwner() {
    var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true };
    if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet",
            StringComparison.OrdinalIgnoreCase)) {
        start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
    }
    start.ArgumentList.Add("idle");
    Process owner = Process.Start(start)!;
    owner.StandardOutput.ReadLine();                 // wait until it is genuinely up
    return owner;
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

/// <summary>Raised when a release declares a contract generation this host does not support.</summary>
sealed class IncompatibleReleaseException(string version, int declared)
    : InvalidOperationException($"release {version} declares contract generation {declared}") {
    public int Declared { get; } = declared;
}

/// <summary>A real operating-system process as an operation owner; liveness is the process itself.</summary>
file sealed class ProcessOwner(Process process) : IOwnerLiveness {
    public bool IsAlive => !process.HasExited;
}
