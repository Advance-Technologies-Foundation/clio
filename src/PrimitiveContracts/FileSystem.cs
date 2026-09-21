using Clio10.Contracts;
namespace Clio10.PrimitiveContracts;

/// <summary>Vendor capability for filesystem I/O using explicit paths.</summary>
[Capability("filesystem")]
public interface IFileSystemPrimitive {
    /// <summary>Reads UTF-8 text asynchronously.</summary>
    Task<string> ReadTextAsync(string path, CancellationToken cancellationToken);
}
