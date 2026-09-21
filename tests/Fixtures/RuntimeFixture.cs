using Clio10.Contracts;
using Clio10.Runtime;
using Microsoft.Extensions.DependencyInjection;
#if RELEASE_V2
using Clio10.FeatureFixture;
#endif

namespace Clio10.RuntimeFixture;

/// <summary>Two separately compiled releases of a workflow module, packaged with the real vendor implementation.</summary>
public sealed class ReleaseBundle : RuntimeBundle, IRuntimeBundle {
#if RELEASE_V2
    /// <summary>V2 introduces a capability absent from the host and V1.</summary>
    public new IReadOnlyCollection<string> Capabilities => [..base.Capabilities, "release-file"];
    /// <summary>Owns the new primitive alongside the vendor session.</summary>
    public new IPrimitiveSession OpenSession(PrimitiveSessionOptions options) {
        var services = new ServiceCollection();
        services.AddSingleton<IReleaseFile, ReleaseFile>();
        var provider = services.BuildServiceProvider();
        try { return new FeatureSession(base.OpenSession(options), provider); }
        catch { provider.Dispose(); throw; }
    }
    private sealed class FeatureSession(IPrimitiveSession inner, ServiceProvider provider) : IPrimitiveSession {
        public object? GetCapability(string name) => name == "release-file" ? provider.GetRequiredService<IReleaseFile>() : inner.GetCapability(name);
        public async ValueTask DisposeAsync() { try { await inner.DisposeAsync(); } finally { await provider.DisposeAsync(); } }
        public void Dispose() { try { inner.Dispose(); } finally { provider.Dispose(); } }
    }
#endif
    /// <inheritdoc />
    protected override void ConfigureWorkflows(IServiceCollection services) {
        var requirement = new PrimitiveRequirement(new(10, 0), new(11, 0), Capabilities: ["http"]);
        services.AddKeyedScoped<IClioWorkflow, ReleaseWorkflow>("release-proof");
        services.AddSingleton(new WorkflowRegistration(new("release-proof", "Exercise the release's sequence."), requirement));
        services.AddKeyedScoped<IClioWorkflow, ReleaseStep>("release-step");
        services.AddSingleton(new WorkflowRegistration(new("release-step", "A nested release-owned step."), requirement));
#if RELEASE_V2
        services.AddKeyedScoped<IClioWorkflow, FeatureWorkflow>("runtime-file");
        services.AddSingleton(new WorkflowRegistration(new("runtime-file", "Use a release-private capability.", true,
            [new("path", ArgumentKind.String, true)]), requirement with { Capabilities = ["release-file"] }));
        services.AddKeyedScoped<IClioWorkflow, AddedWorkflow>("runtime-added");
        services.AddSingleton(new WorkflowRegistration(new("runtime-added", "Only shipped by V2.", false), requirement));
#endif
    }
}

/// <summary>V1 restarts then flushes; V2 reverses that policy without an adapter or Core change.</summary>
public sealed class ReleaseWorkflow : IClioWorkflow {
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
#if RELEASE_V2
        var first = await context.InvokeAsync("release-step", cancellationToken);
        if (!first.Accepted) return first;
        var last = await context.InvokeAsync("restart", cancellationToken);
#else
        var first = await context.InvokeAsync("restart", cancellationToken);
        if (!first.Accepted) return first;
        var last = await context.InvokeAsync("release-step", cancellationToken);
#endif
        return last with { Code = "composition-" + typeof(ReleaseWorkflow).Assembly.GetName().Version,
            Payload = new { ChildVersion = typeof(ReleaseStep).Assembly.GetName().Version!.ToString() } };
    }
}

/// <summary>The nested handler is resolved from the root's original runtime scope.</summary>
public sealed class ReleaseStep : IClioWorkflow {
    /// <inheritdoc />
    public Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) =>
        context.InvokeAsync("flush-redis", cancellationToken);
}

#if RELEASE_V2
/// <summary>Uses a capability API the installed application has never referenced.</summary>
public sealed class FeatureWorkflow : IClioWorkflow {
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        var capability = context.Get<IReleaseFile>();
        string path = (string)context.Arguments["path"]!;
#if RELEASE_V3
        string text = (await capability.PublishAsync(new(path, "v3-new-signature"), cancellationToken)).Text;
#else
        string text = await capability.WriteReadAsync(path, "v2-new-capability", cancellationToken);
#endif
        return new(true, "completed", PrimitiveVersion: context.PrimitiveVersion.ToString(),
            Payload: new Dictionary<string, object?> { ["text"] = text,
                ["contractVersion"] = typeof(IReleaseFile).Assembly.GetName().Version!.ToString() });
    }
}

/// <summary>A new command absent from the initial product and release.</summary>
public sealed class AddedWorkflow : IClioWorkflow {
    /// <inheritdoc />
    public Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new OperationResult(true, "new-runtime-command", PrimitiveVersion: context.PrimitiveVersion.ToString()));
}
#endif
