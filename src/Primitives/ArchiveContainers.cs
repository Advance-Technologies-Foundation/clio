using Clio10.PrimitiveContracts;
using System.IO.Compression;
using Clio10.Contracts;

namespace Clio10.Primitives;

public sealed partial class ArchivePrimitive {
    /// <inheritdoc />
    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Directory.Exists(path));
    }

    /// <inheritdoc />
    public async Task<ArchiveResult> PackContainerAsync(IReadOnlyList<ArchiveSource> sources, string destination, ArchiveContainerLimits limits, CancellationToken cancellationToken) {
        ValidateContainer(limits, sources.Count);
        string[] names = UniqueNames(sources.Select(x => x.Name), limits.Content);
        destination = ResolveParentLocation(destination);
        RejectLinks(destination);
        string staging = CreateStaging(Path.GetDirectoryName(destination)!);
        string container = Path.Combine(staging, "container.zip");
        long totalBytes = 0, containerBytes = 0;
        int totalFiles = 0;
        try {
            await using (var output = Open(container, FileMode.CreateNew, FileAccess.Write)) {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true)) {
                    for (int index = 0; index < sources.Count; index++) {
                        var source = sources[index];
                        if (source.Files.Any(file => string.Equals(Within(ResolveParentLocation(source.Root), Name(file, limits.Content)), destination, StringComparison.OrdinalIgnoreCase)))
                            throw new IOException("An archive cannot include its own destination.");
                        string gzipPath = Path.Combine(staging, "source.gz");
                        var result = await PackAsync(source.Root, source.Files, gzipPath, Remaining(limits.Content, totalFiles, totalBytes), cancellationToken);
                        totalFiles += result.Files; totalBytes += result.Bytes;
                        CheckContent(limits.Content, totalFiles, totalBytes);
                        await using (var input = Open(gzipPath, FileMode.Open, FileAccess.Read)) {
                            if (input.Length > limits.MaxContainerBytes - containerBytes) throw new InvalidDataException("Container payload limit exceeded.");
                            containerBytes += input.Length;
                            await using var entry = zip.CreateEntry(names[index] + ".gz", CompressionLevel.NoCompression).Open();
                            await CopyExactly(input, entry, input.Length, cancellationToken);
                        }
                        File.Delete(gzipPath);
                    }
                }
                if (output.Length > limits.MaxContainerBytes) throw new InvalidDataException("Container input limit exceeded.");
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(destination);
            File.Move(container, destination, overwrite: true);
            return new(destination, totalFiles, totalBytes);
        }
        finally { TryDeleteDirectory(staging); }
    }

    /// <inheritdoc />
    public async Task<ArchiveBatchResult> ExtractContainerAsync(string archive, string destination, ArchiveContainerLimits limits, CancellationToken cancellationToken, bool overwrite = false) {
        ValidateContainer(limits, 1);
        archive = ResolveParentLocation(archive);
        RejectLinks(archive);
        destination = DestinationParent(destination);
        string staging = CreateStaging(destination);
        try {
            var inputs = new List<ArchiveInput>();
            await using (var source = Open(archive, FileMode.Open, FileAccess.Read)) {
                // Bound the input as well as expanded ZIP entries. Central-directory parsing belongs to the platform ZIP reader.
                if (source.Length > limits.MaxContainerBytes) throw new InvalidDataException("Container input limit exceeded.");
                using var bounded = new MetadataBudgetStream(source, limits.MaxMetadataBytes);
                using var zip = new ZipArchive(bounded, ZipArchiveMode.Read, leaveOpen: true);
                var entries = zip.Entries;
                if (entries.Count > limits.MaxArchives) throw new InvalidDataException("Container entry limit exceeded.");
                bounded.AllowPayload();
                long payloadBytes = 0;
                var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries) {
                    cancellationToken.ThrowIfCancellationRequested();
                    string name = Name(entry.FullName.TrimEnd('/'), limits.Content);
                    // Legacy package containers select root .gz entries; other safe entries are not extracted.
                    if (entry.FullName.EndsWith('/') || name.Contains('/') || !name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) continue;
                    string packageName = LeafName(name[..^3], limits.Content);
                    if (!selectedNames.Add(packageName)) throw new InvalidDataException("Duplicate container package name.");
                    if (entry.Length > limits.MaxContainerBytes - payloadBytes) throw new InvalidDataException("Container payload limit exceeded.");
                    payloadBytes += entry.Length;
                    string path = Path.Combine(staging, packageName + ".gz");
                    await using (var input = entry.Open()) {
                        await using var output = Open(path, FileMode.CreateNew, FileAccess.Write);
                        await CopyExactly(input, output, entry.Length, cancellationToken);
                        var probe = new byte[1];
                        if (await input.ReadAsync(probe, cancellationToken) != 0) throw new InvalidDataException("Container entry exceeds its declared length.");
                    }
                    inputs.Add(new(packageName, path));
                }
            }
            return await ExtractManyAsync(inputs, destination, limits, cancellationToken, overwrite);
        }
        finally { TryDeleteDirectory(staging); }
    }

    /// <inheritdoc />
    public async Task<ArchiveBatchResult> ExtractManyAsync(IReadOnlyList<ArchiveInput> inputs, string destination, ArchiveContainerLimits limits, CancellationToken cancellationToken, bool overwrite = false) {
        ValidateContainer(limits, inputs.Count);
        string[] names = UniqueNames(inputs.Select(x => x.Name), limits.Content);
        destination = DestinationParent(destination);
        string[] targets = names.Select(x => Path.Combine(destination, x)).ToArray();
        foreach (string target in targets) {
            RejectLinks(target);
            if (Path.Exists(target) && (!overwrite || !Directory.Exists(target))) throw new IOException("Extraction destination already exists.");
        }
        string staging = CreateStaging(destination);
        var prepared = new List<ArchiveResult>();
        var completed = new List<ArchiveResult>();
        int files = 0;
        long bytes = 0, sourceBytes = 0;
        try {
            for (int index = 0; index < inputs.Count; index++) {
                RejectLinks(inputs[index].Path);
                sourceBytes = checked(sourceBytes + new FileInfo(inputs[index].Path).Length);
                if (sourceBytes > limits.MaxContainerBytes) throw new InvalidDataException("Container payload limit exceeded.");
                var result = await ExtractAsync(inputs[index].Path, Path.Combine(staging, names[index]), Remaining(limits.Content, files, bytes), cancellationToken);
                files += result.Files; bytes += result.Bytes;
                CheckContent(limits.Content, files, bytes);
                prepared.Add(result);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // No cancellation between these short synchronous publications: never discard a known success receipt.
            for (int index = 0; index < prepared.Count; index++) {
                try {
                    string? backup = PublishDirectory(prepared[index].Destination, targets[index], overwrite);
                    completed.Add(prepared[index] with { Destination = targets[index], RetainedBackup = backup });
                }
                catch (ArchiveRestoreException error) { return new(completed, error.Destination, error.BackupPath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(completed, targets[index]); }
            }
            return new(completed);
        }
        finally { TryDeleteDirectory(staging); }
    }

    private static ArchiveLimits Remaining(ArchiveLimits limits, int files, long bytes) => limits with {
        MaxEntries = Math.Max(1, limits.MaxEntries - files), MaxExpandedBytes = limits.MaxExpandedBytes - bytes
    };
    private static void CheckContent(ArchiveLimits limits, int files, long bytes) {
        if (files > limits.MaxEntries || bytes > limits.MaxExpandedBytes) throw new InvalidDataException("Batch content limit exceeded.");
    }
    private static void ValidateContainer(ArchiveContainerLimits limits, int count) {
        Validate(limits.Content);
        if (limits.MaxArchives <= 0 || limits.MaxContainerBytes < 0 || limits.MaxMetadataBytes <= 0) throw new ArgumentOutOfRangeException(nameof(limits));
        if (count == 0 || count > limits.MaxArchives) throw new InvalidDataException("Invalid archive batch count.");
    }
    private static string LeafName(string name, ArchiveLimits limits) {
        name = Name(name, limits);
        if (name.Contains('/')) throw new InvalidDataException("A package name must be one path component.");
        return name;
    }
    private static string[] UniqueNames(IEnumerable<string> names, ArchiveLimits limits) {
        var result = names.Select(x => LeafName(x, limits)).ToArray();
        if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length) throw new InvalidDataException("Duplicate archive batch name.");
        return result;
    }
    private static string DestinationParent(string path) {
        string parent = Path.GetDirectoryName(ResolveParentLocation(Path.Combine(path, ".clio-parent-probe")))!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Destination parent must exist.");
        return parent;
    }
    private static string CreateStaging(string parent) {
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Destination parent must exist.");
        string path = Path.Combine(parent, ".clio-batch-" + Guid.NewGuid().ToString("N"));
        if (Path.Exists(path)) throw new IOException("Staging directory already exists.");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    // ZipArchive materializes its central directory before exposing Entries.Count. Bound those reads first,
    // so a hostile directory cannot allocate an unbounded entry list before the count limit is checked.
    private sealed class MetadataBudgetStream(Stream inner, long budget) : Stream {
        private long _remaining = budget;
        public void AllowPayload() => _remaining = long.MaxValue;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Track(inner.Read(buffer));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Track(await inner.ReadAsync(buffer, cancellationToken));
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Track(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));
        private int Track(int count) {
            if (count > _remaining) throw new InvalidDataException("Container metadata limit exceeded.");
            _remaining -= count;
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
