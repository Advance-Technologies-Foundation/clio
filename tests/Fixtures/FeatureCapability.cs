using Clio10.Contracts;
namespace Clio10.FeatureFixture;

/// <summary>A real external capability shipped only by later test runtime releases.</summary>
[Capability("release-file")]
public interface IReleaseFile {
#if RELEASE_V3
    /// <summary>Replaces the earlier method and its argument/result shape.</summary>
    Task<FileReceipt> PublishAsync(FileInput input, CancellationToken cancellationToken);
#else
    /// <summary>Writes and reads a file asynchronously.</summary>
    Task<string> WriteReadAsync(string path, string text, CancellationToken cancellationToken);
#endif
}
#if RELEASE_V3
/// <summary>Release-owned input.</summary>
public sealed record FileInput(string Path, string Text);
/// <summary>Release-owned result.</summary>
public sealed record FileReceipt(string Text);
#endif
/// <summary>Exercises actual filesystem I/O through a release-private interface.</summary>
public sealed class ReleaseFile : IReleaseFile {
#if RELEASE_V3
    /// <inheritdoc />
    public async Task<FileReceipt> PublishAsync(FileInput input, CancellationToken cancellationToken) {
        await File.WriteAllTextAsync(input.Path, input.Text, cancellationToken);
        return new(await File.ReadAllTextAsync(input.Path, cancellationToken));
    }
#else
    /// <inheritdoc />
    public async Task<string> WriteReadAsync(string path, string text, CancellationToken cancellationToken) {
        await File.WriteAllTextAsync(path, text, cancellationToken);
        return await File.ReadAllTextAsync(path, cancellationToken);
    }
#endif
}
