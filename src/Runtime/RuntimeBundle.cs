using Clio10.Composition;
using Clio10.Contracts;
using Clio10.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace Clio10.Runtime;

/// <summary>A release entry point assembling workflows and primitives without constructing a new host.</summary>
public class RuntimeBundle : IRuntimeBundle {
    /// <inheritdoc />
    public Version Version => GetType().Assembly.GetName().Version!;
    /// <inheritdoc />
    public int ContractVersion => 2;
    /// <inheritdoc />
    public int RuntimeContractVersion => 1;
    /// <inheritdoc />
    public IReadOnlyCollection<string> Capabilities => ["http", "filesystem", "file-writer", "archive", "file-hash", "directory-snapshot"];

    /// <inheritdoc />
    public IPrimitiveSession OpenSession(PrimitiveSessionOptions options) {
        var services = new ServiceCollection();
        services.AddSingleton<IPrimitiveBundle, HttpPrimitiveBundle>();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IPrimitiveBundle>().OpenSession(options);
    }

    /// <inheritdoc />
    public IRuntimeComposition OpenComposition(ICompositionHost host) {
        var services = new ServiceCollection();
        // The host is borrowed: register the instance so the private provider never owns its disposal.
        services.AddSingleton(host);
        services.AddClioWorkflows();
        ConfigureWorkflows(services);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        try {
            var composition = provider.GetRequiredService<IClioComposition>();
            _ = composition.Operations; // Fail invalid registration before publishing a callable composition.
            return new CompositionLifetime(composition, provider);
        }
        catch { provider.Dispose(); throw; }
    }

    /// <summary>Adds release-owned partner or vendor workflows. It must not register another Core.</summary>
    protected virtual void ConfigureWorkflows(IServiceCollection services) { }

    private sealed class CompositionLifetime(IClioComposition composition, ServiceProvider provider) : IRuntimeComposition {
        public IReadOnlyCollection<OperationDescriptor> Operations => composition.Operations;
        public Task<OperationResult> ExecuteAsync(CompositionRequest request, IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default) => composition.ExecuteAsync(request, progress, cancellationToken);
        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
