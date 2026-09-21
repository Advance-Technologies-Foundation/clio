using Clio10.PrimitiveContracts;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio10.Contracts;
using Clio10.Composition.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Clio10.Composition.Packages;

/// <summary>Publishes package dependency workflows independently of the execution dispatcher.</summary>
public static class PackageDependencyRegistration {
    /// <summary>Adds dependency editing through the existing scoped HTTP capability.</summary>
    public static IServiceCollection AddPackageDependencyWorkflows(this IServiceCollection services) {
        Add<AddPackageDependencyWorkflow>(services, "add-package-dependency", "Add installed package dependencies, optionally specifying name:version.");
        Add<RemovePackageDependencyWorkflow>(services, "remove-package-dependency", "Remove package dependencies by name; absent dependencies are a no-op.");
        return services;
    }
    private static void Add<T>(IServiceCollection services, string name, string description) where T : class, IClioWorkflow {
        services.AddKeyedScoped<IClioWorkflow, T>(name);
        services.AddSingleton(new WorkflowRegistration(new(name, description, true,
            [new("package-name", ArgumentKind.String, true), new("dependencies", ArgumentKind.String, true,
                "Comma-separated package names; add accepts an optional :version suffix."),
                new("timeout", ArgumentKind.Integer)]), ServiceWorkflow.Requirement));
    }
}

/// <summary>Reads, edits and saves a complete package descriptor in one Core-managed workflow.</summary>
public abstract class PackageDependencyWorkflow : IClioWorkflow {
    /// <summary>Whether requested dependencies are added rather than removed.</summary>
    protected abstract bool Adding { get; }

    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        string packageName = ((string)context.Arguments["package-name"]!).Trim();
        string[] requested = ((string)context.Arguments["dependencies"]!).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        long timeout = context.Arguments.TryGetValue("timeout", out var value) ? Convert.ToInt64(value) : 100_000;
        if (packageName.Length == 0 || requested.Length == 0 || requested.Any(x => x.Split(':')[0].Trim().Length == 0) || timeout is <= 0 or > int.MaxValue)
            return new(false, "invalid-arguments");

        // The child borrows the same identity, selected bundle and execution gate. It authenticates before querying.
        var catalog = await context.InvokeAsync("list-packages", cancellationToken,
            new Dictionary<string, object?> { ["timeout"] = timeout });
        if (!catalog.Accepted) return catalog;
        if (catalog.Payload is not IEnumerable<IReadOnlyDictionary<string, object?>> rows)
            return Failure("invalid-catalog-response");
        var packages = rows.ToArray();
        IReadOnlyDictionary<string, object?>? Find(string name) => packages.FirstOrDefault(x =>
            string.Equals(x["Name"] as string, name, StringComparison.OrdinalIgnoreCase));
        var target = Find(packageName);
        if (target is null) return Failure("package-not-found");
        if (!Guid.TryParse(target["UId"] as string, out var targetId) || targetId == Guid.Empty) return Failure("invalid-catalog-response");

        var additions = new List<JsonObject>();
        if (Adding) {
            foreach (string item in requested) {
                var parts = item.Split(':', 2, StringSplitOptions.TrimEntries);
                var dependency = Find(parts[0]);
                if (dependency is null) return Failure("dependency-not-found");
                if (!Guid.TryParse(dependency["UId"] as string, out var id) || id == Guid.Empty) return Failure("invalid-catalog-response");
                additions.Add(new JsonObject { ["uId"] = id.ToString(), ["name"] = dependency["Name"] as string,
                    ["version"] = parts.Length == 2 && parts[1].Length > 0 ? parts[1] : dependency["Version"] as string });
            }
        }

        context.Report("reading-package-properties");
        var read = await Send("GetPackageProperties", JsonSerializer.Serialize(targetId), true);
        if (!read.Result.Accepted) return read.Result;
        // Preserve unknown fields and numeric values verbatim; sparse DTO saves erase server-owned metadata.
        var root = JsonNode.Parse(read.Response.Body) as JsonObject;
        if (root?["package"] is not JsonObject package || !Guid.TryParse(package["uId"]?.ToString(), out var receivedId) || receivedId != targetId)
            return Failure("invalid-package-response");
        if (package["dependsOnPackages"] is not null && package["dependsOnPackages"] is not JsonArray)
            return Failure("invalid-package-response");
        var dependencies = package["dependsOnPackages"] as JsonArray ?? new JsonArray();
        if (dependencies.Any(x => x is not JsonObject || !Guid.TryParse(x["uId"]?.ToString(), out var id) || id == Guid.Empty))
            return Failure("invalid-package-response");
        if (package["dependsOnPackages"] is null) package["dependsOnPackages"] = dependencies;
        int changed = 0;
        if (Adding) {
            foreach (var addition in additions) {
                var id = Guid.Parse(addition["uId"]!.ToString());
                if (dependencies.Any(x => Guid.Parse(x!["uId"]!.ToString()) == id)) continue;
                dependencies.Add(addition);
                changed++;
            }
        }
        else {
            var names = requested.Select(x => x.Split(':')[0].Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (int index = dependencies.Count - 1; index >= 0; index--)
                if (names.Contains(dependencies[index]!["name"]?.ToString() ?? "")) { dependencies.RemoveAt(index); changed++; }
        }
        object? savePayload = null;
        // Legacy add persists even if already present; remove intentionally avoids saving a no-op.
        if (Adding || changed > 0) {
            context.Report("saving-package-properties");
            cancellationToken.ThrowIfCancellationRequested();
            (OperationResult Result, PrimitiveResponse Response) save;
            try { save = await Send("SavePackageProperties", package.ToJsonString(), false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return Failure("outcome-unknown");
            }
            if (!save.Result.Accepted) return save.Result;
            savePayload = save.Result.Payload;
        }
        return new(true, "completed", PrimitiveVersion: context.PrimitiveVersion.ToString(),
            AcceptedSteps: Adding || changed > 0 ? ["save-package-properties"] : [],
            Payload: new Dictionary<string, object?> {
                ["packageName"] = packageName, ["changedCount"] = changed,
                ["dependencies"] = dependencies.Select(x => x!["name"]?.ToString() ?? x["uId"]!.ToString()).ToArray(),
                ["saveResult"] = savePayload
            });

        OperationResult Failure(string code) => new(false, code, PrimitiveVersion: context.PrimitiveVersion.ToString());
        async Task<(OperationResult Result, PrimitiveResponse Response)> Send(string method, string body, bool isRead) {
            var response = await context.Get<IClioPrimitive>().ExecuteAsync(new("POST",
                (context.Environment.IsNetCore ? "" : "0/") + "ServiceModel/PackageService.svc/" + method, body) {
                    TimeoutMilliseconds = (int)timeout
                }, cancellationToken);
            return (ServiceResults.Read(response, context.PrimitiveVersion, true, false, false, isRead, false, false), response);
        }
    }
}

/// <summary>Adds dependencies by installed package identity while preserving existing dependency metadata.</summary>
public sealed class AddPackageDependencyWorkflow : PackageDependencyWorkflow {
    /// <inheritdoc />
    protected override bool Adding => true;
}

/// <summary>Removes named dependencies without changing unrelated package properties.</summary>
public sealed class RemovePackageDependencyWorkflow : PackageDependencyWorkflow {
    /// <inheritdoc />
    protected override bool Adding => false;
}
