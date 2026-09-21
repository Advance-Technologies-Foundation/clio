using Clio10.PrimitiveContracts;
using System.Globalization;
using System.Text.Json;
using Clio10.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Clio10.Composition.Services;

/// <summary>Registers service workflows without changing the dispatcher or adapters.</summary>
public static class ServiceRegistration {
    /// <summary>Adds vendor service workflows and their discovery schemas.</summary>
    public static IServiceCollection AddServiceWorkflows(this IServiceCollection services) {
        Add<PingWorkflow>(services, new("ping-app", "Check authenticated Creatio connectivity.", false,
            [new("endpoint", ArgumentKind.String)]));
        Add<CallServiceWorkflow>(services, new("call-service", "Call an application-relative Creatio service once; return structured data.", true,
            [new("service-path", ArgumentKind.String, true), new("method", ArgumentKind.String), new("body", ArgumentKind.String),
                new("input", ArgumentKind.String), new("destination", ArgumentKind.String), new("variables", ArgumentKind.Object)]),
            new(new(10, 0), new(11, 0), Capabilities: ["http", "filesystem", "file-writer"]));
        Add<DataServiceWorkflow>(services, new("dataservice", "Execute a raw DataService SELECT, INSERT, UPDATE or DELETE request.", true,
            [new("type", ArgumentKind.String, true), new("body", ArgumentKind.Object, true)]));
        Add<BuildWorkspaceWorkflow>(services, new("build-workspace", "Build modified configuration or rebuild all configuration.", true,
            [new("modified-items", ArgumentKind.Boolean)]));
        Add<GenerateSourcesWorkflow>(services, new("generate-source-code", "Generate schema sources; background takes precedence over modified, then required.", true,
            [new("modified", ArgumentKind.Boolean), new("required", ArgumentKind.Boolean), new("background", ArgumentKind.Boolean)]));
        Add<LastCompilationWorkflow>(services, new("last-compilation-log", "Read the last persisted compilation result, not active compilation status.", false));
        Add<RestoreConfigurationWorkflow>(services, new("restore-configuration", "Restore the last package backup; acceptance does not establish readiness.", true,
            [new("skip-rollback-data", ArgumentKind.Boolean), new("force", ArgumentKind.Boolean)]));
        Add<UserCultureWorkflow>(services, new("get-user-culture", "Read the authenticated user's profile culture, without substituting the system default.", false));
        Add<ListPackagesWorkflow>(services, new("list-packages", "List package names, UIds, maintainers and versions using DataService.", false,
            [new("filter", ArgumentKind.String)]));
        Add<ListApplicationsWorkflow>(services, new("list-apps", "List installed applications using DataService.", false));
        Add<PackageFilesWorkflow>(services, new("show-package-file-content", "List or read compiled package files through ClioGate 2.0.0.47 or newer.", false,
            [new("package", ArgumentKind.String, true), new("file", ArgumentKind.String)]));
        return services;
    }
    private static void Add<T>(IServiceCollection services, OperationDescriptor descriptor, PrimitiveRequirement? requirement = null) where T : class, IClioWorkflow {
        descriptor = descriptor with { Arguments = [.. descriptor.Arguments ?? [], new("timeout", ArgumentKind.Integer, Description: "Positive request timeout in milliseconds.")] };
        services.AddKeyedScoped<IClioWorkflow, T>(descriptor.Name);
        services.AddSingleton(new WorkflowRegistration(descriptor, requirement ?? ServiceWorkflow.Requirement));
    }
}

/// <summary>Authenticates before probing the application.</summary>
public sealed class PingWorkflow : ServiceWorkflow {
    /// <inheritdoc />
    protected override bool RequiresSuccess => false;
    /// <inheritdoc />
    protected override bool AllowsText => true;
    /// <inheritdoc />
    protected override bool AllowsMarkup => true;
    /// <inheritdoc />
    protected override bool AllowsRedirect => true;
    /// <inheritdoc />
    protected override bool IsRead => true;
    /// <inheritdoc />
    protected override PrimitiveRequest CreateRequest(IWorkflowContext context) => new(context.Environment.IsNetCore ? "GET" : "POST",
        context.Arguments.TryGetValue("endpoint", out var value) ? Route(context, (string)value!) : context.Environment.IsNetCore ? "" : "0/ping");
    /// <inheritdoc />
    protected override OperationResult Interpret(OperationResult result, IWorkflowContext context) =>
        result with { Code = "http-reachable", Payload = new Dictionary<string, object?> { ["reachable"] = true } };
}

/// <summary>Transports caller-owned service requests without imposing a domain schema.</summary>
public sealed class CallServiceWorkflow : ServiceWorkflow {
    /// <inheritdoc />
    protected override int DefaultTimeout => 60_000;
    /// <inheritdoc />
    protected override bool RequiresSuccess => false;
    /// <inheritdoc />
    protected override bool AllowsText => true;
    /// <inheritdoc />
    protected override PrimitiveRequest CreateRequest(IWorkflowContext context) {
        var method = context.Arguments.TryGetValue("method", out var value) ? ((string)value!).ToUpperInvariant() : "POST";
        if (method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE")) throw new ArgumentException("Unsupported HTTP method.");
        return new(method, Route(context, Text(context, "service-path")), context.Arguments.TryGetValue("body", out var body) ? (string)body! : "");
    }
    /// <inheritdoc />
    protected override async Task<PrimitiveRequest> CreateRequestAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        var request = CreateRequest(context);
        if (context.Arguments.TryGetValue("variables", out var variables) &&
            ((IReadOnlyDictionary<string, object?>)variables!).Any(pair => pair.Value is not string || string.IsNullOrWhiteSpace(pair.Key)))
            throw new ArgumentException("Variable values must be strings.");
        string body = request.Body ?? "";
        if (string.IsNullOrWhiteSpace(body) && context.Arguments.ContainsKey("input"))
            body = await context.Get<IFileSystemPrimitive>().ReadTextAsync(Text(context, "input"), cancellationToken);
        if (variables is IReadOnlyDictionary<string, object?> values)
            foreach (var (name, value) in values) body = body.Replace("{{" + name + "}}", (string)value!, StringComparison.Ordinal);
        if (context.Arguments.ContainsKey("destination")) _ = Text(context, "destination");
        return request with { Body = body };
    }
    /// <inheritdoc />
    protected override async Task<OperationResult> InterpretAsync(OperationResult result, string responseBody, IWorkflowContext context, CancellationToken cancellationToken) {
        if (!context.Arguments.ContainsKey("destination")) return result;
        try {
            await context.Get<IFileWriterPrimitive>().WriteTextAsync(Text(context, "destination"), responseBody, cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return result with { Accepted = false, Code = "cancelled", AcceptedSteps = ["service-call"] };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) {
            return result with { Accepted = false, Code = "output-write-failed", AcceptedSteps = ["service-call"] };
        }
    }
}

/// <summary>Uses documented DataService routes; Creatio owns the request schema.</summary>
public sealed class DataServiceWorkflow : ServiceWorkflow {
    /// <inheritdoc />
    protected override PrimitiveRequest CreateRequest(IWorkflowContext context) {
        string operation = Text(context, "type").ToUpperInvariant() switch {
            "SELECT" => "SelectQuery", "INSERT" => "InsertQuery", "UPDATE" => "UpdateQuery", "DELETE" => "DeleteQuery",
            _ => throw new ArgumentException("Unknown DataService operation.")
        };
        return new("POST", Route(context, "DataService/json/SyncReply/" + operation), JsonSerializer.Serialize(context.Arguments["body"]));
    }
}

/// <summary>Requests a synchronous configuration build and checks the response envelope.</summary>
public sealed class BuildWorkspaceWorkflow : ServiceWorkflow {
    /// <inheritdoc />
    protected override PrimitiveRequest CreateRequest(IWorkflowContext context) => new("POST",
        Route(context, "ServiceModel/WorkspaceExplorerService.svc/" + (Flag(context, "modified-items") ? "Build" : "Rebuild")), "");
}

/// <summary>Chooses schema-source generation policy before dispatch.</summary>
public sealed class GenerateSourcesWorkflow : ServiceWorkflow {
    /// <inheritdoc />
    protected override int DefaultTimeout => 3_600_000;
    /// <inheritdoc />
    protected override PrimitiveRequest CreateRequest(IWorkflowContext context) => new("POST", Route(context,
        "ServiceModel/WorkspaceExplorerService.svc/" + (Flag(context, "background") ? "GenerateAllSchemasSourcesInBackground" :
            Flag(context, "modified") ? "GenerateModifiedSchemasSources" : Flag(context, "required") ? "GenerateRequiredSchemasSources" : "GenerateAllSchemasSources")), "");
    /// <inheritdoc />
    protected override OperationResult Interpret(OperationResult result, IWorkflowContext context) =>
        Flag(context, "background") ? result with { Code = "generation-started" } : result;
}

/// <summary>Reads the latest persisted compilation report.</summary>
public sealed class LastCompilationWorkflow : ServiceWorkflow {
    /// <inheritdoc />
    protected override bool RequiresSuccess => false;
    /// <inheritdoc />
    protected override PrimitiveRequest CreateRequest(IWorkflowContext context) => new("GET", Route(context, "api/ConfigurationStatus/GetLastCompilationResult"));
}

/// <summary>Requests restoration with explicit data and SQL compatibility flags.</summary>
public sealed class RestoreConfigurationWorkflow : ServiceWorkflow {
    /// <inheritdoc />
    protected override PrimitiveRequest CreateRequest(IWorkflowContext context) => new("POST", Route(context,
        "ServiceModel/PackageInstallerService.svc/RestoreFromPackageBackup"), JsonSerializer.Serialize(new {
            installPackageData = Flag(context, "skip-rollback-data"), ignoreSqlScriptBackwardCompatibilityCheck = Flag(context, "force")
        }));
}

/// <summary>Resolves the profile culture from ApplicationInfo without a process-wide user cache.</summary>
public sealed class UserCultureWorkflow : ServiceWorkflow {
    /// <inheritdoc />
    protected override bool RequiresSuccess => false;
    /// <inheritdoc />
    protected override bool IsRead => true;
    /// <inheritdoc />
    protected override PrimitiveRequest CreateRequest(IWorkflowContext context) => new("POST", Route(context,
        "ServiceModel/ApplicationInfoService.svc/GetApplicationInfo"), "{}");
    /// <inheritdoc />
    protected override OperationResult Interpret(OperationResult result, IWorkflowContext context) {
        if (result.Payload is not IReadOnlyDictionary<string, object?> root ||
            !root.TryGetValue("applicationInfo", out var info) || info is not IReadOnlyDictionary<string, object?> app ||
            !app.TryGetValue("sysValues", out var values) || values is not IReadOnlyDictionary<string, object?> sys ||
            !sys.TryGetValue("userCulture", out var user) || user is not IReadOnlyDictionary<string, object?> culture ||
            !culture.TryGetValue("displayValue", out var display) || display is not string name || string.IsNullOrWhiteSpace(name))
            return new(false, "user-culture-missing", PrimitiveVersion: result.PrimitiveVersion);
        try { return result with { Payload = new Dictionary<string, object?> { ["culture"] = CultureInfo.GetCultureInfo(name.Trim()).Name } }; }
        catch (CultureNotFoundException) { return new(false, "user-culture-invalid", PrimitiveVersion: result.PrimitiveVersion); }
    }
}

/// <summary>Reads explicit columns from a vendor-owned catalog query.</summary>
public abstract class CatalogWorkflow : ServiceWorkflow {
    /// <summary>The schema owned by this catalog operation.</summary>
    protected abstract string Entity { get; }
    /// <summary>Requested fields; missing columns must surface as failures.</summary>
    protected abstract string[] Columns { get; }
    /// <inheritdoc />
    protected override bool IsRead => true;
    /// <inheritdoc />
    protected override PrimitiveRequest CreateRequest(IWorkflowContext context) => new("POST", Route(context, "DataService/json/SyncReply/SelectQuery"),
        JsonSerializer.Serialize(new { rootSchemaName = Entity, operationType = 0, rowCount = 10000, isDistinct = false,
            allColumns = false, ignoreDisplayValues = false, rowsOffset = -1, isPageable = false,
            columns = new { items = Columns.ToDictionary(name => name, name => new {
                expression = new { expressionType = 0, columnPath = name }, orderDirection = 0, orderPosition = -1, isVisible = true }) },
            filters = new { filterType = 6, logicalOperation = 0, isEnabled = true, items = new Dictionary<string, object>() } }));
    /// <inheritdoc />
    protected override OperationResult Interpret(OperationResult result, IWorkflowContext context) {
        if (result.Payload is not IReadOnlyDictionary<string, object?> data || !data.TryGetValue("rows", out var rows) || rows is not object?[] items ||
            items.Any(row => row is not IReadOnlyDictionary<string, object?> fields || Columns.Any(column => !fields.ContainsKey(column))))
            return new(false, "invalid-catalog-response", PrimitiveVersion: result.PrimitiveVersion);
        var filter = context.Arguments.TryGetValue("filter", out var value) ? (string)value! : "";
        var selected = items.Cast<IReadOnlyDictionary<string, object?>>().Where(row => (row["Name"] as string ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row["Name"] as string, StringComparer.OrdinalIgnoreCase).ToArray();
        return result with { Payload = selected };
    }
}

/// <summary>Reads the package catalog with case-insensitive name filtering.</summary>
public sealed class ListPackagesWorkflow : CatalogWorkflow {
    /// <inheritdoc />
    protected override string Entity => "SysPackage";
    /// <inheritdoc />
    protected override string[] Columns => ["Name", "UId", "Maintainer", "Version"];
}

/// <summary>Reads installed application summaries without an ATF repository dependency.</summary>
public sealed class ListApplicationsWorkflow : CatalogWorkflow {
    /// <inheritdoc />
    protected override string Entity => "SysInstalledApp";
    /// <inheritdoc />
    protected override string[] Columns => ["Id", "Name", "Code", "Version", "Description"];
}
