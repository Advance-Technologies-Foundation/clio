using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Clio10.DetachedOperations;
using Clio10.DetachedOperations.Host;
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

// cfg-1 is prepared and becomes current; V1 is the activated release.
SettingsSnapshot cfg1 = settings.Prepare("envX", new Dictionary<string, string> { ["mode"] = "one" }, 0);
var (v1Context, v1) = Load(v1Dir);

// X1: the assigned case. A partner composes the pinned release, an operation is admitted under cfg-1,
// then V2 AND cfg-2 arrive underneath, and the operation's outcome fails to persist.
IPartnerWorkflow partner = LoadPartner(partnerDir);
var composed = partner.Compose(v1);

Process owner = StartIdleOwner();
IOperationLease pinned = settings.Admit("envX", v1.Version, new ProcessOwner(owner), "envX");
var (v2Context, v2) = Load(v2Dir);                      // the new release arrives
SettingsSnapshot cfg2 = settings.Prepare("envX", new Dictionary<string, string> { ["mode"] = "two" },
    settings.CurrentVersion("envX"));                   // and the new settings

var stillPinned = ledger.Query(pinned.Id);
ledger.FailEndPersistenceForTests = true;               // the storage failure, injected
pinned.Complete(OperationState.Succeeded, "work-really-succeeded");
ledger.FailEndPersistenceForTests = false;
var afterFailure = ledger.Query(pinned.Id);

IOperationLease onNew = settings.Admit("envX", v2.Version, new ProcessOwner(owner), "envX");
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
SettingsSnapshot cfg3 = settings.Prepare("envY", new Dictionary<string, string> { ["mode"] = "three" }, 0);
IOperationLease unresolvable = settings.Admit("envY", v1.Version, bareOwner, "envY");   // bare, on purpose
SettingsSnapshot cfg4 = settings.Prepare("envY", new Dictionary<string, string> { ["mode"] = "four" },
    settings.CurrentVersion("envY"));
bareOwner.Kill(entireProcessTree: true);
await bareOwner.WaitForExitAsync();
IReadOnlyCollection<string> deletedWithBareOwner = settings.Cleanup();
Check("X3 a snapshot held by an owner with no liveness is never reclaimed, and cleanup gives no signal",
    !deletedWithBareOwner.Contains(cfg3.Id) && settings.Contains(cfg3.Id)
        && ledger.OwnersWithoutLiveness.Contains(unresolvable.Id)
        && ledger.Query(unresolvable.Id).State == OperationState.Running,
    new { reclaimed = deletedWithBareOwner, cfg3SurvivedCleanup = settings.Contains(cfg3.Id),
          listedByLedger = ledger.OwnersWithoutLiveness.Contains(unresolvable.Id),
          stateOfDeadOwnersOperation = ledger.Query(unresolvable.Id).State.ToString(),
          note = "Cleanup consults OperationHeldSnapshots only; the warning lives in OwnersWithoutLiveness" });

// X4 negative control: the same sequence with a liveness-capable owner reclaims the snapshot, so X3 is
// about the missing liveness and not about cleanup being broken.
Process wrappedOwner = StartIdleOwner();
SettingsSnapshot cfg5 = settings.Prepare("envZ", new Dictionary<string, string> { ["mode"] = "five" }, 0);
IOperationLease resolvable = settings.Admit("envZ", v1.Version, new ProcessOwner(wrappedOwner), "envZ");
SettingsSnapshot cfg6 = settings.Prepare("envZ", new Dictionary<string, string> { ["mode"] = "six" },
    settings.CurrentVersion("envZ"));
wrappedOwner.Kill(entireProcessTree: true);
await wrappedOwner.WaitForExitAsync();
IReadOnlyCollection<string> deletedWithWrappedOwner = settings.Cleanup();
Check("X4 negative control: with a liveness-capable owner the same snapshot IS reclaimed",
    deletedWithWrappedOwner.Contains(cfg5.Id) && !settings.Contains(cfg5.Id)
        && ledger.Query(resolvable.Id).State == OperationState.Unknown,
    new { reclaimed = deletedWithWrappedOwner, cfg5Gone = !settings.Contains(cfg5.Id),
          stateOfDeadOwnersOperation = ledger.Query(resolvable.Id).State.ToString(),
          note = "same sequence, one difference: the owner could be asked" });

// X5: @kirillkrylov's exact acceptance case -- settings prepared SUCCESSFULLY, then the runtime
// activation FAILS. An admission afterwards must observe ONE COMMITTED PAIR, not one half of each.
SettingsSnapshot committedPair = settings.Prepare("envW",
    new Dictionary<string, string> { ["mode"] = "committed" }, 0);
Process wOwner = StartIdleOwner();
IOperationLease beforeFailedActivation = settings.Admit("envW", v1.Version, new ProcessOwner(wOwner), "envW");

// The settings half succeeds first, exactly as a real staged update would do it.
SettingsSnapshot preparedForV3 = settings.Prepare("envW",
    new Dictionary<string, string> { ["mode"] = "with-v3" }, settings.CurrentVersion("envW"));

// The runtime half is then refused: this release declares a contract generation the host cannot serve.
bool activationFailed = false;
try {
    var (rejectedContext, rejected) = Load(incompatibleDir);
    if (rejected.ContractVersion != 1) { activationFailed = true; rejectedContext.Unload(); }
}
catch (Exception) { activationFailed = true; }

IOperationLease afterFailedActivation = settings.Admit("envW", v1.Version, new ProcessOwner(wOwner), "envW");
var pairSeen = ledger.Query(afterFailedActivation.Id);
Check("X5 a failed runtime activation leaves the committed runtime/configuration pair unchanged",
    activationFailed
        && pairSeen.RuntimeVersion == v1.Version
        && pairSeen.ConfigurationSnapshot == committedPair.Id,
    new { activationFailed, runtimeSeen = pairSeen.RuntimeVersion, snapshotSeen = pairSeen.ConfigurationSnapshot,
          committedSnapshot = committedPair.Id, preparedButNeverActivated = preparedForV3.Id,
          snapshotBeforeAttempt = ledger.Query(beforeFailedActivation.Id).ConfigurationSnapshot,
          note = "Prepare activates on success, so a refused runtime leaves settings ahead of it" });
beforeFailedActivation.Dispose();
afterFailedActivation.Dispose();
try { wOwner.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }

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
