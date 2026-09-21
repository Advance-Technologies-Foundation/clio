using Clio10.PrimitiveContracts;
using System.Text.Json;
using Clio10.Contracts;
using Clio10.Core;

namespace Clio10.Composition;

/// <summary>Shared authentication and result interpretation for vendor maintenance operations.</summary>
public abstract class MaintenanceWorkflow : IClioWorkflow {
    /// <summary>Complete bundle compatibility and required capabilities.</summary>
    public static PrimitiveRequirement Requirement => new(new(10, 0, 0, 0), new(11, 0, 0, 0), Capabilities: ["http"]);
    /// <summary>Chooses the platform-specific Creatio service method.</summary>
    protected abstract string Method(bool isNetCore);
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(context.Environment.UserName) || string.IsNullOrEmpty(context.Environment.Password))
            return new(false, "missing-credentials");
        context.Report("authenticating");
        var login = await context.Get<IClioPrimitive>().LoginAsync(cancellationToken);
        if (login.TransportError is not null) return new(false, login.TransportError);
        if (login.StatusCode is not (>= 200 and < 300)) return new(false, "authentication-http-failure");
        try {
            using var json = JsonDocument.Parse(login.Body);
            if (!json.RootElement.TryGetProperty("Code", out var code) || !code.TryGetInt32(out int value) || value != 0)
                return new(false, "authentication-rejected");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return new(false, "invalid-authentication-response"); }
        context.Report("executing");
        string prefix = context.Environment.IsNetCore ? "" : "0/";
        var response = await context.Get<IClioPrimitive>().ExecuteAsync(new("POST",
            $"{prefix}ServiceModel/AppInstallerService.svc/{Method(context.Environment.IsNetCore)}", "{}"), cancellationToken);
        if (response.TransportError is not null) return new(false, "outcome-unknown", PrimitiveVersion: context.PrimitiveVersion.ToString());
        bool accepted = response.StatusCode is >= 200 and < 300;
        return new(accepted, accepted ? "http-accepted" : "operation-http-failure", response.Body, context.PrimitiveVersion.ToString());
    }
}

/// <summary>Requests a restart without claiming the application has become ready.</summary>
public sealed class RestartWorkflow : MaintenanceWorkflow {
    /// <summary>Discovery metadata; reading it does not construct the workflow.</summary>
    public static OperationDescriptor Descriptor => new(Operation.Restart, "Request a Creatio restart.");
    /// <inheritdoc />
    protected override string Method(bool isNetCore) => isNetCore ? "RestartApp" : "UnloadAppDomain";
}

/// <summary>Clears Creatio's configured Redis database.</summary>
public sealed class FlushRedisWorkflow : MaintenanceWorkflow {
    /// <summary>Discovery metadata; reading it does not construct the workflow.</summary>
    public static OperationDescriptor Descriptor => new(Operation.FlushRedis, "Clear Creatio's Redis database.");
    /// <inheritdoc />
    protected override string Method(bool isNetCore) => "ClearRedisDb";
}
