using Clio10.PrimitiveContracts;
using System.Text.Json;
using Clio10.Contracts;
using Clio10.Composition.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Clio10.Composition.Packages;

/// <summary>Registers package activation independently of the dispatcher and presentation layers.</summary>
public static class PackageActivationRegistration {
    /// <summary>Adds activation and deactivation using the platform package service.</summary>
    public static IServiceCollection AddPackageActivationWorkflows(this IServiceCollection services) {
        Add<ActivatePackageWorkflow>(services, "activate-pkg", "Activate a package by name and report each package outcome.");
        Add<DeactivatePackageWorkflow>(services, "deactivate-pkg", "Resolve and deactivate a package by its installed identity.");
        return services;
    }
    private static void Add<T>(IServiceCollection services, string name, string description) where T : class, IClioWorkflow {
        services.AddKeyedScoped<IClioWorkflow, T>(name);
        services.AddSingleton(new WorkflowRegistration(new(name, description, true,
            [new("package-name", ArgumentKind.String, true), new("timeout", ArgumentKind.Integer)]), ServiceWorkflow.Requirement));
    }
}

/// <summary>Activates a package and retains per-package success and partial acceptance.</summary>
public sealed class ActivatePackageWorkflow : ServiceWorkflow {
    /// <inheritdoc />
    protected override PrimitiveRequest CreateRequest(IWorkflowContext context) => new("POST",
        Route(context, "ServiceModel/PackageService.svc/ActivatePackage"), JsonSerializer.Serialize(Text(context, "package-name").Trim()));
    /// <inheritdoc />
    protected override OperationResult Interpret(OperationResult result, IWorkflowContext context) {
        if (result.Payload is not IReadOnlyDictionary<string, object?> root ||
            !root.TryGetValue("packagesActivationResults", out var value) || value is not object?[] { Length: > 0 } rows)
            return result with { Accepted = false, Code = "invalid-activation-response" };
        var outcomes = rows.OfType<IReadOnlyDictionary<string, object?>>().Where(row =>
            row.TryGetValue("success", out var success) && success is bool &&
            row.TryGetValue("packageName", out var name) && name is string text && !string.IsNullOrWhiteSpace(text)).ToArray();
        var accepted = outcomes.Where(x => x["success"] is true).Select(x => "activate:" + x["packageName"]).ToArray();
        if (outcomes.Length != rows.Length)
            return result with { Accepted = false, Code = "invalid-activation-response", AcceptedSteps = accepted };
        bool completed = accepted.Length == outcomes.Length;
        return result with { Accepted = completed, Code = completed ? "completed" : "package-activation-failed", AcceptedSteps = accepted };
    }
}

/// <summary>Resolves the package identity before a single deactivation request in the same managed context.</summary>
public sealed class DeactivatePackageWorkflow : IClioWorkflow {
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        var name = ((string)context.Arguments["package-name"]!).Trim();
        long timeout = context.Arguments.TryGetValue("timeout", out var value) ? Convert.ToInt64(value) : 100_000;
        if (name.Length == 0 || timeout is <= 0 or > int.MaxValue) return new(false, "invalid-arguments");
        var catalog = await context.InvokeAsync("list-packages", cancellationToken, new Dictionary<string, object?> { ["timeout"] = timeout });
        if (!catalog.Accepted) return catalog;
        if (catalog.Payload is not IEnumerable<IReadOnlyDictionary<string, object?>> rows)
            return new(false, "invalid-catalog-response");
        var package = rows.FirstOrDefault(x => string.Equals(x["Name"] as string, name, StringComparison.OrdinalIgnoreCase));
        if (package is null) return new(false, "package-not-found");
        if (!Guid.TryParse(package["UId"] as string, out var id) || id == Guid.Empty) return new(false, "invalid-catalog-response");
        context.Report("deactivating-package");
        cancellationToken.ThrowIfCancellationRequested();
        PrimitiveResponse response;
        try {
            response = await context.Get<IClioPrimitive>().ExecuteAsync(new("POST",
                (context.Environment.IsNetCore ? "" : "0/") + "ServiceModel/PackageService.svc/DeactivatePackage", JsonSerializer.Serialize(id)) {
                    TimeoutMilliseconds = (int)timeout
                }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return new(false, "outcome-unknown", PrimitiveVersion: context.PrimitiveVersion.ToString());
        }
        var result = ServiceResults.Read(response, context.PrimitiveVersion, true, false, false, false, false, false);
        return result.Accepted ? result with { AcceptedSteps = ["deactivate-package"] } : result;
    }
}
