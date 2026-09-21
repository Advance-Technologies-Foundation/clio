using Clio10.PrimitiveContracts;
using Clio10.Contracts;
namespace Clio10.Primitives;

/// <summary>Local filesystem implementation. Trusted hosts control which paths workflows receive.</summary>
public sealed class FileSystemPrimitive : IFileSystemPrimitive {
    /// <inheritdoc />
    public Task<string> ReadTextAsync(string path, CancellationToken cancellationToken) => File.ReadAllTextAsync(path, cancellationToken);
}
