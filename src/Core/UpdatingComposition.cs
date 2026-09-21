using Clio10.Contracts;
using Microsoft.Extensions.Logging;

namespace Clio10.Core;

/// <summary>Routes discovery and root calls to a complete runtime while keeping one Core and transport alive.</summary>
/// <remarks>Loaded compositions remain owned until shutdown. A call captures its composition before awaiting anything.</remarks>
public sealed class UpdatingComposition(IPrimitiveCatalog catalog, IClioCore core, CoreOptions options,
    ILogger<UpdatingComposition> logger) : IRuntimeComposition {
    private readonly object _sync = new();
    private readonly Dictionary<IPrimitiveBundle, IRuntimeComposition> _loaded = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IPrimitiveBundle> _rejected = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    /// <inheritdoc />
    public IReadOnlyCollection<OperationDescriptor> Operations => Select().Operations;

    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(CompositionRequest request, IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        IRuntimeComposition runtime;
        try { runtime = Select(); }
        catch (CoreResolutionException error) { return new(false, error.Code); }
        // Never retry after execution starts: the old call may already have external effects.
        return await runtime.ExecuteAsync(request, progress, cancellationToken);
    }

    private IRuntimeComposition Select() {
        lock (_sync) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var requirement = new PrimitiveRequirement(new(10, 0), new(11, 0), Exact: options.ExactVersion);
            while (true) {
                var bundle = catalog.Select(requirement);
                if (_loaded.TryGetValue(bundle, out var existing)) return existing;
                if (!_rejected.Contains(bundle)) {
                    try {
                        if (bundle is not IRuntimeBundle runtime || runtime.RuntimeContractVersion != 1)
                            throw new InvalidOperationException("A complete runtime is required.");
                        var composition = runtime.OpenComposition(core.Bind(bundle));
                        _loaded.Add(bundle, composition);
                        return composition;
                    }
                    catch (Exception error) {
                        _rejected.Add(bundle);
                        try { logger.LogWarning("Runtime activation failed ({ExceptionType}).", error.GetType().FullName); }
                        catch (Exception) { /* Diagnostics cannot disable the existing release. */ }
                    }
                }
                // Activation has not executed a workflow. An older complete release is a safe fallback.
                requirement = requirement with { MaximumExclusive = BundleVersion.Normalize(bundle.Version) };
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        IRuntimeComposition[] loaded;
        lock (_sync) { if (_disposed) return; _disposed = true; loaded = _loaded.Values.ToArray(); _loaded.Clear(); }
        foreach (var composition in loaded) {
            try { await composition.DisposeAsync(); }
            catch (Exception error) {
                try { logger.LogWarning("Runtime cleanup failed ({ExceptionType}).", error.GetType().FullName); }
                catch (Exception) { /* Continue disposing the other releases. */ }
            }
        }
    }
}
