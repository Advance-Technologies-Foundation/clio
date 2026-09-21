using System.Reflection;
using System.Text.Json;
using Clio10.Contracts;

namespace Clio10.Core;

/// <summary>Checks the shared assembly identity before any release implementation is activated.</summary>
internal static class BundleCompatibility {
    internal static void ValidateRuntimeDependencies(string entryAssembly) {
        using var dependencies = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(entryAssembly, ".deps.json")));
        string target = dependencies.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
        foreach (var library in dependencies.RootElement.GetProperty("targets").GetProperty(target).EnumerateObject()) {
            if (!library.Value.TryGetProperty("runtime", out var assets)) continue;
            foreach (var asset in assets.EnumerateObject()) {
                if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                // Bundle payloads are complete, flat managed dependency outputs.
                if (!File.Exists(Path.Combine(Path.GetDirectoryName(entryAssembly)!, Path.GetFileName(asset.Name))))
                    throw new InvalidDataException("A private runtime dependency is missing.");
            }
        }
    }
    internal static void ValidateContracts(string directory) {
        ValidateAssembly(directory, typeof(IPrimitiveBundle).Assembly);
    }
    internal static void ValidateAssembly(string directory, Assembly assembly) {
        var expected = assembly.GetName();
        var supplied = AssemblyName.GetAssemblyName(Path.Combine(directory, expected.Name + ".dll"));
        if (supplied.Name != expected.Name || supplied.Version != expected.Version)
            throw new InvalidDataException("The release requires a different Contracts ABI.");
    }
}
