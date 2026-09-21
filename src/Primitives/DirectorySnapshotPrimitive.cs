using Clio10.PrimitiveContracts;

namespace Clio10.Primitives;

/// <summary>Enumerates stable directory trees without following reparse points and streams file hashes.</summary>
public sealed class DirectorySnapshotPrimitive(IFileHashPrimitive fileHash) : IDirectorySnapshotPrimitive {
    /// <inheritdoc />
    public async Task<IReadOnlyList<DirectoryFileSnapshot>> SnapshotAsync(string path, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(path);
        var ancestors = new Stack<string>();
        for (var current = root; current is not null; current = Path.GetDirectoryName(current)) {
            cancellationToken.ThrowIfCancellationRequested();
            ancestors.Push(current);
        }
        while (ancestors.TryPop(out var ancestor)) {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try {
                attributes = File.GetAttributes(ancestor);
            } catch (FileNotFoundException) {
                throw new DirectoryNotFoundException("The snapshot directory does not exist.");
            }
            RejectLink(attributes);
            if ((attributes & FileAttributes.Directory) == 0) {
                throw new DirectoryNotFoundException("The snapshot root must be a directory.");
            }
        }
        var pending = new Stack<string>();
        pending.Push(root);
        var files = new List<DirectoryFileSnapshot>();
        while (pending.TryPop(out var directory)) {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(File.GetAttributes(directory));
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory)) {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                RejectLink(attributes);
                if ((attributes & FileAttributes.Directory) != 0) {
                    pending.Push(entry);
                    continue;
                }
                var hash = await fileHash.HashAsync(entry, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, entry);
                if (Path.DirectorySeparatorChar != '/') {
                    relative = relative.Replace(Path.DirectorySeparatorChar, '/');
                }
                files.Add(new DirectoryFileSnapshot(relative, hash.Sha256));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        files.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        cancellationToken.ThrowIfCancellationRequested();
        return files;
    }

    private static void RejectLink(FileAttributes attributes) {
        if ((attributes & FileAttributes.ReparsePoint) != 0) {
            throw new DirectoryLinkException();
        }
    }
}
