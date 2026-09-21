using System.Security.Cryptography;
using Clio10.PrimitiveContracts;

namespace Clio10.Primitives;

/// <summary>Hashes local files asynchronously with bounded memory.</summary>
public sealed class FileHashPrimitive : IFileHashPrimitive {
    /// <inheritdoc />
    public async Task<FileHashResult> HashAsync(string path, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        const int bufferSize = 64 * 1024;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[bufferSize];
        long bytes = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0) {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
            bytes += read;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new FileHashResult(Convert.ToHexStringLower(hash.GetHashAndReset()), bytes);
    }
}
