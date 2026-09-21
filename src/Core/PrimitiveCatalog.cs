using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Clio10.Contracts;

namespace Clio10.Core;

/// <summary>Selects trusted local primitive bundles using declared compatibility.</summary>
public interface IPrimitiveCatalog {
    /// <summary>Safe diagnostics for rejected candidates and unavailable sources.</summary>
    IReadOnlyList<string> Diagnostics { get; }
    /// <summary>Returns a compatible provider or fails before external execution.</summary>
    IPrimitiveBundle Select(PrimitiveRequirement requirement);
}

/// <summary>Metadata read before loading executable code from a bundle directory.</summary>
public sealed record PrimitiveManifest(string Version, int ContractVersion, string[] Capabilities,
    string Assembly, string EntryType, int? RuntimeContractVersion = null);

/// <summary>Filters metadata before lazy activation; malformed unrelated bundles do not disable healthy ones.</summary>
public sealed class PrimitiveCatalog : IPrimitiveCatalog, IDisposable {
    private readonly List<Candidate> _candidates = [];
    private readonly List<AssemblyLoadContext> _contexts = [];
    private readonly List<string> _diagnostics = [];
    private readonly object _sync = new();
    private bool _disposed;
    private string? _sourceError;
    private readonly string? _directory;
    private readonly bool _runtimeComposition;
    private readonly IReadOnlyList<Assembly> _sharedCapabilities;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    /// <summary>Safe diagnostic codes for rejected bundle directories.</summary>
    public IReadOnlyList<string> Diagnostics { get { lock (_sync) return _diagnostics.ToArray(); } }

    /// <summary>Discovers manifests from immediate child directories; defaults apply without a bundle directory.</summary>
    public PrimitiveCatalog(CoreOptions options, IEnumerable<IPrimitiveBundle> defaults) {
        _directory = options.BundleDirectory;
        _runtimeComposition = options.RuntimeComposition;
        _sharedCapabilities = options.RuntimeComposition ? [] : options.SharedCapabilityAssemblies.ToArray();
        if (_directory is null) {
            foreach (var bundle in defaults) _candidates.Add(new(BundleVersion.Normalize(bundle.Version), bundle.ContractVersion, bundle.Capabilities, null, null, bundle));
            return;
        }
        Refresh();
    }
    private void Refresh() {
        if (_directory is null) return;
        _sourceError = null;
        if (!Directory.Exists(_directory)) {
            _sourceError = "bundle-directory-unavailable";
            Diagnose(_sourceError);
            return;
        }
        string[] directories;
        try { directories = Directory.GetDirectories(_directory); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            _sourceError = "unreadable-bundle-directory";
            Diagnose(_sourceError);
            return;
        }
        foreach (var directory in directories) {
            if (Path.GetFileName(directory).StartsWith('.') || _seen.Contains(directory)) continue;
            try {
                var manifest = JsonSerializer.Deserialize<PrimitiveManifest>(File.ReadAllText(Path.Combine(directory, "bundle.json")))
                    ?? throw new JsonException();
                if (!Version.TryParse(manifest.Version, out var version) || manifest.Capabilities is null ||
                    string.IsNullOrWhiteSpace(manifest.Assembly) || Path.GetFileName(manifest.Assembly) != manifest.Assembly ||
                    string.IsNullOrWhiteSpace(manifest.EntryType)) throw new JsonException();
                if ((_runtimeComposition && manifest.RuntimeContractVersion != 1) || (!_runtimeComposition && manifest.RuntimeContractVersion is not null)) { Diagnose("incompatible-runtime-contract"); continue; }
                _candidates.Add(new(BundleVersion.Normalize(version), manifest.ContractVersion, manifest.Capabilities,
                    Path.GetFullPath(Path.Combine(directory, manifest.Assembly)), manifest.EntryType, null));
                _seen.Add(directory);
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or ArgumentException) {
                Diagnose("invalid-bundle-manifest");
            }
        }
    }

    private void Diagnose(string code) { if (!_diagnostics.Contains(code)) _diagnostics.Add(code); }

    /// <inheritdoc />
    public IPrimitiveBundle Select(PrimitiveRequirement requirement) {
        lock (_sync) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Refresh();
            if (_sourceError is not null && _candidates.Count == 0) throw new CoreResolutionException(_sourceError, "The configured bundle directory is unavailable.");
            var matches = _candidates.Where(x => !x.Failed && x.Contract == 2 && requirement.Matches(x.Version, x.Capabilities))
                .OrderByDescending(x => x.Version).ToArray();
            foreach (var candidate in matches) {
                if (matches.Count(x => x.Version == candidate.Version) > 1)
                    throw new CoreResolutionException("ambiguous-bundle", "Multiple bundles declare the selected version.");
                try {
                    if (candidate.Bundle is not null) return candidate.Bundle;
                    BundleCompatibility.ValidateContracts(Path.GetDirectoryName(candidate.Path!)!);
                    BundleCompatibility.ValidateRuntimeDependencies(candidate.Path!);
                    foreach (var shared in _sharedCapabilities)
                        BundleCompatibility.ValidateAssembly(Path.GetDirectoryName(candidate.Path!)!, shared);
                    var context = new BundleLoadContext(candidate.Path!, _sharedCapabilities);
                    try {
                        var assembly = context.LoadFromAssemblyPath(candidate.Path!);
                        var type = assembly.GetType(candidate.EntryType!, throwOnError: true)!;
                        var bundle = Activator.CreateInstance(type) as IPrimitiveBundle
                            ?? throw new InvalidOperationException("Invalid bundle entry point.");
                        if (_runtimeComposition && (bundle is not IRuntimeBundle runtime || runtime.RuntimeContractVersion != 1))
                            throw new InvalidOperationException("Invalid runtime factory.");
                        if (BundleVersion.Normalize(bundle.Version) != candidate.Version || bundle.ContractVersion != candidate.Contract ||
                            !bundle.Capabilities.Order().SequenceEqual(candidate.Capabilities.Order()))
                            throw new InvalidOperationException("Bundle does not match its manifest.");
                        candidate.Bundle = bundle;
                        _contexts.Add(context);
                        return bundle;
                    }
                    catch { context.Unload(); throw; }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or JsonException or KeyNotFoundException or TypeLoadException or BadImageFormatException or
                    ReflectionTypeLoadException or MissingMethodException or TargetInvocationException or InvalidOperationException or ArgumentException) {
                    // A scanner can temporarily hold a newly published Windows DLL. Retry I/O failures on a later root.
                    candidate.Failed = error is not IOException;
                    Diagnose("bundle-activation-failed");
                }
            }
            throw new CoreResolutionException("no-compatible-bundle", "No compatible primitive bundle is available.");
        }
    }

    /// <summary>Requests unload; callers must finish sessions before disposing their service provider.</summary>
    public void Dispose() {
        lock (_sync) {
            _disposed = true;
            _candidates.Clear();
            foreach (var context in _contexts) context.Unload();
            _contexts.Clear();
        }
    }
    private sealed class Candidate(Version version, int contract, IReadOnlyCollection<string> capabilities,
        string? path, string? entryType, IPrimitiveBundle? bundle) {
        public Version Version { get; } = version;
        public int Contract { get; } = contract;
        public IReadOnlyCollection<string> Capabilities { get; } = capabilities;
        public string? Path { get; } = path;
        public string? EntryType { get; } = entryType;
        public IPrimitiveBundle? Bundle { get; set; } = bundle;
        public bool Failed { get; set; }
    }
    private sealed class BundleLoadContext(string path, IReadOnlyList<Assembly> sharedCapabilities) : AssemblyLoadContext(isCollectible: true) {
        private static readonly HashSet<string> FrameworkAssemblies = Directory
            .GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
            .Select(file => Path.GetFileNameWithoutExtension(file)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        private readonly AssemblyDependencyResolver _resolver = new(path);
        protected override Assembly? Load(AssemblyName name) {
            if (name.Name == typeof(IPrimitiveBundle).Assembly.GetName().Name) return typeof(IPrimitiveBundle).Assembly;
            var shared = sharedCapabilities.SingleOrDefault(x => x.GetName().Name == name.Name);
            if (shared is not null) return shared;
            var resolved = _resolver.ResolveAssemblyToPath(name);
            if (resolved is not null) return LoadFromAssemblyPath(resolved);
            if (name.Name is not null && FrameworkAssemblies.Contains(name.Name)) return null;
            // A missing release dependency must never borrow the application's feature implementation.
            throw new FileNotFoundException("A private runtime dependency is missing.", name.Name);
        }
        protected override IntPtr LoadUnmanagedDll(string name) {
            var resolved = _resolver.ResolveUnmanagedDllToPath(name);
            return resolved is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(resolved);
        }
    }
}
