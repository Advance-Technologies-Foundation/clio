using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;

// Standalone experimental apparatus, not a production loader or proposed Core API.
if (args.Length != 1) throw new ArgumentException("Pass the Payload build output directory.");
string source = Path.GetFullPath(args[0]);
foreach (string file in new[] { "Payload.dll", "Dependency.dll" })
    if (!File.Exists(Path.Combine(source, file))) throw new FileNotFoundException(file);
string root = Path.Combine(Path.GetTempPath(), "clio-retirement-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var results = new List<object>();
foreach (bool streams in new[] { false, true }) {
    foreach (bool prematureDelete in new[] { false, true }) { foreach (bool warm in new[] { false, true }) {
        string release = Path.Combine(root, $"{streams}-{prematureDelete}-{warm}");
        Directory.CreateDirectory(release);
        foreach (string file in new[] { "Payload.dll", "Dependency.dll" })
            File.Copy(Path.Combine(source, file), Path.Combine(release, file));
        var observation = Exercise(release, streams, prematureDelete, warm);
        for (int i = 0; i < 20 && observation.Context.IsAlive; i++) {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(25);
        }
        bool collected = !observation.Context.IsAlive;
        bool reclaimed = Delete(release);
        bool expected = observation.Initial == "entry-ok" && observation.DependencyWasLazy &&
            collected && reclaimed && (prematureDelete
                ? (warm ? observation.Final == "private-dependency-ok" : (observation.DependencyPresent ? observation.Final == "private-dependency-ok" : observation.FinishError == nameof(FileNotFoundException)))
                : observation.Final == "private-dependency-ok");
        results.Add(new { mode = streams ? "stream" : "path", prematureDelete, dependencyPreloaded = warm,
            observation.Initial, observation.DependencyWasLazy, observation.EarlyDeleted,
            observation.DependencyPresent, observation.Final, observation.FinishError, collected, reclaimed, passed = expected });
        if (!expected) Environment.ExitCode = 1;
    } }
}
Console.WriteLine(JsonSerializer.Serialize(new { os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, results }, new JsonSerializerOptions { WriteIndented = true }));
// Only the unique directory created above is removed, after all contexts have been observed.
if (Environment.ExitCode == 0) Directory.Delete(root, true);

[MethodImpl(MethodImplOptions.NoInlining)]
static Observation Exercise(string release, bool streams, bool prematureDelete, bool warm) {
    var context = new ReleaseContext(release, streams);
    var assembly = context.Open(Path.Combine(release, "Payload.dll"));
    var type = assembly.GetType("ProbePayload.Entry", throwOnError: true)!;
    string initial = (string)type.GetMethod("Start")!.Invoke(null, null)!;
    bool lazy = !context.Assemblies.Any(a => a.GetName().Name == "Dependency");
    if (warm) _ = type.GetMethod("Finish")!.Invoke(null, null);
    bool deleted = prematureDelete && Delete(release);
    bool dependencyPresent = File.Exists(Path.Combine(release, "Dependency.dll"));
    string? final = null, error = null;
    try { final = (string)type.GetMethod("Finish")!.Invoke(null, null)!; }
    catch (TargetInvocationException ex) { error = ex.InnerException?.GetType().Name; }
    // Completion is the release of our controlled execution owner. No runtime is used after this.
    var weak = new WeakReference(context);
    context.Unload();
    return new(weak, initial, lazy, deleted, dependencyPresent, final, error);
}
static bool Delete(string path) {
    try { if (Directory.Exists(path)) Directory.Delete(path, true); return true; }
    catch (IOException) { return false; }
    catch (UnauthorizedAccessException) { return false; }
}
internal sealed record Observation(WeakReference Context, string Initial, bool DependencyWasLazy,
    bool EarlyDeleted, bool DependencyPresent, string? Final, string? FinishError);
internal sealed class ReleaseContext(string directory, bool streams) : AssemblyLoadContext(isCollectible: true) {
    internal Assembly Open(string path) {
        if (!streams) return LoadFromAssemblyPath(path);
        using var input = File.OpenRead(path);
        return LoadFromStream(input);
    }
    protected override Assembly? Load(AssemblyName name) {
        if (name.Name != "Dependency") return null;
        return Open(Path.Combine(directory, "Dependency.dll"));
    }
}
