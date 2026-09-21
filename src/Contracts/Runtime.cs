namespace Clio10.Contracts;

/// <summary>The stable host services available to a dynamically loaded composition.</summary>
public interface ICompositionHost {
    /// <summary>Runs one root against the release pinned by the host, sharing environment and coordination policy.</summary>
    Task<T> RunAsync<T>(string environmentName, PrimitiveRequirement requirement,
        Func<IOperationContext, CancellationToken, Task<T>> execute, CancellationToken cancellationToken);
}

/// <summary>A borrowed operation context whose lifetime belongs to the host.</summary>
public interface IOperationContext : ICapabilityProvider {
    /// <summary>The resolved environment snapshot.</summary>
    ClioEnvironment Environment { get; }
    /// <summary>The pinned primitive release.</summary>
    Version PrimitiveVersion { get; }
    /// <summary>The capabilities provided by that release.</summary>
    IReadOnlyCollection<string> Capabilities { get; }
}

/// <summary>A complete release containing both workflows and their primitive implementation.</summary>
public interface IRuntimeBundle : IPrimitiveBundle {
    /// <summary>Explicit compatibility version of the composition factory; currently 1.</summary>
    int RuntimeContractVersion { get; }
    /// <summary>Creates a composition with borrowed host services. It must not construct another Core.</summary>
    IRuntimeComposition OpenComposition(ICompositionHost host);
}

/// <summary>A loaded composition and its owned dependency container, retained until host shutdown.</summary>
public interface IRuntimeComposition : IClioComposition, IAsyncDisposable { }

/// <summary>An expected infrastructure resolution failure, safe to expose through any loaded composition.</summary>
public sealed class CoreResolutionException(string code, string message) : Exception(message) {
    /// <summary>A stable machine-readable error identifier.</summary>
    public string Code { get; } = code;
}
