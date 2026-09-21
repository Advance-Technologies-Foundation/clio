using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Clio10.DetachedOperations;
using Clio10.DetachedOperations.Host;
using Clio10.JointIntegration;
using Clio10.SettingsVersioning;

// The joint proof @kirillkrylov assigned: a partner workflow pinned to one release while another
// arrives with changed settings, with an outcome-storage failure injected. Nothing here reimplements
// either half -- the ledger is the lifetime lane's, the settings owner is @vladimir-nikonov's, both
// compiled from their own sources.
if (args.Length >= 1 && args[0] == "idle") { Console.WriteLine("ready"); Console.Out.Flush(); Thread.Sleep(Timeout.Infinite); return 0; }
if (args.Length < 5) {
    Console.Error.WriteLine("usage: jointintegration <v1Dir> <v2Dir> <partnerDir> <incompatibleDir> <workDir>");
    return 2;
}
string v1Dir = Path.GetFullPath(args[0]), v2Dir = Path.GetFullPath(args[1]);
string partnerDir = Path.GetFullPath(args[2]);
string incompatibleDir = Path.GetFullPath(args[3]);
string work = Path.GetFullPath(args[4]);
Directory.CreateDirectory(work);

var observations = new List<object>();
bool failed = false;
void Check(string name, bool ok, object detail) {
    observations.Add(new { name, passed = ok, detail });
    if (!ok) failed = true;
}

var ledger = new OperationLedger(Path.Combine(work, "operations.jsonl"));
var settings = new SettingsStore(ledger, Path.Combine(work, "settings.jsonl"));
string effect = Path.Combine(work, "effect.log");

// Admission goes through the published pair, never through two separate reads. SettingsStore.Admit was
// removed this round at my request precisely so there is one path and not two.
IOperationLease AdmitOn(string scope, string runtimeVersion, string snapshotId, object owner) {
    ActivationSelection selection = ledger.CurrentSelection(scope);
    ledger.TryPublishSelection(scope, runtimeVersion, snapshotId, selection.Generation);
    return ledger.BeginFromSelection(scope, owner);
}

// cfg-1 is prepared and becomes current; V1 is the activated release.
SettingsSnapshot cfg1 = settings.PrepareAndActivate("envX", new Dictionary<string, string> { ["mode"] = "one" }, 0);
var (v1Context, v1) = Load(v1Dir);

// X1: the assigned case. A partner composes the pinned release, an operation is admitted under cfg-1,
// then V2 AND cfg-2 arrive underneath, and the operation's outcome fails to persist.
IPartnerWorkflow partner = LoadPartner(partnerDir);
var composed = partner.Compose(v1);

Process owner = StartIdleOwner();
IOperationLease pinned = AdmitOn("envX", v1.Version, cfg1.Id, new ProcessOwner(owner));
var (v2Context, v2) = Load(v2Dir);                      // the new release arrives
SettingsSnapshot cfg2 = settings.PrepareAndActivate("envX", new Dictionary<string, string> { ["mode"] = "two" },
    settings.CurrentVersion("envX"));                   // and the new settings

var stillPinned = ledger.Query(pinned.Id);
ledger.FailEndPersistenceForTests = true;               // the storage failure, injected
pinned.Complete(OperationState.Succeeded, "work-really-succeeded");
ledger.FailEndPersistenceForTests = false;
var afterFailure = ledger.Query(pinned.Id);

IOperationLease onNew = AdmitOn("envX", v2.Version, cfg2.Id, new ProcessOwner(owner));
var newAdmission = ledger.Query(onNew.Id);

Check("X1 a partner stays on its pinned release and snapshot while a new release and new settings arrive",
    composed.RuntimeVersion == v1.Version
        && stillPinned.ConfigurationSnapshot == cfg1.Id
        && afterFailure.ConfigurationSnapshot == cfg1.Id
        && newAdmission.RuntimeVersion == v2.Version && newAdmission.ConfigurationSnapshot == cfg2.Id,
    new { partnerRanAgainst = composed.RuntimeVersion, pinnedSnapshot = stillPinned.ConfigurationSnapshot,
          newRelease = newAdmission.RuntimeVersion, newSnapshot = newAdmission.ConfigurationSnapshot,
          note = "release and configuration are pinned by the same rule, and neither moved under it" });

// X2: the storage failure degraded evidence WITHOUT rewriting the outcome, and the record still knows
// which configuration produced it -- the case where that knowledge matters most.
Check("X2 a failed evidence write degrades the scope without touching the outcome or losing the snapshot",
    afterFailure.State == OperationState.Succeeded && afterFailure.Code == "work-really-succeeded"
        && ledger.DegradedScopes.Contains("envX")
        && ledger.UnpersistedOperations.Contains(pinned.Id)
        && afterFailure.ConfigurationSnapshot == cfg1.Id,
    new { outcome = afterFailure.State.ToString(), code = afterFailure.Code,
          degraded = ledger.DegradedScopes.Contains("envX"),
          snapshotStillKnown = afterFailure.ConfigurationSnapshot,
          note = "successful work must not become failed work because a disk write failed" });

// X3: THE INTEGRATION FINDING. Cleanup() consults OperationHeldSnapshots, which resolves orphans -- but
// an owner with no liveness is never resolved, so its snapshot is retained forever and Cleanup() reports
// nothing unusual. The only signal is OwnersWithoutLiveness, which the settings owner does not consult.
pinned.Dispose();
onNew.Dispose();
settings.RetainForRollback(cfg1.Id);
Process bareOwner = StartIdleOwner();
SettingsSnapshot cfg3 = settings.PrepareAndActivate("envY", new Dictionary<string, string> { ["mode"] = "three" }, 0);
IOperationLease unresolvable = AdmitOn("envY", v1.Version, cfg3.Id, bareOwner);   // bare, on purpose
SettingsSnapshot cfg4 = settings.PrepareAndActivate("envY", new Dictionary<string, string> { ["mode"] = "four" },
    settings.CurrentVersion("envY"));
bareOwner.Kill(entireProcessTree: true);
await bareOwner.WaitForExitAsync();
CleanupResult bareOwnerCleanup = settings.Cleanup();
IReadOnlyCollection<string> deletedWithBareOwner = bareOwnerCleanup.Reclaimed;
Check("X3 a cross-process owner registered without liveness holds its snapshot, and cleanup now says so",
    !deletedWithBareOwner.Contains(cfg3.Id) && settings.Contains(cfg3.Id)
        && ledger.OwnersWithoutLiveness.Contains(unresolvable.Id)
        && bareOwnerCleanup.HeldByOwnerWithoutLiveness.Count > 0
        && ledger.Query(unresolvable.Id).State == OperationState.Running,
    new { reclaimed = deletedWithBareOwner, cfg3SurvivedCleanup = settings.Contains(cfg3.Id),
          listedByLedger = ledger.OwnersWithoutLiveness.Contains(unresolvable.Id),
          stateOfDeadOwnersOperation = ledger.Query(unresolvable.Id).State.ToString(),
          reportedByCleanup = bareOwnerCleanup.HeldByOwnerWithoutLiveness.Count,
          note = "a diagnostic, not a verdict: an in-process owner ending at Dispose appears here legitimately" });

// X4 negative control: the same sequence with a liveness-capable owner reclaims the snapshot, so X3 is
// about the missing liveness and not about cleanup being broken.
Process wrappedOwner = StartIdleOwner();
SettingsSnapshot cfg5 = settings.PrepareAndActivate("envZ", new Dictionary<string, string> { ["mode"] = "five" }, 0);
IOperationLease resolvable = AdmitOn("envZ", v1.Version, cfg5.Id, new ProcessOwner(wrappedOwner));
SettingsSnapshot cfg6 = settings.PrepareAndActivate("envZ", new Dictionary<string, string> { ["mode"] = "six" },
    settings.CurrentVersion("envZ"));
wrappedOwner.Kill(entireProcessTree: true);
await wrappedOwner.WaitForExitAsync();
CleanupResult wrappedOwnerCleanup = settings.Cleanup();
IReadOnlyCollection<string> deletedWithWrappedOwner = wrappedOwnerCleanup.Reclaimed;
Check("X4 negative control: with a liveness-capable owner the same snapshot IS reclaimed",
    deletedWithWrappedOwner.Contains(cfg5.Id) && !settings.Contains(cfg5.Id)
        && ledger.Query(resolvable.Id).State == OperationState.Unknown,
    new { reclaimed = deletedWithWrappedOwner, cfg5Gone = !settings.Contains(cfg5.Id),
          stateOfDeadOwnersOperation = ledger.Query(resolvable.Id).State.ToString(),
          note = "same sequence, one difference: the owner could be asked" });

// ── X5: one coordination boundary, not an ordering ───────────────────────────────────────────────
// @kirillkrylov's correction to my first proposal: prepare -> activate runtime -> activate settings is
// NOT sufficient, because an admission between the last two steps sees new-runtime/old-settings, and a
// settings commit that fails after the runtime is activated leaves the same split. So the pair is
// PUBLISHED as a unit through the ledger, and admission takes both halves from one read.
// Publishing is the lifetime lane's (mine); preparing and committing settings is @vladimir-nikonov's.
const string pairedScope = "envW";
SettingsSnapshot first = settings.PrepareAndActivate(pairedScope,
    new Dictionary<string, string> { ["mode"] = "committed" }, 0);
ledger.TryPublishSelection(pairedScope, v1.Version, first.Id, 0);
ActivationSelection committed = ledger.CurrentSelection(pairedScope);

Process wOwner = StartIdleOwner();

// X5a: the runtime half is refused. The candidate is prepared but never activated, and because the
// pair is published only after BOTH halves succeed, nothing about the live selection moves.
SettingsSnapshot candidateForRejected = settings.Prepare(pairedScope,
    new Dictionary<string, string> { ["mode"] = "with-rejected" }, settings.CurrentVersion(pairedScope));
bool runtimeRefused = false;
try {
    var (rejectedContext, rejected) = Load(incompatibleDir);
    if (rejected.ContractVersion != 1) { runtimeRefused = true; rejectedContext.Unload(); }
}
catch (Exception) { runtimeRefused = true; }
if (runtimeRefused) settings.AbandonCandidate(candidateForRejected.Id);   // no publication attempted

IOperationLease afterRefusal = ledger.BeginFromSelection(pairedScope, new ProcessOwner(wOwner));
var pairAfterRefusal = ledger.Query(afterRefusal.Id);
Check("X5a a refused runtime leaves the committed pair untouched, and admissions still see it",
    runtimeRefused
        && pairAfterRefusal.RuntimeVersion == v1.Version
        && pairAfterRefusal.ConfigurationSnapshot == first.Id
        && ledger.CurrentSelection(pairedScope).Generation == committed.Generation,
    new { runtimeRefused, runtimeSeen = pairAfterRefusal.RuntimeVersion,
          snapshotSeen = pairAfterRefusal.ConfigurationSnapshot,
          generationUnchanged = ledger.CurrentSelection(pairedScope).Generation == committed.Generation,
          note = "the pair is published only after both halves succeed, so a refusal is a non-event" });

// X5b: the runtime half succeeds and the SETTINGS COMMIT then fails. This is the case my first
// ordering proposal got wrong -- by then the runtime would already be activated.
SettingsSnapshot candidateForFailedCommit = settings.Prepare(pairedScope,
    new Dictionary<string, string> { ["mode"] = "commit-fails" }, settings.CurrentVersion(pairedScope));
bool settingsCommitFailed = false;
settings.FailNextActivatePersistForTests = true;
try { settings.Activate(pairedScope, candidateForFailedCommit.Id, settings.CurrentVersion(pairedScope)); }
catch (Exception) { settingsCommitFailed = true; }
IOperationLease afterCommitFailure = ledger.BeginFromSelection(pairedScope, new ProcessOwner(wOwner));
var pairAfterCommitFailure = ledger.Query(afterCommitFailure.Id);
Check("X5b a settings commit failure after the runtime half leaves the previous pair usable",
    settingsCommitFailed
        && pairAfterCommitFailure.RuntimeVersion == v1.Version
        && pairAfterCommitFailure.ConfigurationSnapshot == first.Id
        && ledger.CurrentSelection(pairedScope).Generation == committed.Generation,
    new { settingsCommitFailed, runtimeSeen = pairAfterCommitFailure.RuntimeVersion,
          snapshotSeen = pairAfterCommitFailure.ConfigurationSnapshot,
          note = "publication never happened, so there is no rollback step that could itself fail" });

// X5c: an admission held EXACTLY at the boundary. It runs while the publication holds the lock, so it
// is serialised against it and must observe one whole pair -- never the new runtime with the old
// snapshot, which is the interleaving @kirillkrylov named.
SettingsSnapshot second = settings.Prepare(pairedScope,
    new Dictionary<string, string> { ["mode"] = "second" }, settings.CurrentVersion(pairedScope));
settings.Activate(pairedScope, second.Id, settings.CurrentVersion(pairedScope));
string? heldRuntime = null, heldSnapshot = null;
var admissionEntered = new ManualResetEventSlim(false);
ledger.OnPublishInsideBoundaryForTests = () => {
    var admission = Task.Run(() => {
        admissionEntered.Set();
        using IOperationLease held = ledger.BeginFromSelection(pairedScope, new ProcessOwner(wOwner));
        var record = ledger.Query(held.Id);
        heldRuntime = record.RuntimeVersion;
        heldSnapshot = record.ConfigurationSnapshot;
    });
    admissionEntered.Wait(TimeSpan.FromSeconds(5));
    Thread.Sleep(150);                       // the admission is now blocked on the boundary, deliberately
    _ = admission;                           // completes after the publication releases it
};
ledger.TryPublishSelection(pairedScope, v2.Version, second.Id, committed.Generation);
ledger.OnPublishInsideBoundaryForTests = null;
Thread.Sleep(400);
bool coherent = (heldRuntime == v1.Version && heldSnapshot == first.Id)
    || (heldRuntime == v2.Version && heldSnapshot == second.Id);
Check("X5c an admission held at the boundary observes one whole pair, never a mixture",
    heldRuntime is not null && coherent,
    new { heldRuntime, heldSnapshot, oldPair = new { v1.Version, snapshot = first.Id },
          newPair = new { v2.Version, snapshot = second.Id }, coherent,
          note = "admission and publication share one critical section, so there is no halfway state" });

// X5d MUTATION CONTROL: the naive caller that reads the two halves separately. Deterministic, not
// raced for -- the publication happens between its two reads, through the same boundary hook.
string naiveRuntime = ledger.CurrentSelection(pairedScope).RuntimeVersion;
SettingsSnapshot third = settings.Prepare(pairedScope,
    new Dictionary<string, string> { ["mode"] = "third" }, settings.CurrentVersion(pairedScope));
settings.Activate(pairedScope, third.Id, settings.CurrentVersion(pairedScope));
ledger.TryPublishSelection(pairedScope, v1.Version, third.Id,
    ledger.CurrentSelection(pairedScope).Generation);
string? naiveSnapshot = ledger.CurrentSelection(pairedScope).ConfigurationSnapshot;
IOperationLease naive = ledger.Begin(pairedScope, naiveRuntime, new ProcessOwner(wOwner), naiveSnapshot);
var naivePair = ledger.Query(naive.Id);
bool naiveMixed = naivePair.RuntimeVersion == v2.Version && naivePair.ConfigurationSnapshot == third.Id;
Check("X5d mutation control: reading the halves separately admits a pair that was never current",
    naiveMixed,
    new { admittedRuntime = naivePair.RuntimeVersion, admittedSnapshot = naivePair.ConfigurationSnapshot,
          everCurrentTogether = false,
          note = "this is what BeginFromSelection exists to prevent; it fails here on purpose" });

afterRefusal.Dispose();
afterCommitFailure.Dispose();
naive.Dispose();
try { wOwner.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }

// ── X6/X7: the requirement the removal of SettingsStore.Admit put on this boundary ───────────────
// @vladimir-nikonov removed Admit at my request and named precisely what moved with it: Admit's
// lock-protected read-then-register kept a two-step admission from straddling a concurrent Cleanup.
// That guarantee is now BeginFromSelection's. Checking it surfaced a second, larger gap: the published
// pair can go STALE, and then the guarantee protects the wrong snapshot.
const string coordScope = "envC";
var coordinator = new ActivationCoordinator(ledger, settings);
SettingsSnapshot cFirst = settings.PrepareAndActivate(coordScope,
    new Dictionary<string, string> { ["mode"] = "c1" }, 0);
ledger.TryPublishSelection(coordScope, v1.Version, cFirst.Id, 0);
Process cOwner = StartIdleOwner();

// X6: a settings-only change goes through the coordinator, so selected and pinned stay equal and an
// admission can never land on a snapshot cleanup has reclaimed.
SettingsSnapshot cSecond = settings.Prepare(coordScope,
    new Dictionary<string, string> { ["mode"] = "c2" }, settings.CurrentVersion(coordScope));
coordinator.TryCommitSettingsOnly(coordScope, cSecond.Id);
settings.Cleanup();
IOperationLease coordinated = ledger.BeginFromSelection(coordScope, new ProcessOwner(cOwner));
string? coordinatedSnapshot = ledger.Query(coordinated.Id).ConfigurationSnapshot;
Check("X6 after a settings-only activation through the coordinator, admission lands on a live snapshot",
    coordinatedSnapshot == cSecond.Id && settings.Contains(coordinatedSnapshot!),
    new { admittedUnder = coordinatedSnapshot, stillExists = settings.Contains(coordinatedSnapshot!),
          selected = ledger.CurrentSelection(coordScope).ConfigurationSnapshot,
          pinned = settings.CurrentSnapshotId(coordScope),
          note = "the rule is that any activation republishes the pair, so selected and pinned are equal" });

// X7 MUTATION CONTROL: the same settings-only change driven directly, bypassing the coordinator. The
// selection still names the OLD snapshot, which is then neither pinned nor held nor retained, so
// Cleanup reclaims it -- and the next admission is registered under a snapshot that no longer exists.
const string staleScope = "envS";
SettingsSnapshot sFirst = settings.PrepareAndActivate(staleScope,
    new Dictionary<string, string> { ["mode"] = "s1" }, 0);
ledger.TryPublishSelection(staleScope, v1.Version, sFirst.Id, 0);
SettingsSnapshot sSecond = settings.Prepare(staleScope,
    new Dictionary<string, string> { ["mode"] = "s2" }, settings.CurrentVersion(staleScope));
settings.Activate(staleScope, sSecond.Id, settings.CurrentVersion(staleScope));   // no republication
CleanupResult staleCleanup = settings.Cleanup();
IOperationLease stale = ledger.BeginFromSelection(staleScope, new ProcessOwner(cOwner));
string? staleSnapshot = ledger.Query(stale.Id).ConfigurationSnapshot;
Check("X7 mutation control: skipping the republication admits under a snapshot cleanup reclaimed",
    staleCleanup.Reclaimed.Contains(sFirst.Id) && staleSnapshot == sFirst.Id
        && !settings.Contains(staleSnapshot!),
    new { reclaimed = staleCleanup.Reclaimed, admittedUnder = staleSnapshot,
          snapshotStillExists = settings.Contains(staleSnapshot!),
          pinnedInStore = settings.CurrentSnapshotId(staleScope),
          note = "passing here is the point: it shows what X6's rule prevents" });

coordinated.Dispose();
stale.Dispose();
try { cOwner.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }

Console.WriteLine(JsonSerializer.Serialize(new {
    os = Environment.OSVersion.VersionString, framework = Environment.Version.ToString(),
    ledgerFrom = "DetachedOperations (lifetime lane)", settingsFrom = "SettingsVersioning (settings lane)",
    cases = observations
}, new JsonSerializerOptions { WriteIndented = true }));
GC.KeepAlive(v1Context); GC.KeepAlive(v2Context); GC.KeepAlive(cfg4); GC.KeepAlive(cfg6);
return failed ? 1 : 0;

static (AssemblyLoadContext Context, IDetachedRuntime Runtime) Load(string directory) {
    var context = new ReleaseContext(directory);
    Assembly assembly = context.LoadFromAssemblyPath(
        Path.Combine(directory, "Clio10.DetachedRuntimeFixture.dll"));
    Type type = assembly.GetType("Clio10.DetachedOperationsFixture.DetachedRuntime")!;
    return (context, (IDetachedRuntime)Activator.CreateInstance(type)!);
}

static IPartnerWorkflow LoadPartner(string directory) {
    var context = new ReleaseContext(directory);
    Assembly assembly = context.LoadFromAssemblyPath(Path.Combine(directory, "Partner.Workflow.dll"));
    return (IPartnerWorkflow)Activator.CreateInstance(
        assembly.GetType("Partner.Workflow.ReportingWorkflow")!)!;
}

// A real process that exists only to be an owner and then be killed. Same shape as the E3 host's
// idle mode: the point is a genuine OS process, not a stand-in for one.
static Process StartIdleOwner() {
    var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true };
    if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet",
            StringComparison.OrdinalIgnoreCase)) {
        start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
    }
    start.ArgumentList.Add("idle");
    Process owner = Process.Start(start)!;
    owner.StandardOutput.ReadLine();
    return owner;
}

file sealed class ReleaseContext(string directory) : AssemblyLoadContext(isCollectible: true) {
    protected override Assembly? Load(AssemblyName assemblyName) {
        string candidate = Path.Combine(directory, assemblyName.Name + ".dll");
        if (assemblyName.Name == "Clio10.DetachedOperations.Contract") return null;   // shared
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }
}

file sealed class ProcessOwner(Process process) : IOwnerLiveness {
    public bool IsAlive => !process.HasExited;
}
