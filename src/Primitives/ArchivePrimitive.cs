using Clio10.PrimitiveContracts;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Clio10.Contracts;

namespace Clio10.Primitives;

/// <summary>Streams Creatio's length-prefixed gzip format and publishes complete artifacts from private staging paths.</summary>
public sealed partial class ArchivePrimitive : IArchivePrimitive {
    private static readonly Encoding Unicode = new UnicodeEncoding(false, false, true);
    /// <inheritdoc />
    public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string root, CancellationToken cancellationToken) {
        root = ResolveParentLocation(root);
        RejectLinks(root);
        var entries = new List<ArchiveEntry>();
        foreach (string path in Directory.EnumerateFileSystemEntries(root)) {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(path);
            entries.Add(new(Path.GetFileName(path), (attributes & FileAttributes.Directory) != 0, (attributes & FileAttributes.ReparsePoint) != 0));
        }
        return Task.FromResult<IReadOnlyList<ArchiveEntry>>(entries.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray());
    }
    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListFilesAsync(string root, CancellationToken cancellationToken) {
        root = ResolveParentLocation(root);
        RejectLinks(root);
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory)) {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string path in Directory.EnumerateFileSystemEntries(directory)) {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Archive sources cannot contain links.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                else result.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
            }
        }
        result.Sort(StringComparer.Ordinal);
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    /// <inheritdoc />
    public async Task<ArchiveResult> PackAsync(string root, IReadOnlyList<string> files, string destination, ArchiveLimits limits, CancellationToken cancellationToken) {
        Validate(limits);
        if (files.Count > limits.MaxEntries) throw new InvalidDataException("Archive entry limit exceeded.");
        root = ResolveParentLocation(root);
        destination = ResolveParentLocation(destination);
        RejectLinks(root);
        RejectLinks(destination);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = files.Select(name => Name(name, limits)).ToArray();
        foreach (string name in selected) Register(names, directories, name);
        string parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("An archive file path is required.");
        string staged = Path.Combine(parent, ".clio-archive-" + Guid.NewGuid().ToString("N"));
        bool owned = false;
        long total = 0;
        try {
            await using (var output = Open(staged, FileMode.CreateNew, FileAccess.Write)) {
                owned = true;
                await using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) {
                    foreach (string name in selected) {
                        cancellationToken.ThrowIfCancellationRequested();
                        string source = Within(root, name);
                        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) throw new IOException("An archive cannot include its own destination.");
                        RejectLinks(source, root);
                        await using var input = Open(source, FileMode.Open, FileAccess.Read);
                        long length = input.Length;
                        if (length > int.MaxValue || length > limits.MaxExpandedBytes - total) throw new InvalidDataException("Archive size limit exceeded.");
                        byte[] encoded = Unicode.GetBytes(name);
                        await WriteInt(gzip, encoded.Length / 2, cancellationToken);
                        await gzip.WriteAsync(encoded, cancellationToken);
                        await WriteInt(gzip, (int)length, cancellationToken);
                        await CopyExactly(input, gzip, length, cancellationToken);
                        if (input.Length != length) throw new IOException("Archive source changed while reading.");
                        total += length;
                    }
                }
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(destination);
            File.Move(staged, destination, overwrite: true);
            return new(destination, selected.Length, total);
        }
        finally { if (owned) TryDeleteFile(staged); }
    }

    /// <inheritdoc />
    public async Task<ArchiveResult> ExtractAsync(string archive, string destination, ArchiveLimits limits, CancellationToken cancellationToken, bool overwrite = false) {
        Validate(limits);
        archive = ResolveParentLocation(archive);
        destination = Path.TrimEndingDirectorySeparator(ResolveParentLocation(destination));
        RejectLinks(archive);
        RejectLinks(destination);
        if (Path.Exists(destination) && (!overwrite || !Directory.Exists(destination))) throw new IOException("Extraction destination already exists.");
        string parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("A destination directory is required.");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Destination parent must exist.");
        string staged = Path.Combine(parent, ".clio-extract-" + Guid.NewGuid().ToString("N"));
        if (Path.Exists(staged)) throw new IOException("Staging directory already exists.");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(staged);
        else Directory.CreateDirectory(staged, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        long expanded = 0;
        try {
            await using (var input = Open(archive, FileMode.Open, FileAccess.Read)) {
                if (input.Length < 20) throw new InvalidDataException("Incomplete gzip container.");
                input.Seek(-4, SeekOrigin.End);
                var footer = new byte[4];
                await input.ReadExactlyAsync(footer, cancellationToken);
                uint expectedSize = BinaryPrimitives.ReadUInt32LittleEndian(footer);
                input.Position = 0;
                await using var gzip = new GZipStream(input, CompressionMode.Decompress);
                while (await ReadInt(gzip, allowEnd: true, cancellationToken) is int characters) {
                    if (characters <= 0 || characters > limits.MaxPathCharacters || names.Count >= limits.MaxEntries)
                        throw new InvalidDataException("Invalid archive name or entry count.");
                    var bytes = new byte[checked(characters * 2)];
                    await gzip.ReadExactlyAsync(bytes, cancellationToken);
                    string name;
                    try { name = Name(Unicode.GetString(bytes), limits); }
                    catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid archive path encoding.", error); }
                    Register(names, directories, name);
                    int length = (await ReadInt(gzip, false, cancellationToken))!.Value;
                    if (length < 0 || length > limits.MaxExpandedBytes - total) throw new InvalidDataException("Archive size limit exceeded.");
                    string path = Within(staged, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await using var output = Open(path, FileMode.CreateNew, FileAccess.Write);
                    await CopyExactly(gzip, output, length, cancellationToken);
                    total += length;
                    expanded += 8L + bytes.Length + length;
                }
                // GZipStream may accept a truncated trailer; compare the final ISIZE to reject missing footer bytes.
                if (unchecked((uint)expanded) != expectedSize) throw new InvalidDataException("Gzip size trailer does not match its records.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(destination);
            string? retainedBackup = PublishDirectory(staged, destination, overwrite);
            return new(destination, names.Count, total, retainedBackup);
        }
        finally {
            // Only the random staging child created above is owned by this invocation.
            if (Directory.Exists(staged) && Path.GetDirectoryName(Path.GetFullPath(staged)) == parent)
                TryDeleteDirectory(staged);
        }
    }

    private static string? PublishDirectory(string staged, string destination, bool overwrite) {
        // Explicit overwrite replaces the validated tree; it never overlays untrusted entries onto an existing tree.
        if (!Directory.Exists(destination)) { Directory.Move(staged, destination); return null; }
        if (!overwrite) throw new IOException("Extraction destination already exists.");
        RejectLinks(destination);
        string backup = Path.Combine(Path.GetDirectoryName(destination)!, ".clio-backup-" + Guid.NewGuid().ToString("N"));
        Directory.Move(destination, backup);
        try { Directory.Move(staged, destination); }
        catch (Exception publicationError) when (publicationError is IOException or UnauthorizedAccessException) {
            try { Directory.Move(backup, destination); }
            catch (Exception restorationError) when (restorationError is IOException or UnauthorizedAccessException) {
                throw new ArchiveRestoreException(destination, backup, new AggregateException(publicationError, restorationError));
            }
            throw;
        }
        // Publication is known to have succeeded; cleanup must not turn that into a reported operation failure.
        return TryDeleteDirectory(backup) ? null : backup;
    }

    private static string Name(string name, ArchiveLimits limits) {
        name = name.Replace('\\', '/');
        if (name.Length == 0 || name.Length > limits.MaxPathCharacters || name.StartsWith('/') || name.Any(c => c < 32 || ":*?\"<>|".Contains(c)))
            throw new InvalidDataException("Invalid archive path.");
        foreach (string part in name.Split('/')) {
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')) throw new InvalidDataException("Invalid archive path component.");
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3])))
                throw new InvalidDataException("Reserved archive path component.");
        }
        return name;
    }
    private static void Register(HashSet<string> names, HashSet<string> directories, string name) {
        if (directories.Contains(name) || !names.Add(name)) throw new InvalidDataException("Duplicate or conflicting archive path.");
        // Detect both file-then-child and child-then-file collisions independent of input ordering.
        for (int separator = name.IndexOf('/'); separator >= 0; separator = name.IndexOf('/', separator + 1)) {
            string parent = name[..separator];
            if (names.Contains(parent)) throw new InvalidDataException("Archive path conflicts with a file.");
            directories.Add(parent);
        }
    }
    private static string Within(string root, string name) {
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Archive path escaped its root.");
        return path;
    }
    private static void RejectLinks(string path, string? contentRoot = null) {
        // Caller-selected parent locations may be OS aliases (/var on macOS) or developer junctions.
        // Reject the target itself and, for selected package files, every component inside the content root.
        string? boundary = contentRoot is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRoot));
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current)) {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Archive paths cannot traverse links."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            if (boundary is null || string.Equals(Path.TrimEndingDirectorySeparator(current), boundary, StringComparison.OrdinalIgnoreCase)) break;
        }
    }
    private static string ResolveParentLocation(string path) {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string? parent = Path.GetDirectoryName(full);
        if (parent is null) return full;
        // Resolve caller-controlled parent aliases once, before staging. Package-relative links remain forbidden.
        string resolvedParent = ResolveParentLocation(parent);
        var info = new DirectoryInfo(resolvedParent);
        if (info.LinkTarget is not null)
            resolvedParent = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? throw new IOException("Cannot resolve archive parent location.");
        return Path.Combine(resolvedParent, Path.GetFileName(full));
    }
    private static void Validate(ArchiveLimits limits) {
        if (limits.MaxEntries <= 0 || limits.MaxExpandedBytes < 0 || limits.MaxPathCharacters is <= 0 or > 32767)
            throw new ArgumentOutOfRangeException(nameof(limits));
    }
    private static FileStream Open(string path, FileMode mode, FileAccess access) => new(path, new FileStreamOptions {
        Mode = mode, Access = access, Share = access == FileAccess.Read ? FileShare.Read : FileShare.None,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan, BufferSize = 65536
    });
    private static async Task WriteInt(Stream output, int value, CancellationToken token) {
        var bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value); await output.WriteAsync(bytes, token);
    }
    private static async Task<int?> ReadInt(Stream input, bool allowEnd, CancellationToken token) {
        var bytes = new byte[4];
        int first = await input.ReadAsync(bytes.AsMemory(0, 1), token);
        if (first == 0 && allowEnd) return null;
        if (first == 0) throw new EndOfStreamException("Missing archive length.");
        await input.ReadExactlyAsync(bytes.AsMemory(1), token);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }
    private static async Task CopyExactly(Stream input, Stream output, long remaining, CancellationToken token) {
        var buffer = new byte[65536];
        while (remaining > 0) {
            int count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), token);
            if (count == 0) throw new EndOfStreamException("Truncated archive content.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            remaining -= count;
        }
    }
    private static void TryDeleteFile(string path) {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static bool TryDeleteDirectory(string path) {
        try { Directory.Delete(path, recursive: true); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
