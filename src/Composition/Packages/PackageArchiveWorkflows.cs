using Clio10.PrimitiveContracts;
using Clio10.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clio10.Composition.Packages;

/// <summary>Registers package selection workflows separately from binary archive I/O.</summary>
public static class PackageArchiveRegistration {
    /// <summary>Adds package selection and gzip publication workflows.</summary>
    public static IServiceCollection AddPackageArchiveWorkflows(this IServiceCollection services) {
        services.AddTransient<Ignore.Ignore>();
        services.AddScoped<Func<Ignore.Ignore>>(provider => () => provider.GetRequiredService<Ignore.Ignore>());
        services.AddScoped<IPackageArchiveSelection, PackageArchiveSelection>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddKeyedScoped<IClioWorkflow, CompressPackageWorkflow>("generate-pkg-zip");
        services.AddKeyedScoped<IClioWorkflow, ExtractPackageWorkflow>("extract-pkg-zip");
        var requirement = new PrimitiveRequirement(new(10, 0), new(11, 0), Capabilities: ["archive"]);
        services.AddSingleton(new WorkflowRegistration(new("generate-pkg-zip", "Create a Creatio package gzip archive or a ZIP of selected packages.", true,
            [new("package-path", ArgumentKind.String), new("destination-path", ArgumentKind.String), new("skip-pdb", ArgumentKind.Boolean), new("packages", ArgumentKind.String)]),
            new(new(10, 0), new(11, 0), Capabilities: ["archive", "filesystem"])));
        services.AddSingleton(new WorkflowRegistration(new("extract-pkg-zip", "Extract Creatio gzip, a ZIP container, or a directory of gzip archives; replacement requires explicit overwrite.", true,
            [new("archive-path", ArgumentKind.String, true), new("destination-path", ArgumentKind.String), new("overwrite", ArgumentKind.Boolean)]), requirement));
        return services;
    }
}

/// <summary>Selects package content according to the vendor layout; delegates all external I/O.</summary>
public sealed class CompressPackageWorkflow(IPackageArchiveSelection selection, TimeProvider clock) : IClioWorkflow {
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        bool batch = context.Arguments.TryGetValue("packages", out var packagesValue);
        string root = context.Arguments.TryGetValue("package-path", out var rootValue) ? ((string)rootValue!).Trim() : batch ? "." : "";
        if (root.Length == 0) return new(false, "invalid-arguments");
        string destination = context.Arguments.TryGetValue("destination-path", out var path) ? (string)path! : batch ?
            $"packages_{clock.GetLocalNow():yy.MM.dd_hh.mm.ss}.zip" : Path.TrimEndingDirectorySeparator(root) + ".gz";
        if (string.IsNullOrWhiteSpace(destination)) return new(false, "invalid-arguments");
        try {
            var archive = context.Get<IArchivePrimitive>();
            string? outputParent = Path.GetDirectoryName(destination);
            if (!await archive.DirectoryExistsAsync(string.IsNullOrEmpty(outputParent) ? "." : outputParent, cancellationToken))
                return new(false, "archive-destination-unavailable");
            bool skipPdb = context.Arguments.TryGetValue("skip-pdb", out var skip) && skip is true;
            if (batch) {
                string[] names = ((string)packagesValue!).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (names.Length == 0 || names.Any(x => x is "." or ".." || x.IndexOfAny(['/', '\\', ':']) >= 0) ||
                    names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) return new(false, "invalid-arguments");
                var sources = new List<ArchiveSource>();
                foreach (string name in names) {
                    var package = await selection.SelectAsync(context, Path.Combine(root, name), skipPdb, cancellationToken);
                    sources.Add(new(name, package.Root, package.Files));
                }
                return Completed(await archive.PackContainerAsync(sources, destination, new(new()), cancellationToken), context);
            }
            var selected = await selection.SelectAsync(context, root, skipPdb, cancellationToken);
            var result = await archive.PackAsync(selected.Root, selected.Files, destination, new(), cancellationToken);
            return Completed(result, context);
        }
        catch (InvalidDataException) { return new(false, "invalid-package-archive"); }
        catch (FileNotFoundException) { return new(false, "package-file-not-found"); }
        catch (DirectoryNotFoundException) { return new(false, "package-file-not-found"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return new(false, "archive-write-failed"); }
    }
    internal static OperationResult Completed(ArchiveResult result, IWorkflowContext context) => new(true, "completed",
        PrimitiveVersion: context.PrimitiveVersion.ToString(), Payload: new Dictionary<string, object?> {
            ["destination"] = result.Destination, ["files"] = result.Files, ["bytes"] = result.Bytes, ["retainedBackup"] = result.RetainedBackup
        });
}

/// <summary>Chooses the package directory name; archive decoding and safe publication belong to Primitives.</summary>
public sealed class ExtractPackageWorkflow : IClioWorkflow {
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        string archive = ((string)context.Arguments["archive-path"]!).Trim();
        string parent = context.Arguments.TryGetValue("destination-path", out var destination) && !string.IsNullOrWhiteSpace((string?)destination) ? ((string)destination!).Trim() : ".";
        if (archive.Length == 0) return new(false, "invalid-arguments");
        try {
            var primitive = context.Get<IArchivePrimitive>();
            if (!await primitive.DirectoryExistsAsync(parent, cancellationToken)) return new(false, "archive-destination-unavailable");
            bool overwrite = context.Arguments.TryGetValue("overwrite", out var replace) && replace is true;
            if (await primitive.DirectoryExistsAsync(archive, cancellationToken)) {
                var entries = await primitive.ListEntriesAsync(archive, cancellationToken);
                var selected = entries.Where(x => !x.IsDirectory && x.Name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (selected.Any(x => x.IsLink)) return new(false, "invalid-package-archive");
                var inputs = selected.Select(x => new ArchiveInput(x.Name[..^3], Path.Combine(archive, x.Name))).ToArray();
                return CompletedBatch(await primitive.ExtractManyAsync(inputs, parent, new(new()), cancellationToken, overwrite), context);
            }
            string extension = Path.GetExtension(archive);
            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return CompletedBatch(await primitive.ExtractContainerAsync(archive, parent, new(new()), cancellationToken, overwrite), context);
            if (!extension.Equals(".gz", StringComparison.OrdinalIgnoreCase)) archive += ".gz";
            string name = Path.GetFileNameWithoutExtension(archive);
            if (name is "" or "." or "..") return new(false, "invalid-arguments");
            var result = await primitive.ExtractAsync(archive, Path.Combine(parent, name), new(), cancellationToken, overwrite);
            return CompressPackageWorkflow.Completed(result, context);
        }
        catch (InvalidDataException) { return new(false, "invalid-package-archive"); }
        catch (EndOfStreamException) { return new(false, "invalid-package-archive"); }
        catch (ArchiveRestoreException error) { return new(false, "archive-recovery-required", Payload: new Dictionary<string, object?> {
            ["destination"] = error.Destination, ["backup"] = error.BackupPath
        }); }
        catch (FileNotFoundException) { return new(false, "archive-path-not-found"); }
        catch (DirectoryNotFoundException) { return new(false, "archive-path-not-found"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return new(false, "archive-extraction-failed"); }
    }
    private static OperationResult CompletedBatch(ArchiveBatchResult result, IWorkflowContext context) => new(result.FailedDestination is null,
        result.FailedDestination is null ? "completed" : result.RecoveryPath is null ? "archive-extraction-failed" : "archive-recovery-required",
        PrimitiveVersion: context.PrimitiveVersion.ToString(), AcceptedSteps: result.Completed.Select(x => x.Destination).ToArray(),
        Payload: new Dictionary<string, object?> {
            ["packages"] = result.Completed.Select(x => new Dictionary<string, object?> {
                ["destination"] = x.Destination, ["files"] = x.Files, ["bytes"] = x.Bytes, ["retainedBackup"] = x.RetainedBackup
            }).ToArray(), ["failedDestination"] = result.FailedDestination, ["backup"] = result.RecoveryPath
        });
}
