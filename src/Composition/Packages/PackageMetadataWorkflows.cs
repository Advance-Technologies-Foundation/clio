using Clio10.PrimitiveContracts;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using Clio10.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clio10.Composition.Packages;

/// <summary>Registers local package metadata operations independently of remote service workflows.</summary>
public static class PackageMetadataRegistration {
    /// <summary>Adds workflows declaring file reading and, for edits, complete-document publication.</summary>
    public static IServiceCollection AddPackageMetadataWorkflows(this IServiceCollection services) {
        services.AddKeyedScoped<IClioWorkflow, GetPackageVersionWorkflow>("get-pkg-version");
        services.AddSingleton(new WorkflowRegistration(new("get-pkg-version", "Read PackageVersion from a local package descriptor.", false,
            [new("package-path", ArgumentKind.String, true)]), new(new(10, 0), new(11, 0), Capabilities: ["filesystem"])));
        services.AddKeyedScoped<IClioWorkflow, SetPackageVersionWorkflow>("set-pkg-version");
        services.AddSingleton(new WorkflowRegistration(new("set-pkg-version", "Update package version and ModifiedOnUtc together, preserving other descriptor fields.", true,
            [new("package-path", ArgumentKind.String, true), new("package-version", ArgumentKind.String, true)]),
            new(new(10, 0), new(11, 0), Capabilities: ["filesystem", "file-writer"])));
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}

/// <summary>Owns descriptor edit policy; the filesystem capability publishes the complete document.</summary>
public sealed class SetPackageVersionWorkflow(TimeProvider clock) : IClioWorkflow {
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        string path = (string)context.Arguments["package-path"]!;
        string input = ((string)context.Arguments["package-version"]!).Trim();
        int separator = input.IndexOf('-');
        string versionText = separator > 0 ? input[..separator].Trim() : input;
        string suffix = separator > 0 ? input[(separator + 1)..].Trim() : "";
        if (string.IsNullOrWhiteSpace(path) || !Version.TryParse(versionText, out var version)) return new(false, "invalid-arguments");
        string canonical = version + (suffix.Length == 0 ? "" : "-" + suffix);
        try {
            string descriptorPath = Path.Combine(path, "descriptor.json");
            var text = await context.Get<IFileSystemPrimitive>().ReadTextAsync(descriptorPath, cancellationToken);
            if (JsonNode.Parse(text) is not JsonObject root || root["Descriptor"] is not JsonObject descriptor)
                return new(false, "invalid-package-descriptor");
            descriptor["PackageVersion"] = canonical;
            long timestamp = clock.GetUtcNow().ToUnixTimeSeconds() * 1000;
            string? previous = descriptor["ModifiedOnUtc"] is JsonValue oldValue && oldValue.TryGetValue<string>(out var oldText) ? oldText : null;
            if (previous is not null && previous.StartsWith("/Date(", StringComparison.Ordinal) && previous.EndsWith(")/", StringComparison.Ordinal) &&
                long.TryParse(previous.AsSpan(6, previous.Length - 8), out long prior) && prior >= timestamp && prior < 253402300798000)
                timestamp = prior + 1000;
            // Creatio uses this timestamp to decide whether the recorded package version changes.
            descriptor["ModifiedOnUtc"] = $"/Date({timestamp})/";
            // This is a JSON file, not markup embedded into a page: keep localized metadata readable.
            await context.Get<IFileWriterPrimitive>().WriteTextAsync(descriptorPath, root.ToJsonString(new() {
                WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }), cancellationToken);
            return new(true, "completed", PrimitiveVersion: context.PrimitiveVersion.ToString(),
                Payload: new Dictionary<string, object?> { ["packageVersion"] = canonical, ["modifiedOnUtc"] = $"/Date({timestamp})/" });
        }
        catch (FileNotFoundException) { return new(false, "package-descriptor-not-found"); }
        catch (DirectoryNotFoundException) { return new(false, "package-descriptor-not-found"); }
        catch (JsonException) { return new(false, "invalid-package-descriptor"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(false, "package-descriptor-unwritable"); }
    }
}

/// <summary>Interprets package metadata; the primitive owns all filesystem access.</summary>
public sealed class GetPackageVersionWorkflow : IClioWorkflow {
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        string path = (string)context.Arguments["package-path"]!;
        if (string.IsNullOrWhiteSpace(path)) return new(false, "invalid-arguments");
        try {
            var text = await context.Get<IFileSystemPrimitive>().ReadTextAsync(Path.Combine(path, "descriptor.json"), cancellationToken);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Descriptor", out var descriptor) || descriptor.ValueKind != JsonValueKind.Object ||
                !descriptor.TryGetProperty("PackageVersion", out var version) || version.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(version.GetString())) return new(false, "invalid-package-descriptor");
            return new(true, "completed", PrimitiveVersion: context.PrimitiveVersion.ToString(),
                Payload: new Dictionary<string, object?> { ["packageVersion"] = version.GetString() });
        }
        catch (FileNotFoundException) { return new(false, "package-descriptor-not-found"); }
        catch (DirectoryNotFoundException) { return new(false, "package-descriptor-not-found"); }
        catch (JsonException) { return new(false, "invalid-package-descriptor"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(false, "package-descriptor-unreadable"); }
    }
}
