using Clio10.Contracts;

namespace Clio10.PrimitiveContracts;

/// <summary>Reads file paths and SHA-256 digests from a stable local directory tree without following links.</summary>
[Capability("directory-snapshot")]
public interface IDirectorySnapshotPrimitive {
    /// <summary>Reads regular files recursively; rejects reparse points in the root, ancestors and entries.</summary>
    /// <remarks>Supports stable ordinary-file/directory trees only. Unix special entries are not detected or safely rejected and may block before cancellation is observed.</remarks>
    /// <param name="path">Directory to inspect.</param>
    /// <param name="cancellationToken">Cancels enumeration and hashing.</param>
    /// <returns>Ordinal relative paths using forward directory separators and lowercase SHA-256 digests.</returns>
    Task<IReadOnlyList<DirectoryFileSnapshot>> SnapshotAsync(string path, CancellationToken cancellationToken);
}

/// <summary>A file's relative path and content digest; metadata and empty directories are excluded.</summary>
/// <param name="RelativePath">Case-sensitive relative file path.</param>
/// <param name="Sha256">SHA-256 of the file bytes.</param>
public sealed record DirectoryFileSnapshot(string RelativePath, string Sha256);

/// <summary>Indicates a link or reparse point that cannot be inspected without following it.</summary>
public sealed class DirectoryLinkException : IOException {
    /// <summary>Creates a failure that does not expose filesystem paths.</summary>
    public DirectoryLinkException() : base("Directory comparison does not support symbolic links or reparse points.") { }
}
