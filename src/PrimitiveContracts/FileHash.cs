using Clio10.Contracts;

namespace Clio10.PrimitiveContracts;

/// <summary>Computes a SHA-256 digest from a local file.</summary>
[Capability("file-hash")]
public interface IFileHashPrimitive {
    /// <summary>Streams the file and returns its digest and the number of bytes read.</summary>
    /// <param name="path">The file path, used without normalization or trimming.</param>
    /// <param name="cancellationToken">Cancels opening or reading the file.</param>
    /// <returns>The lowercase SHA-256 digest and actual byte count.</returns>
    /// <remarks>Filesystem exceptions propagate to the caller. The primitive owns and disposes the stream.</remarks>
    Task<FileHashResult> HashAsync(string path, CancellationToken cancellationToken);
}

/// <summary>The digest and byte count observed while streaming a file.</summary>
/// <param name="Sha256">The SHA-256 digest encoded as lowercase hexadecimal characters.</param>
/// <param name="Bytes">The actual number of bytes read.</param>
public sealed record FileHashResult(string Sha256, long Bytes);
