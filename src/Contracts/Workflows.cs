namespace Clio10.Contracts;

/// <summary>Immutable discovery metadata. Register the implementation as keyed IClioWorkflow using Descriptor.Name.</summary>
/// <remarks>Use a scoped keyed DI registration and one singleton metadata record. Duplicate names are catalog errors. Metadata must not perform I/O.</remarks>
public sealed record WorkflowRegistration(OperationDescriptor Descriptor, PrimitiveRequirement Requirement);

/// <summary>A scoped execution implementation; discovery metadata is registered separately.</summary>
public interface IClioWorkflow {
    /// <summary>Executes on one target using borrowed capabilities. Await all children; never open a nested root or retain the context.</summary>
    Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken);
}

/// <summary>Borrowed capabilities and child invocation for one single-target root.</summary>
/// <remarks>Cross-environment orchestration belongs to the host as separate roots; it is not atomic. Nested roots on the same Core are rejected.</remarks>
public interface IWorkflowContext : ICapabilityProvider {
    /// <summary>Immutable portable input snapshot.</summary>
    IReadOnlyDictionary<string, object?> Arguments { get; }
    /// <summary>The environment snapshot. Treat credentials as secrets.</summary>
    ClioEnvironment Environment { get; }
    /// <summary>The pinned complete bundle release.</summary>
    Version PrimitiveVersion { get; }
    /// <summary>Reports nonsecret progress independently of results.</summary>
    void Report(string stage);
    /// <summary>Invokes a registered child on the same target, bundle and execution scope.</summary>
    /// <remarks>Arguments replace input entirely; omission means empty input. Await sequentially. Overlapping children are rejected; unfinished children drain before the root releases its session.</remarks>
    Task<OperationResult> InvokeAsync(string operation, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object?>? arguments = null);
}

/// <summary>A safe extension contract violation, without implementation exception details.</summary>
public sealed class WorkflowContractException(string code) : Exception(code) {
    /// <summary>Stable failure category.</summary>
    public string Code { get; } = code;
}
