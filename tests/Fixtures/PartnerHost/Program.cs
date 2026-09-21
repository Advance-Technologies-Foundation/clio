using Clio10.Cli;
using Clio10.Mcp;
using Clio10.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Partner.Composition;
var services = new ServiceCollection();
services.AddClioCli(composition => TestWorkflows.Register(composition));
services.AddClioMcp(composition => TestWorkflows.Register(composition));
await using var provider = services.BuildServiceProvider();
return args is ["mcp", ..] ? await provider.GetRequiredService<IMcpAdapter>().RunAsync(args[1..])
    : await provider.GetRequiredService<ICliAdapter>().RunAsync(args);

/// <summary>Test-only operation for checking portable argument transport without external effects.</summary>
public sealed class EchoInput(ILogger<EchoInput> logger) : IClioWorkflow {
    /// <summary>Discovery metadata; reading it does not construct the workflow.</summary>
    public static OperationDescriptor Descriptor => new("test.echo", "Echo portable arguments.", false,
        [new("number", ArgumentKind.Number), new("nested", ArgumentKind.Object)]);
    /// <summary>Complete bundle compatibility and required capabilities.</summary>
    public static PrimitiveRequirement Requirement => new(new(10, 0), new(11, 0), Capabilities: []);
    /// <inheritdoc />
    public Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        logger.LogDebug("Echo fixture executing.");
        return Task.FromResult(new OperationResult(true, "completed", Payload: context.Arguments));
    }
}

/// <summary>Deliberately unresolved dependency used to verify lazy activation.</summary>
public interface IMissingDependency { }
/// <summary>Discovery must not construct this deliberately broken extension.</summary>
public sealed class BrokenWorkflow : IClioWorkflow {
    /// <summary>Requires a service that this test host never registers.</summary>
    public BrokenWorkflow(IMissingDependency missing) { _ = missing; }
    /// <inheritdoc />
    public Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This handler must never be activated.");
}

/// <summary>Test-only unexpected failure with secret-like diagnostic content.</summary>
public sealed class ThrowingWorkflow : IClioWorkflow {
    /// <summary>Discovery metadata; reading it does not construct the workflow.</summary>
    public static OperationDescriptor Descriptor => new("test.throw", "Throw an unexpected error.", false);
    /// <summary>Complete bundle compatibility and required capabilities.</summary>
    public static PrimitiveRequirement Requirement => new(new(10, 0), new(11, 0), Capabilities: []);
    /// <inheritdoc />
    public Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("test-secret-must-not-leak");
}

/// <summary>Test-only completed operation whose payload cannot be serialized.</summary>
public sealed class InvalidPayloadWorkflow : IClioWorkflow {
    /// <summary>Discovery metadata; reading it does not construct the workflow.</summary>
    public static OperationDescriptor Descriptor => new("test.invalid-payload", "Return an invalid payload.", false);
    /// <summary>Complete bundle compatibility and required capabilities.</summary>
    public static PrimitiveRequirement Requirement => new(new(10, 0), new(11, 0), Capabilities: []);
    /// <inheritdoc />
    public Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new OperationResult(true, "completed", PrimitiveVersion: context.PrimitiveVersion.ToString(),
            AcceptedSteps: ["completed-step"], Payload: typeof(InvalidPayloadWorkflow)));
}

/// <summary>Test-only accidental echo of a credential-bearing environment in a nested payload.</summary>
public sealed class EnvironmentEcho : IClioWorkflow {
    /// <inheritdoc />
    public Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new OperationResult(true, "completed", Payload: new { Environment = context.Environment with { Password = "test-secret-must-not-leak" } }));
}

/// <summary>Registers test-only workflows through the same public metadata and keyed DI pattern as partners.</summary>
public static class TestWorkflows {
    /// <summary>Registers the fixture catalog without constructing handlers.</summary>
    public static IServiceCollection Register(IServiceCollection services) {
        services.AddPartnerWorkflows();
        services.AddKeyedScoped<IClioWorkflow, EnvironmentEcho>("test.environment");
        services.AddSingleton(new WorkflowRegistration(new("test.environment", "Echo environment.", false), EchoInput.Requirement));
        services.AddKeyedScoped<IClioWorkflow, BrokenWorkflow>("test.broken");
        services.AddSingleton(new WorkflowRegistration(new("test.broken", "Unresolvable extension.", false), EchoInput.Requirement));
        services.AddKeyedScoped<IClioWorkflow, EchoInput>(EchoInput.Descriptor.Name);
        services.AddSingleton(new WorkflowRegistration(EchoInput.Descriptor, EchoInput.Requirement));
        services.AddKeyedScoped<IClioWorkflow, ThrowingWorkflow>(ThrowingWorkflow.Descriptor.Name);
        services.AddSingleton(new WorkflowRegistration(ThrowingWorkflow.Descriptor, ThrowingWorkflow.Requirement));
        services.AddKeyedScoped<IClioWorkflow, InvalidPayloadWorkflow>(InvalidPayloadWorkflow.Descriptor.Name);
        services.AddSingleton(new WorkflowRegistration(InvalidPayloadWorkflow.Descriptor, InvalidPayloadWorkflow.Requirement));
        return services;
    }
}
