using System.Reflection;
using Clio10.DetachedOperations;
using Clio10.DetachedOperations.Host;
using Clio10.SettingsVersioning;

// Versioned settings, against Alexandr-Kravchuk's ConfigurationSnapshot seam
// (experiments/DetachedOperations/Contract/Contract.cs) and consuming his OperationHeldSnapshots /
// OwnersWithoutLiveness additions rather than rebuilding a parallel reference count. No MCP transport here
// -- see the class doc on SettingsStore for why that is a narrowing of an earlier, over-broad claim.
string workDir = Directory.CreateTempSubdirectory("settings-versioning-").FullName;
string ledgerEvidence = Path.Combine(workDir, "operations.jsonl");
string settingsEvidence = Path.Combine(workDir, "settings.jsonl");

var observations = new List<object>();
bool failed = false;
void Check(string name, bool ok, object detail) {
    observations.Add(new { name, passed = ok, detail });
    if (!ok) failed = true;
}

T1_CoexistWithV2();
T2_FailedMigrationLeavesV1Usable();
T3_ConcurrentEditIsRefusedNotOverwritten();
T4_RollbackRestoresExactlyAndRefusesAgainstNewerEdit();
T4b_RollbackRetentionSurvivesCleanup();
T5_CleanupRespectsRetentionNotJustPin();
T6_CredentialFreeSurface();
T7_NoSentinelSecretInPersistedArtifacts();
T8_IdleCurrentSnapshotSurvivesCleanup();
T9_ConcurrentPrepareIsSerializedNotRaced();
T10_AdmitIsSerializedAgainstCleanup();
T11_MutatingCallersDictionaryAfterPrepareDoesNotReachTheSnapshot();
T12_MigrateDetectsAnEditThatLandsAfterItsReadNotJustBeforeItsCommit();
T13_ImmutableDictionaryRejectsMutationThroughEveryAlias();

Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { cases = observations },
    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

void T1_CoexistWithV2() {
    var ledger = new OperationLedger(ledgerEvidence);
    var store = new SettingsStore(ledger, settingsEvidence);
    SettingsSnapshot cfgA = store.Prepare("envT1", new Dictionary<string, string> { ["schema"] = "v1" }, 0);
    var ownerA = new FakeOwner();
    IOperationLease leaseA = store.Admit("envT1", "V1", ownerA, "envT1");

    SettingsSnapshot cfgB = store.Prepare("envT1", new Dictionary<string, string> { ["schema"] = "v2" }, cfgA.Version);
    var ownerB = new FakeOwner();
    IOperationLease leaseB = store.Admit("envT1", "V2", ownerB, "envT1");

    OperationRecord recA = ledger.Query(leaseA.Id);
    OperationRecord recB = ledger.Query(leaseB.Id);
    Check("T1: an op admitted under V1 keeps cfg-A's identity after V2 activates",
        recA.ConfigurationSnapshot == cfgA.Id && recB.ConfigurationSnapshot == cfgB.Id,
        new { cfgA = cfgA.Id, cfgB = cfgB.Id, recA = recA.ConfigurationSnapshot, recB = recB.ConfigurationSnapshot });
    Check("T1: V1's own values are still readable, unchanged, while V2 is active",
        store.Values(cfgA.Id)["schema"] == "v1" && store.CurrentSnapshotId("envT1") == cfgB.Id,
        new { v1Schema = store.Values(cfgA.Id)["schema"], active = store.CurrentSnapshotId("envT1") });

    leaseA.Complete(OperationState.Succeeded); leaseA.Dispose();
    leaseB.Complete(OperationState.Succeeded); leaseB.Dispose();
}

void T2_FailedMigrationLeavesV1Usable() {
    var ledger = new OperationLedger(Path.Combine(workDir, "operations-t2.jsonl"));
    var store = new SettingsStore(ledger, Path.Combine(workDir, "settings-t2.jsonl"));
    SettingsSnapshot cfgA = store.Prepare("envT2", new Dictionary<string, string> { ["schema"] = "v1", ["k"] = "orig" }, 0);
    IOperationLease lease = store.Admit("envT2", "V1", new FakeOwner(), "envT2");

    store.FailMigrationForTests = true;
    InvalidOperationException? caught = null;
    try { store.Migrate("envT2", values => new Dictionary<string, string>(values) { ["schema"] = "v2" }); }
    catch (InvalidOperationException ex) { caught = ex; }
    store.FailMigrationForTests = false;

    Check("T2: a failed migration throws and never activates a new snapshot",
        caught is not null && store.CurrentSnapshotId("envT2") == cfgA.Id,
        new { threw = caught?.Message, stillActive = store.CurrentSnapshotId("envT2"), original = cfgA.Id });
    Check("T2: V1's own values are exactly as they were, not partially written",
        store.Values(cfgA.Id)["k"] == "orig" && store.Values(cfgA.Id)["schema"] == "v1",
        store.Values(cfgA.Id));
    Check("T2: the operation admitted under V1 before the failed migration still resolves normally",
        ledger.Query(lease.Id).State == OperationState.Running,
        new { state = ledger.Query(lease.Id).State.ToString() });

    lease.Complete(OperationState.Succeeded); lease.Dispose();
}

void T3_ConcurrentEditIsRefusedNotOverwritten() {
    var ledger = new OperationLedger(Path.Combine(workDir, "operations-t3.jsonl"));
    var store = new SettingsStore(ledger, Path.Combine(workDir, "settings-t3.jsonl"));
    SettingsSnapshot cfgA = store.Prepare("envT3", new Dictionary<string, string> { ["v"] = "0" }, 0);

    // Two writers both read version 1 (cfgA). Writer 1 commits first.
    SettingsSnapshot writer1 = store.Prepare("envT3", new Dictionary<string, string> { ["v"] = "1-from-writer1" }, cfgA.Version);
    ConcurrencyConflictException? refused = null;
    try { store.Prepare("envT3", new Dictionary<string, string> { ["v"] = "1-from-writer2" }, cfgA.Version); }
    catch (ConcurrencyConflictException ex) { refused = ex; }
    Check("T3: a second writer based on the same stale version is refused, not merged or overwritten",
        refused is not null && store.CurrentSnapshotId("envT3") == writer1.Id,
        new { refusedWith = refused?.Message, stillActive = store.CurrentSnapshotId("envT3") });

    // Mutation control, observed rather than asserted: the same race with the check disabled silently
    // loses writer 1's edit.
    store.SkipConcurrencyCheckForTests = true;
    SettingsSnapshot writer2 = store.Prepare("envT3", new Dictionary<string, string> { ["v"] = "1-from-writer2" }, cfgA.Version);
    store.SkipConcurrencyCheckForTests = false;
    Check("T3 MUTATION: with the check disabled, writer 2 silently replaces writer 1 -- the failure the check exists to prevent",
        store.CurrentSnapshotId("envT3") == writer2.Id && store.Values(writer2.Id)["v"] == "1-from-writer2",
        new { note = "writer1's edit is gone from 'current' with no error at any point", active = store.CurrentSnapshotId("envT3") });
}

void T4_RollbackRestoresExactlyAndRefusesAgainstNewerEdit() {
    var ledger = new OperationLedger(Path.Combine(workDir, "operations-t4.jsonl"));
    var store = new SettingsStore(ledger, Path.Combine(workDir, "settings-t4.jsonl"));
    SettingsSnapshot cfgA = store.Prepare("envT4", new Dictionary<string, string> { ["schema"] = "v1" }, 0);
    SettingsSnapshot cfgB = store.Prepare("envT4", new Dictionary<string, string> { ["schema"] = "v2-broken" }, cfgA.Version);

    store.Rollback("envT4", cfgA.Id, cfgB.Version);
    Check("T4: rollback restores V1's exact snapshot identity and values, not a copy",
        store.CurrentSnapshotId("envT4") == cfgA.Id && store.Values(store.CurrentSnapshotId("envT4"))["schema"] == "v1",
        new { active = store.CurrentSnapshotId("envT4") });

    // A legitimate edit lands after the rollback. A second rollback attempt still carrying the
    // pre-edit version must be refused, not silently discard the newer edit.
    SettingsSnapshot legitimateEdit = store.Prepare("envT4", new Dictionary<string, string> { ["schema"] = "v1-patched" }, store.CurrentVersion("envT4"));
    ConcurrencyConflictException? refused = null;
    try { store.Rollback("envT4", cfgA.Id, cfgA.Version); } // stale expectedCurrentVersion, pre-dates legitimateEdit
    catch (ConcurrencyConflictException ex) { refused = ex; }
    Check("T4: a rollback based on a stale version is refused rather than clobbering a newer legitimate edit",
        refused is not null && store.CurrentSnapshotId("envT4") == legitimateEdit.Id,
        new { refusedWith = refused?.Message, stillActive = store.CurrentSnapshotId("envT4") });
}

void T4b_RollbackRetentionSurvivesCleanup() {
    // Without explicit retention: a superseded snapshot with zero references is reclaimable, and a
    // later rollback attempt against it genuinely fails -- kirillkrylov's "retained rollback
    // configuration" gap, reproduced rather than assumed.
    var ledgerBare = new OperationLedger(Path.Combine(workDir, "operations-t4b-bare.jsonl"));
    var storeBare = new SettingsStore(ledgerBare, Path.Combine(workDir, "settings-t4b-bare.jsonl"));
    SettingsSnapshot cfgA = storeBare.Prepare("envT4b", new Dictionary<string, string> { ["schema"] = "v1" }, 0);
    storeBare.Prepare("envT4b", new Dictionary<string, string> { ["schema"] = "v2" }, cfgA.Version);
    IReadOnlyCollection<string> doomed = storeBare.Cleanup();
    KeyNotFoundException? failedRollback = null;
    try { storeBare.Rollback("envT4b", cfgA.Id, storeBare.CurrentVersion("envT4b")); }
    catch (KeyNotFoundException ex) { failedRollback = ex; }
    Check("T4b MUTATION: without RetainForRollback, cleanup reclaims the prior snapshot and rollback to it genuinely fails",
        doomed.Contains(cfgA.Id) && failedRollback is not null,
        new { doomed, rollbackFailed = failedRollback?.Message });

    // With explicit retention: the same sequence, but the caller marks cfg-A' as a rollback target
    // before cleanup runs. It survives, and rollback to it succeeds.
    var ledgerRetained = new OperationLedger(Path.Combine(workDir, "operations-t4b-retained.jsonl"));
    var storeRetained = new SettingsStore(ledgerRetained, Path.Combine(workDir, "settings-t4b-retained.jsonl"));
    SettingsSnapshot cfgA2 = storeRetained.Prepare("envT4b", new Dictionary<string, string> { ["schema"] = "v1" }, 0);
    storeRetained.RetainForRollback(cfgA2.Id);
    storeRetained.Prepare("envT4b", new Dictionary<string, string> { ["schema"] = "v2" }, cfgA2.Version);
    IReadOnlyCollection<string> doomed2 = storeRetained.Cleanup();
    storeRetained.Rollback("envT4b", cfgA2.Id, storeRetained.CurrentVersion("envT4b"));
    Check("T4b: with RetainForRollback, cleanup leaves the prior snapshot alone and rollback to it succeeds",
        !doomed2.Contains(cfgA2.Id) && storeRetained.CurrentSnapshotId("envT4b") == cfgA2.Id,
        new { doomed = doomed2, activeAfterRollback = storeRetained.CurrentSnapshotId("envT4b") });

    // Explicit operator decision, mirrors AcceptLoss/RepairDegraded: release the retention, then
    // supersede cfg-A2 so it is no longer the pinned snapshot either -- now nothing protects it.
    storeRetained.ReleaseRollbackRetention(cfgA2.Id);
    storeRetained.Prepare("envT4b", new Dictionary<string, string> { ["schema"] = "v3" }, storeRetained.CurrentVersion("envT4b"));
    IReadOnlyCollection<string> doomed3 = storeRetained.Cleanup();
    Check("T4b: releasing retention makes the snapshot reclaimable again once nothing else protects it",
        doomed3.Contains(cfgA2.Id),
        new { doomed = doomed3 });
}

void T8_IdleCurrentSnapshotSurvivesCleanup() {
    // kirillkrylov: "a snapshot may have zero active operations and still be the current configuration
    // for the next admission" -- OperationHeldSnapshots alone would miss this; Cleanup() must not.
    var ledger = new OperationLedger(Path.Combine(workDir, "operations-t8.jsonl"));
    var store = new SettingsStore(ledger, Path.Combine(workDir, "settings-t8.jsonl"));
    SettingsSnapshot cfgX = store.Prepare("envT8", new Dictionary<string, string> { ["k"] = "x" }, 0);
    // Deliberately no Admit() call: this snapshot has never been referenced by any operation.
    IReadOnlyCollection<string> doomed = store.Cleanup();
    Check("T8: a current snapshot with zero admitted operations survives cleanup",
        !doomed.Contains(cfgX.Id) && store.Contains(cfgX.Id) && !ledger.OperationHeldSnapshots.Contains(cfgX.Id),
        new { doomed, referencedByLedger = ledger.OperationHeldSnapshots, note = "protected by 'pinned', not by OperationHeldSnapshots" });
}

void T5_CleanupRespectsRetentionNotJustPin() {
    var ledger = new OperationLedger(Path.Combine(workDir, "operations-t5.jsonl"));
    var store = new SettingsStore(ledger, Path.Combine(workDir, "settings-t5.jsonl"));

    // cfg-C: no longer pinned (a later snapshot is active) but still referenced by a retained operation.
    SettingsSnapshot cfgC = store.Prepare("envT5", new Dictionary<string, string> { ["k"] = "c" }, 0);
    IOperationLease leaseUnderC = store.Admit("envT5-op", "V1", new FakeOwner(), "envT5");
    SettingsSnapshot cfgD = store.Prepare("envT5", new Dictionary<string, string> { ["k"] = "d" }, cfgC.Version);

    IReadOnlyCollection<string> doomed1 = store.Cleanup();
    Check("T5: cleanup does not delete an unpinned snapshot that a retained operation still references",
        !doomed1.Contains(cfgC.Id) && store.Contains(cfgC.Id),
        new { doomed = doomed1, referencedByLedger = ledger.OperationHeldSnapshots });

    leaseUnderC.Complete(OperationState.Succeeded); leaseUnderC.Dispose();
    IReadOnlyCollection<string> doomed2 = store.Cleanup();
    Check("T5: once the operation completes and releases ownership, cleanup removes it",
        doomed2.Contains(cfgC.Id) && !store.Contains(cfgC.Id),
        new { doomed = doomed2 });

    // K3 shape: a resolved orphan releases its snapshot; an unresolvable (bare) owner pins one forever.
    SettingsSnapshot cfgF = store.Prepare("envT5", new Dictionary<string, string> { ["k"] = "f" }, store.CurrentVersion("envT5"));
    var wrappedOwner = new FakeOwner();
    IOperationLease leaseF = store.Admit("envT5-wrapped", "V1", wrappedOwner, "envT5");
    SettingsSnapshot cfgG = store.Prepare("envT5", new Dictionary<string, string> { ["k"] = "g" }, cfgF.Version);
    IOperationLease leaseG = store.Admit("envT5-bare", "V1", new object(), "envT5"); // deliberately unresolvable
    SettingsSnapshot cfgH = store.Prepare("envT5", new Dictionary<string, string> { ["k"] = "h" }, cfgG.Version); // moves the pin off F and G

    wrappedOwner.Kill();
    var referencedAfterKill = new HashSet<string>(ledger.OperationHeldSnapshots, StringComparer.Ordinal); // triggers orphan resolution
    Check("T5 (K3): the wrapped owner's death releases cfg-F; the bare owner still pins cfg-G",
        !referencedAfterKill.Contains(cfgF.Id) && referencedAfterKill.Contains(cfgG.Id),
        new { referenced = referencedAfterKill, resolvedState = ledger.Query(leaseF.Id).State.ToString(),
              unresolvableState = ledger.Query(leaseG.Id).State.ToString(),
              unresolvableOwners = ledger.OwnersWithoutLiveness });

    IReadOnlyCollection<string> doomed3 = store.Cleanup();
    Check("T5 (K3): cleanup can now reclaim cfg-F but must not touch cfg-G while its owner is unresolvable",
        doomed3.Contains(cfgF.Id) && !doomed3.Contains(cfgG.Id) && store.Contains(cfgG.Id),
        new { doomed = doomed3 });

    leaseG.Complete(OperationState.Succeeded); leaseG.Dispose(); // release cleanly so the process can exit
}

void T6_CredentialFreeSurface() {
    // Not a compile-fail trick -- a checked assertion on the type's own public surface, so the guarantee
    // is verified rather than merely intended (Alexandr-Kravchuk's suggestion, same shape as F4/J3).
    string[] suspect = { "key", "secret", "password", "token", "credential", "connectionstring", "pwd" };
    PropertyInfo[] members = typeof(SettingsSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance);
    string[] flagged = members
        .Where(p => suspect.Any(s => p.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
        .Select(p => p.Name).ToArray();
    Check("T6: SettingsSnapshot's public surface has no credential-shaped member",
        flagged.Length == 0,
        new { members = members.Select(p => p.Name).ToArray(), flagged });
}

void T7_NoSentinelSecretInPersistedArtifacts() {
    const string sentinel = "SENTINEL-SECRET-DO-NOT-PERSIST-3f9a";
    var credentials = new CredentialStore();
    credentials.Set("envT7", sentinel);

    string opEvidence = Path.Combine(workDir, "operations-t7.jsonl");
    string setEvidence = Path.Combine(workDir, "settings-t7.jsonl");
    var ledger = new OperationLedger(opEvidence);
    var store = new SettingsStore(ledger, setEvidence);
    SettingsSnapshot cfgA = store.Prepare("envT7", new Dictionary<string, string> { ["endpoint"] = "https://example.invalid" }, 0);
    IOperationLease lease = store.Admit("envT7", "V1", new FakeOwner(), "envT7");
    store.Migrate("envT7", values => new Dictionary<string, string>(values) { ["endpoint"] = "https://example2.invalid" });
    store.Rollback("envT7", cfgA.Id, store.CurrentVersion("envT7"));
    lease.Complete(OperationState.Succeeded); lease.Dispose();
    store.Cleanup();

    // Resolve the credential exactly once, the way a real caller would, without ever handing it to the store.
    string resolved = credentials.Resolve("envT7");
    Check("T7: resolving the credential returns the sentinel (sanity: the store under test actually has it)",
        resolved == sentinel, new { resolvedLength = resolved.Length });

    string opText = File.ReadAllText(opEvidence);
    string setText = File.ReadAllText(setEvidence);
    Check("T7: the sentinel secret never appears in the ledger's persisted evidence file",
        !opText.Contains(sentinel, StringComparison.Ordinal), new { path = opEvidence });
    Check("T7: the sentinel secret never appears in the settings store's persisted evidence file",
        !setText.Contains(sentinel, StringComparison.Ordinal), new { path = setEvidence });
}

void T9_ConcurrentPrepareIsSerializedNotRaced() {
    // kirillkrylov: "prefer deterministic handshakes for actual overlap rather than sequential
    // stale-version calls." T3's mutation control is sequential; this proves the fix under genuine
    // concurrent callers, forcing the interleaving instead of hoping timing produces it.
    var ledger = new OperationLedger(Path.Combine(workDir, "operations-t9.jsonl"));
    var store = new SettingsStore(ledger, Path.Combine(workDir, "settings-t9.jsonl"));
    SettingsSnapshot cfgA = store.Prepare("envT9", new Dictionary<string, string> { ["v"] = "0" }, 0);

    using var aInsideLock = new ManualResetEventSlim(false);
    using var releaseA = new ManualResetEventSlim(false);
    store.OnPreparePassedCheckForTests = () => { aInsideLock.Set(); releaseA.Wait(); };

    Task<SettingsSnapshot> taskA = Task.Run(() =>
        store.Prepare("envT9", new Dictionary<string, string> { ["v"] = "from-A" }, cfgA.Version));
    if (!aInsideLock.Wait(TimeSpan.FromSeconds(5)))
        throw new TimeoutException("T9 setup: A never reached the critical section");

    store.OnPreparePassedCheckForTests = null; // B must not also trip the hook
    Task<ConcurrencyConflictException?> taskB = Task.Run(() => {
        try {
            store.Prepare("envT9", new Dictionary<string, string> { ["v"] = "from-B" }, cfgA.Version);
            return (ConcurrencyConflictException?)null;
        }
        catch (ConcurrencyConflictException ex) { return ex; }
    });
    bool bFinishedWhileABlocked = taskB.Wait(TimeSpan.FromMilliseconds(300));

    releaseA.Set();
    SettingsSnapshot resultA = taskA.Result;
    ConcurrencyConflictException? resultB = taskB.Result;

    Check("T9: while A holds the lock mid-Prepare, a concurrent B cannot even begin its own check",
        !bFinishedWhileABlocked, new { bFinishedBeforeReleasingA = bFinishedWhileABlocked });
    Check("T9: after A completes, B's same-base call is correctly refused -- forced overlap, not a timing guess",
        resultB is not null && store.CurrentSnapshotId("envT9") == resultA.Id,
        new { aSucceeded = resultA.Id, bRefusedWith = resultB?.Message });
}

void T10_AdmitIsSerializedAgainstCleanup() {
    // kirillkrylov: "activation and cleanup between those steps can delete the ID being admitted."
    var ledger = new OperationLedger(Path.Combine(workDir, "operations-t10.jsonl"));
    var store = new SettingsStore(ledger, Path.Combine(workDir, "settings-t10.jsonl"));
    SettingsSnapshot cfgA = store.Prepare("envT10", new Dictionary<string, string> { ["k"] = "a" }, 0);
    // Supersede so cfg-A is unpinned and unreferenced -- eligible for cleanup the instant nothing else
    // protects it, exactly the window the race needs.
    SettingsSnapshot cfgB = store.Prepare("envT10", new Dictionary<string, string> { ["k"] = "b" }, cfgA.Version);

    using var admitInsideLock = new ManualResetEventSlim(false);
    using var releaseAdmit = new ManualResetEventSlim(false);
    store.OnAdmitReadSnapshotIdForTests = () => { admitInsideLock.Set(); releaseAdmit.Wait(); };

    Task<IOperationLease> admitTask = Task.Run(() => store.Admit("envT10-op", "V1", new FakeOwner(), "envT10"));
    if (!admitInsideLock.Wait(TimeSpan.FromSeconds(5)))
        throw new TimeoutException("T10 setup: Admit never reached the critical section");

    store.OnAdmitReadSnapshotIdForTests = null;
    Task<IReadOnlyCollection<string>> cleanupTask = Task.Run(() => store.Cleanup());
    bool cleanupFinishedWhileAdmitBlocked = cleanupTask.Wait(TimeSpan.FromMilliseconds(300));

    releaseAdmit.Set();
    IOperationLease lease = admitTask.Result;
    IReadOnlyCollection<string> doomed = cleanupTask.Result;

    Check("T10: cleanup cannot run while an admission is mid-flight between reading and registering",
        !cleanupFinishedWhileAdmitBlocked, new { cleanupFinishedBeforeReleasingAdmit = cleanupFinishedWhileAdmitBlocked });
    string admittedSnapshot = ledger.Query(lease.Id).ConfigurationSnapshot!;
    Check("T10: cleanup, once it runs, still correctly reclaims what is genuinely unowned (cfg-A) without disturbing the admission (cfg-B)",
        doomed.Contains(cfgA.Id) && admittedSnapshot == cfgB.Id && store.Contains(cfgB.Id),
        new { doomed, admittedSnapshot });

    lease.Complete(OperationState.Succeeded); lease.Dispose();
}

void T11_MutatingCallersDictionaryAfterPrepareDoesNotReachTheSnapshot() {
    var ledger = new OperationLedger(Path.Combine(workDir, "operations-t11.jsonl"));
    var store = new SettingsStore(ledger, Path.Combine(workDir, "settings-t11.jsonl"));
    var callerOwned = new Dictionary<string, string> { ["k"] = "original" };
    SettingsSnapshot cfgA = store.Prepare("envT11", callerOwned, 0);

    callerOwned["k"] = "mutated-after-admission"; // the caller still holds this exact reference
    callerOwned["new-key"] = "should-not-appear";

    Check("T11: mutating the dictionary the caller originally passed does not change the pinned snapshot",
        store.Values(cfgA.Id)["k"] == "original" && !store.Values(cfgA.Id).ContainsKey("new-key"),
        new { pinned = store.Values(cfgA.Id), callerNow = callerOwned });
}

void T12_MigrateDetectsAnEditThatLandsAfterItsReadNotJustBeforeItsCommit() {
    var ledger = new OperationLedger(Path.Combine(workDir, "operations-t12.jsonl"));
    var store = new SettingsStore(ledger, Path.Combine(workDir, "settings-t12.jsonl"));
    SettingsSnapshot cfgA = store.Prepare("envT12", new Dictionary<string, string> { ["k"] = "orig" }, 0);

    using var migrateReadSource = new ManualResetEventSlim(false);
    using var releaseMigrate = new ManualResetEventSlim(false);
    Task<ConcurrencyConflictException?> migrateTask = Task.Run(() => {
        try {
            store.Migrate("envT12", values => {
                migrateReadSource.Set(); // source + base revision already captured at this point
                releaseMigrate.Wait();   // hold here so a genuinely intervening edit can land
                return new Dictionary<string, string>(values) { ["k"] = "migrated" };
            });
            return (ConcurrencyConflictException?)null;
        }
        catch (ConcurrencyConflictException ex) { return ex; }
    });
    if (!migrateReadSource.Wait(TimeSpan.FromSeconds(5)))
        throw new TimeoutException("T12 setup: Migrate's transform never started");

    SettingsSnapshot interveningEdit = store.Prepare("envT12", new Dictionary<string, string> { ["k"] = "edited-concurrently" }, cfgA.Version);
    releaseMigrate.Set();

    ConcurrencyConflictException? refused = migrateTask.Result;
    Check("T12: an edit landing after Migrate reads its source, before it commits, is detected -- not silently superseded",
        refused is not null && store.CurrentSnapshotId("envT12") == interveningEdit.Id,
        new { refusedWith = refused?.Message, stillActive = store.CurrentSnapshotId("envT12") });
}

void T13_ImmutableDictionaryRejectsMutationThroughEveryAlias() {
    // kirillkrylov: an interface-only "read-only" guarantee (IReadOnlyDictionary over a plain
    // Dictionary) does not survive a cast back to IDictionary. T11 only covered the ORIGINAL caller
    // input; this covers the other two aliases he named: the value Values() returns, and the value a
    // Migrate transform receives -- both now backed by ImmutableDictionary, which rejects a mutating
    // call through any interface it satisfies rather than silently succeeding.
    var ledger = new OperationLedger(Path.Combine(workDir, "operations-t13.jsonl"));
    var store = new SettingsStore(ledger, Path.Combine(workDir, "settings-t13.jsonl"));
    SettingsSnapshot cfgA = store.Prepare("envT13", new Dictionary<string, string> { ["k"] = "orig" }, 0);

    // Alias 1: the returned value from Values().
    NotSupportedException? returnedValueRejected = null;
    try { ((IDictionary<string, string>)store.Values(cfgA.Id))["k"] = "mutated-via-returned-value"; }
    catch (NotSupportedException ex) { returnedValueRejected = ex; }
    Check("T13: casting Values()'s returned collection to a mutable interface and writing through it is rejected",
        returnedValueRejected is not null && store.Values(cfgA.Id)["k"] == "orig",
        new { rejectedWith = returnedValueRejected?.Message, stillOrig = store.Values(cfgA.Id)["k"] });

    // Alias 2: the value a Migrate transform receives, mutated in place before the transform throws.
    NotSupportedException? transformInputRejected = null;
    InvalidOperationException? migrationThrew = null;
    try {
        store.Migrate("envT13", values => {
            try { ((IDictionary<string, string>)values)["k"] = "mutated-via-transform-input"; }
            catch (NotSupportedException ex) { transformInputRejected = ex; }
            throw new InvalidOperationException("transform aborts after attempting a mutation");
        });
    }
    catch (InvalidOperationException ex) { migrationThrew = ex; }
    Check("T13: casting the transform's input to a mutable interface and writing through it is rejected",
        transformInputRejected is not null,
        new { rejectedWith = transformInputRejected?.Message });
    Check("T13: after a mutating-then-throwing transform, V1's stored snapshot is exactly as it was",
        migrationThrew is not null && store.Values(cfgA.Id)["k"] == "orig",
        new { migrationThrew = migrationThrew?.Message, v1Now = store.Values(cfgA.Id) });
}

/// <summary>An in-memory owner whose liveness can be flipped without a real process, for settings-lane cases that don't need one.</summary>
file sealed class FakeOwner : IOwnerLiveness {
    private volatile bool _alive = true;
    public bool IsAlive => _alive;
    public void Kill() => _alive = false;
}
