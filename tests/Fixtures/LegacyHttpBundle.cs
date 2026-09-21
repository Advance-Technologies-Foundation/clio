using Clio10.PrimitiveContracts;
using Clio10.Contracts;
using Microsoft.Extensions.DependencyInjection;
namespace Clio10.Primitives;

// An independently built HTTP-only release fixture, sharing the version-2 factory ABI.
public sealed class HttpPrimitiveBundle : IPrimitiveBundle {
    public Version Version => typeof(HttpPrimitiveBundle).Assembly.GetName().Version!;
    public int ContractVersion => 2;
    public IReadOnlyCollection<string> Capabilities => ["http"];
    public IPrimitiveSession OpenSession(PrimitiveSessionOptions options) {
        var services = new ServiceCollection();
        services.AddClioPrimitives(options);
        return new Session(services.BuildServiceProvider());
    }
    private sealed class Session(ServiceProvider provider) : IPrimitiveSession {
        private readonly IServiceScope _scope = provider.CreateScope();
        public object? GetCapability(string name) => name == "http" ? _scope.ServiceProvider.GetRequiredService<IClioPrimitive>() : null;
        public void Dispose() { try { _scope.Dispose(); } finally { provider.Dispose(); } }
        public async ValueTask DisposeAsync() {
            try { await ((IAsyncDisposable)_scope).DisposeAsync(); } finally { await provider.DisposeAsync(); }
        }
    }
}
