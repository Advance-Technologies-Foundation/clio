using Clio10.PrimitiveContracts;
using Clio10.Contracts;

namespace Clio10.Composition.Packages;

/// <summary>Selected package files relative to the resolved content root.</summary>
public sealed record PackageArchiveSource(string Root, IReadOnlyList<string> Files);

/// <summary>Owns package layout and ignore policy, without filesystem implementation dependencies.</summary>
public interface IPackageArchiveSelection {
    /// <summary>Resolves a single branch, applies package content and ignore rules, and returns the selected files.</summary>
    Task<PackageArchiveSource> SelectAsync(IWorkflowContext context, string root, bool skipPdb, CancellationToken cancellationToken);
}

/// <summary>Applies gitignore rules through the same parser family used by Clio 8, with deterministic nested scope.</summary>
public sealed class PackageArchiveSelection(Func<Ignore.Ignore> createMatcher) : IPackageArchiveSelection {
    private static readonly HashSet<string> ContentDirectories = new(StringComparer.OrdinalIgnoreCase) {
        "Assemblies", "Bin", "Data", "Files", "Resources", "Schemas", "SqlScripts"
    };
    /// <inheritdoc />
    public async Task<PackageArchiveSource> SelectAsync(IWorkflowContext context, string root, bool skipPdb, CancellationToken cancellationToken) {
        string repositoryRoot = root;
        var archive = context.Get<IArchivePrimitive>();
        var entries = await archive.ListEntriesAsync(root, cancellationToken);
        var branches = entries.SingleOrDefault(x => x.Name == "branches" && x.IsDirectory);
        if (branches is not null) {
            if (branches.IsLink) throw new InvalidDataException("Package branch root cannot be a link.");
            var versions = (await archive.ListEntriesAsync(Path.Combine(root, "branches"), cancellationToken)).Where(x => x.IsDirectory).ToArray();
            if (versions.Length != 1 || versions[0].IsLink) throw new InvalidDataException("Exactly one package branch is required.");
            root = Path.Combine(root, "branches", versions[0].Name);
            entries = await archive.ListEntriesAsync(root, cancellationToken);
        }
        if (!entries.Any(x => x.Name == "descriptor.json" && !x.IsDirectory && !x.IsLink))
            throw new FileNotFoundException("Package descriptor is missing.");
        var files = new List<string> { "descriptor.json" };
        foreach (var directory in entries.Where(x => x.IsDirectory && ContentDirectories.Contains(x.Name))) {
            if (directory.IsLink) throw new InvalidDataException("Package content cannot traverse a link.");
            files.AddRange((await archive.ListFilesAsync(Path.Combine(root, directory.Name), cancellationToken)).Select(x => directory.Name + "/" + x));
        }
        var matcher = createMatcher();
        await AddRules(Path.Combine(repositoryRoot, "..", "..", ".clio", "clioignore"), "", optional: true);
        // A root ignore file controls selection but is not itself package content.
        if (entries.Any(x => x.Name == "clioignore" && !x.IsDirectory)) {
            if (entries.Single(x => x.Name == "clioignore").IsLink) throw new InvalidDataException("Ignore rules cannot traverse links.");
            await AddRules(Path.Combine(root, "clioignore"), "", optional: false);
        }
        foreach (string ignore in files.Where(x => x.EndsWith("/clioignore", StringComparison.Ordinal))
            .OrderBy(x => x.Count(c => c == '/')).ThenBy(x => x, StringComparer.Ordinal))
            if (!ParentIgnored(ignore)) await AddRules(Path.Combine(root, ignore), ignore[..^"clioignore".Length], optional: false);
        var selected = files.Where(x => (!skipPdb || !x.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) && !ParentIgnored(x) && !matcher.IsIgnored(x))
            .Order(StringComparer.Ordinal).ToArray();
        return new(root, selected);

        bool ParentIgnored(string path) {
            // The matcher evaluates one path; gitignore also forbids re-including descendants of an ignored directory.
            for (int separator = path.IndexOf('/'); separator >= 0; separator = path.IndexOf('/', separator + 1))
                if (matcher.IsIgnored(path[..(separator + 1)])) return true;
            return false;
        }

        async Task AddRules(string path, string prefix, bool optional) {
            string content;
            try { content = await context.Get<IFileSystemPrimitive>().ReadTextAsync(path, cancellationToken); }
            catch (FileNotFoundException) when (optional) { return; }
            catch (DirectoryNotFoundException) when (optional) { return; }
            foreach (string raw in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')) {
                if (prefix.Length == 0 || raw.Length == 0 || raw.StartsWith('#')) { matcher.Add(raw); continue; }
                bool negate = raw.StartsWith('!');
                string pattern = negate ? raw[1..] : raw;
                if (pattern.Length == 0) continue;
                bool anchored = pattern.StartsWith('/') || pattern.TrimEnd('/').Contains('/');
                // Keep nested patterns scoped to their directory; ** also matches zero intervening directories.
                matcher.Add((negate ? "!" : "") + "/" + prefix + (anchored ? "" : "**/") + pattern.TrimStart('/'));
            }
        }
    }
}
