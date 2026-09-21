using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Clio10.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clio10.Core;

/// <summary>Opt-in trusted NuGet V3 source for complete, immutable primitive bundles.</summary>
/// <remarks>The proof supports stable numeric versions. Packages contain a nuspec and a complete bundle/ directory, not a dependency graph to restore.</remarks>
public sealed record BundleUpdateOptions(Uri Source, string PackageId, TimeSpan Interval, Version Minimum, Version MaximumExclusive);

/// <summary>Downloads beside running bundles and atomically publishes a complete directory; it never replaces loaded files.</summary>
/// <remarks>A Generic Host owns this background service. Plain CLI invocations consume the already installed cache.</remarks>
public sealed class BundleUpdateService(CoreOptions options, ILogger<BundleUpdateService> logger) : BackgroundService {
    private const long DownloadLimit = 64 * 1024 * 1024;
    private const long ExpandedLimit = 256 * 1024 * 1024;
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var update = options.Updates!;
        if (update.Interval <= TimeSpan.Zero || update.Minimum >= update.MaximumExclusive || !ValidSource(update.Source) || string.IsNullOrWhiteSpace(update.PackageId) ||
            update.PackageId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')))
            throw new ArgumentException("Invalid automatic-update configuration.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        using var timer = new PeriodicTimer(update.Interval);
        do {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            try { await CheckAsync(http, update, deadline.Token); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { Diagnose("bundle-update-failed", error); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    private bool MatchesMode(string directory) {
        try {
            var manifest = JsonSerializer.Deserialize<PrimitiveManifest>(File.ReadAllText(Path.Combine(directory, "bundle.json")));
            return manifest is not null && (options.RuntimeComposition ? manifest.RuntimeContractVersion == 1 : manifest.RuntimeContractVersion is null);
        }
        catch (Exception error) when (error is IOException or JsonException) { return false; }
    }
    private async Task CheckAsync(HttpClient http, BundleUpdateOptions update, CancellationToken token) {
        using var index = await ReadJsonAsync(http, update.Source, token);
        var address = index.RootElement.GetProperty("resources").EnumerateArray()
            .First(x => x.GetProperty("@type").GetString() == "PackageBaseAddress/3.0.0").GetProperty("@id").GetString()!;
        var baseUri = new Uri(address.EndsWith('/') ? address : address + '/');
        if (!ValidSource(baseUri)) throw new InvalidDataException("Unsupported package source.");
        string id = update.PackageId.ToLowerInvariant();
        using var versions = await ReadJsonAsync(http, new Uri(baseUri, id + "/index.json"), token);
        var releases = versions.RootElement.GetProperty("versions").EnumerateArray().Select(x => x.GetString()!)
            .Where(x => Version.TryParse(x, out _)).Select(x => (Text: x, Version: BundleVersion.Normalize(Version.Parse(x))))
            .Where(x => x.Version >= BundleVersion.Normalize(update.Minimum) && x.Version < BundleVersion.Normalize(update.MaximumExclusive))
            .Where(x => options.ExactVersion is null || x.Version == BundleVersion.Normalize(options.ExactVersion))
            .OrderByDescending(x => x.Version).ToArray();
        string cache = Path.GetFullPath(options.BundleDirectory!);
        Directory.CreateDirectory(cache);
        foreach (var release in releases) {
            string destination = Path.Combine(cache, release.Version.ToString());
            if (Directory.Exists(destination) && !MatchesMode(destination)) destination += "-" + id;
            if (Directory.Exists(destination)) {
                if (!MatchesMode(destination)) throw new InvalidDataException("Bundle cache identity conflict.");
                return;
            }
            string stage = Path.Combine(cache, ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try {
                string packagePath = Path.Combine(stage, "package.nupkg");
                using (var response = await http.GetAsync(new Uri(baseUri, $"{id}/{release.Text}/{id}.{release.Text}.nupkg"), HttpCompletionOption.ResponseHeadersRead, token)) {
                    response.EnsureSuccessStatusCode();
                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    await using var output = File.Create(packagePath);
                    await CopyBoundedAsync(input, output, DownloadLimit, token);
                }
                string payload = Path.Combine(stage, "payload");
                Directory.CreateDirectory(payload);
                await ExtractAsync(packagePath, payload, id, release.Version, token);
                var manifest = JsonSerializer.Deserialize<PrimitiveManifest>(await File.ReadAllTextAsync(Path.Combine(payload, "bundle.json"), token))
                    ?? throw new InvalidDataException("Missing bundle manifest.");
                if (!Version.TryParse(manifest.Version, out var version) || BundleVersion.Normalize(version) != release.Version ||
                    manifest.ContractVersion != 2 || (options.RuntimeComposition && manifest.RuntimeContractVersion != 1) || manifest.Capabilities is null || string.IsNullOrWhiteSpace(manifest.EntryType) ||
                    string.IsNullOrWhiteSpace(manifest.Assembly) || Path.GetFileName(manifest.Assembly) != manifest.Assembly)
                    throw new InvalidDataException("Incompatible bundle manifest.");
                BundleCompatibility.ValidateContracts(payload);
                if (!options.RuntimeComposition)
                    foreach (var shared in options.SharedCapabilityAssemblies) BundleCompatibility.ValidateAssembly(payload, shared);
                string assembly = Path.Combine(payload, manifest.Assembly);
                BundleCompatibility.ValidateRuntimeDependencies(assembly);
                if (BundleVersion.Normalize(AssemblyName.GetAssemblyName(assembly).Version!) != release.Version ||
                    !File.Exists(Path.ChangeExtension(assembly, ".deps.json"))) throw new InvalidDataException("Incomplete bundle output.");
                token.ThrowIfCancellationRequested();
                try { Directory.Move(payload, destination); }
                catch (IOException) when (Directory.Exists(destination)) { /* Another updater published this immutable version first. */ }
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { Diagnose("bundle-candidate-rejected", error); }
            finally {
                // Only the generated staging directory beneath this explicit cache can be removed.
                if (Path.GetDirectoryName(Path.GetFullPath(stage)) == cache && Path.GetFileName(stage).StartsWith(".staging-", StringComparison.Ordinal)) {
                    try { Directory.Delete(stage, recursive: true); }
                    catch (Exception error) { Diagnose("bundle-staging-cleanup-failed", error); }
                }
            }
        }
    }
    private static async Task<JsonDocument> ReadJsonAsync(HttpClient http, Uri uri, CancellationToken token) {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        await CopyBoundedAsync(input, buffer, 1024 * 1024, token);
        return JsonDocument.Parse(buffer.ToArray());
    }
    private static async Task ExtractAsync(string package, string root, string id, Version version, CancellationToken token) {
        using var archive = ZipFile.OpenRead(package);
        if (archive.Entries.Count > 4096) throw new InvalidDataException("Too many bundle entries.");
        var specification = archive.Entries.Single(x => !x.FullName.Contains('/') && x.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using (var input = specification.Open()) {
            using var buffer = new MemoryStream();
            await CopyBoundedAsync(input, buffer, 1024 * 1024, token);
            buffer.Position = 0;
            var xml = XDocument.Load(buffer);
            string Value(string name) => xml.Descendants().First(x => x.Name.LocalName == name).Value;
            if (!string.Equals(Value("id"), id, StringComparison.OrdinalIgnoreCase) ||
                !Version.TryParse(Value("version"), out var packageVersion) || BundleVersion.Normalize(packageVersion) != version)
                throw new InvalidDataException("Package identity mismatch.");
        }
        long expanded = 0;
        foreach (var entry in archive.Entries.Where(x => x.FullName.StartsWith("bundle/", StringComparison.Ordinal))) {
            string relative = entry.FullName[7..];
            if (relative.Length == 0) continue;
            if (relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(x => x is ".." or ".")) throw new InvalidDataException("Invalid archive path.");
            string destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Archive path escapes staging.");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew);
            expanded += await CopyBoundedAsync(input, output, ExpandedLimit - expanded, token);
        }
    }
    private static async Task<long> CopyBoundedAsync(Stream input, Stream output, long limit, CancellationToken token) {
        var buffer = new byte[81920]; long total = 0; int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0) {
            total += read;
            if (total > limit) throw new InvalidDataException("Bundle content exceeds the size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return total;
    }
    private static bool ValidSource(Uri uri) => uri.IsAbsoluteUri && uri.UserInfo.Length == 0 &&
        (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback);
    private void Diagnose(string code, Exception error) {
        try { logger.LogWarning("{Code} ({ExceptionType}).", code, error.GetType().Name); }
        catch (Exception) { /* Diagnostics do not interrupt serving existing bundles. */ }
    }
}
