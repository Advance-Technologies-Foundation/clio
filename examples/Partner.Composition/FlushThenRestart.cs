using Clio10.PrimitiveContracts;
using Clio10.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Partner.Composition;

/// <summary>Partner policy using vendor operations without implementation dependencies.</summary>
public sealed class FlushThenRestart : IClioWorkflow {
    /// <summary>Discovery metadata; reading it does not construct the workflow.</summary>
    public static OperationDescriptor Descriptor => new("partner.flush-then-restart", "Clear cache, optionally restart.",
        Arguments: [new("restart-after-flush", ArgumentKind.Boolean)]);
    /// <summary>Complete bundle compatibility and required capabilities.</summary>
    public static PrimitiveRequirement Requirement => new(new(10, 0, 0, 0), new(11, 0, 0, 0), Capabilities: ["http"]);
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        var flush = await context.InvokeAsync("flush-redis", cancellationToken);
        if (!flush.Accepted) return flush;
        if (context.Arguments.TryGetValue("restart-after-flush", out var value) && value is false)
            return flush with { AcceptedSteps = ["flush-redis"] };
        var restart = await context.InvokeAsync("restart", cancellationToken);
        return restart with { AcceptedSteps = restart.Accepted ? ["flush-redis", "restart"] : ["flush-redis"] };
    }
}

/// <summary>Typed partner result surfaced unchanged to callers.</summary>
public sealed record TextSummary(string Label, int Characters, int Lines);

/// <summary>Non-HTTP workflow using a vendor capability through the shared interface.</summary>
public sealed class InspectText : IClioWorkflow {
    /// <summary>Discovery metadata; reading it does not construct the workflow.</summary>
    public static OperationDescriptor Descriptor => new("partner.inspect-text", "Describe a UTF-8 text file.", false,
        [new("path", ArgumentKind.String, true), new("label", ArgumentKind.String, true)]);
    /// <summary>Complete bundle compatibility and required capabilities.</summary>
    public static PrimitiveRequirement Requirement => new(new(10, 0, 0, 0), new(11, 0, 0, 0), Capabilities: ["filesystem"]);
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        try {
            if (string.IsNullOrWhiteSpace((string)context.Arguments["path"]!)) return new(false, "invalid-path");
            string content = await context.Get<IFileSystemPrimitive>().ReadTextAsync((string)context.Arguments["path"]!, cancellationToken);
            return new(true, "completed", PrimitiveVersion: context.PrimitiveVersion.ToString(),
                Payload: new TextSummary((string)context.Arguments["label"]!, content.Length, content.Split('\n').Length));
        }
        catch (ArgumentException) { return new(false, "invalid-path"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(false, "file-unavailable"); }
    }
}

/// <summary>Calls the same child twice with isolated arguments, including a nested structured input.</summary>
public sealed class CompareTexts : IClioWorkflow {
    /// <summary>Discovery metadata; reading it does not construct the workflow.</summary>
    public static OperationDescriptor Descriptor => new("partner.compare-texts", "Inspect two files with separate child arguments.", false,
        [new("files", ArgumentKind.Object, true, Fields: [new("left", ArgumentKind.String, true), new("right", ArgumentKind.String, true)])]);
    /// <summary>Complete bundle compatibility and required capabilities.</summary>
    public static PrimitiveRequirement Requirement => new(new(10, 0, 0, 0), new(11, 0, 0, 0), Capabilities: ["filesystem"]);
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        var files = (IReadOnlyDictionary<string, object?>)context.Arguments["files"]!;
        if (!files.TryGetValue("left", out var left) || left is not string || !files.TryGetValue("right", out var right) || right is not string)
            return new(false, "invalid-files");
        var first = await context.InvokeAsync("partner.inspect-text", cancellationToken,
            new Dictionary<string, object?> { ["path"] = left, ["label"] = "left" });
        if (!first.Accepted) return first;
        var second = await context.InvokeAsync("partner.inspect-text", cancellationToken,
            new Dictionary<string, object?> { ["path"] = right, ["label"] = "right" });
        return second with { Payload = new ComparisonSummary((TextSummary)first.Payload!, second.Payload as TextSummary) };
    }
}

/// <summary>Structured multi-step result with independently typed child results.</summary>
public sealed record ComparisonSummary(TextSummary Left, TextSummary? Right);

/// <summary>Optional partner-owned registration; no vendor source edit is needed.</summary>
public static class PartnerRegistration {
    /// <summary>Adds all partner operations alongside the vendor catalog.</summary>
    public static IServiceCollection AddPartnerWorkflows(this IServiceCollection services) {
        services.AddKeyedScoped<IClioWorkflow, FlushThenRestart>(FlushThenRestart.Descriptor.Name);
        services.AddSingleton(new WorkflowRegistration(FlushThenRestart.Descriptor, FlushThenRestart.Requirement));
        services.AddKeyedScoped<IClioWorkflow, InspectText>(InspectText.Descriptor.Name);
        services.AddSingleton(new WorkflowRegistration(InspectText.Descriptor, InspectText.Requirement));
        services.AddKeyedScoped<IClioWorkflow, CompareTexts>(CompareTexts.Descriptor.Name);
        services.AddSingleton(new WorkflowRegistration(CompareTexts.Descriptor, CompareTexts.Requirement));
        return services;
    }
}
