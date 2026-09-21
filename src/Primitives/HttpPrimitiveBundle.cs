using Clio10.PrimitiveContracts;
using Clio10.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Clio10.Primitives;

/// <summary>Public entry point of a independently deployable primitives bundle.</summary>
public sealed class HttpPrimitiveBundle : IPrimitiveBundle {
    /// <inheritdoc />
    public Version Version => typeof(HttpPrimitiveBundle).Assembly.GetName().Version!;
    /// <inheritdoc />
    public int ContractVersion => 2;
    /// <inheritdoc />
    public IReadOnlyCollection<string> Capabilities => ["http", "filesystem", "file-writer", "archive", "file-hash", "directory-snapshot"];
    /// <inheritdoc />
    public IPrimitiveSession OpenSession(PrimitiveSessionOptions options) {
        var services = new ServiceCollection();
        services.AddClioPrimitives(options);
        services.AddScoped<IFileSystemPrimitive, FileSystemPrimitive>();
        services.AddScoped<IFileWriterPrimitive, FileWriterPrimitive>();
        services.AddScoped<IArchivePrimitive, ArchivePrimitive>();
        services.AddScoped<IFileHashPrimitive, FileHashPrimitive>();
        services.AddScoped<IDirectorySnapshotPrimitive, DirectorySnapshotPrimitive>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        try { return ActivatorUtilities.CreateInstance<OwnedSession>(provider, provider, provider.CreateScope()); }
        catch { provider.Dispose(); throw; }
    }

    private sealed class OwnedSession(ServiceProvider owner, IServiceScope scope) : IPrimitiveSession {
        public object? GetCapability(string name) => name switch {
            "http" => scope.ServiceProvider.GetRequiredService<IClioPrimitive>(),
            "filesystem" => scope.ServiceProvider.GetRequiredService<IFileSystemPrimitive>(),
            "file-writer" => scope.ServiceProvider.GetRequiredService<IFileWriterPrimitive>(),
            "archive" => scope.ServiceProvider.GetRequiredService<IArchivePrimitive>(),
            "file-hash" => scope.ServiceProvider.GetRequiredService<IFileHashPrimitive>(),
            "directory-snapshot" => scope.ServiceProvider.GetRequiredService<IDirectorySnapshotPrimitive>(),
            _ => null
        };
        public async ValueTask DisposeAsync() {
            try { if (scope is IAsyncDisposable asyncScope) await asyncScope.DisposeAsync(); else scope.Dispose(); }
            finally { await owner.DisposeAsync(); }
        }
        public void Dispose() { try { scope.Dispose(); } finally { owner.Dispose(); } }
    }
}
