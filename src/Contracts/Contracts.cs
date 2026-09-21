namespace Clio10.Contracts;

/// <summary>An open operation identifier; partners add operations without changing this contract.</summary>
public sealed record CompositionRequest(string Operation, string EnvironmentName = "default",
    IReadOnlyDictionary<string, object?>? Arguments = null);

/// <summary>Discoverable operation metadata independent of any particular adapter.</summary>
public sealed record OperationDescriptor(string Name, string Description, bool Destructive = true, IReadOnlyList<ArgumentDescriptor>? Arguments = null);

/// <summary>A completed workflow outcome, retaining the server response without interpreting it as readiness.</summary>
public sealed record OperationResult(bool Accepted, string Code, string? Response = null, string? PrimitiveVersion = null,
    IReadOnlyList<string>? AcceptedSteps = null, object? Payload = null);

/// <summary>Progress is separate from the final result and contains no credentials.</summary>
public sealed record OperationProgress(string Stage);

/// <summary>Client-facing workflow API without transport or presentation dependencies.</summary>
public interface IClioComposition {
    /// <summary>Returns available vendor and explicitly registered partner operations.</summary>
    IReadOnlyCollection<OperationDescriptor> Operations { get; }
    /// <summary>Executes a registered workflow in a managed context and returns its structured outcome.</summary>
    Task<OperationResult> ExecuteAsync(CompositionRequest request,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
}
