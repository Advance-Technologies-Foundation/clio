using Clio10.PrimitiveContracts;
using System.Text;
using Clio10.Contracts;

namespace Clio10.Primitives;

/// <summary>Writes without truncating a working destination when staging or cancellation fails.</summary>
public sealed class FileWriterPrimitive : IFileWriterPrimitive {
    /// <inheritdoc />
    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        string destination = Path.GetFullPath(path);
        string parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("A file destination, not a volume root, is required.", nameof(path));
        string temporary = Path.Combine(parent, ".clio-write-" + Guid.NewGuid().ToString("N"));
        bool owned = false;
        try {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                BufferSize = 81920, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options)) {
                owned = true;
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destination)) {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, File.GetUnixFileMode(destination));
                File.Replace(temporary, destination, null);
            }
            else File.Move(temporary, destination);
        }
        finally {
            try { if (owned) File.Delete(temporary); }
            catch (IOException) { /* Cleanup cannot erase a published outcome. */ }
            catch (UnauthorizedAccessException) { /* A failed cleanup leaves only the staged file. */ }
        }
    }
}
