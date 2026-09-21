using Clio10.Contracts;
namespace Clio10.PrimitiveContracts;

/// <summary>Explicit file publication capability, separate from read-only filesystem access.</summary>
[Capability("file-writer")]
public interface IFileWriterPrimitive {
    /// <summary>Publishes complete UTF-8 content by staging beside the destination and replacing it after the write succeeds.</summary>
    /// <remarks>Requires an existing parent directory. Cancellation before publication leaves the destination unchanged.
    /// Does not coordinate other processes or promise durability across machine failure.</remarks>
    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken);
}
