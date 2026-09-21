using System.ComponentModel;
using System.Text.Json;
using Clio10.Contracts;
using Clio10.AdapterShared;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
namespace Clio10.Mcp;

/// <summary>Stable discovery/dispatch bridge for all registered workflows.</summary>
[McpServerToolType]
public sealed class CreatioTools(IServiceScopeFactory scopes) {
    /// <summary>Lists operation identities, descriptions, effects and typed argument descriptors.</summary>
    [McpServerTool(Name = "list-operations", ReadOnly = true, Destructive = false)]
    [Description("List registered operations and their argument schemas. Discover operations before execution.")]
    public async Task<CallToolResult> ListAsync() {
        try {
            await using var scope = scopes.CreateAsyncScope();
            var operations = scope.ServiceProvider.GetRequiredService<IClioComposition>().Operations;
            return new() { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(operations, JsonArguments.Output) }] };
        }
        catch (CoreResolutionException error) { return Render(new(false, error.Code)); }
        catch (WorkflowContractException error) { return Render(new(false, error.Code)); }
        catch (Exception) { return Render(new(false, "discovery-failed")); }
    }

    /// <summary>Executes any registered operation. Conservative annotation covers destructive workflows.</summary>
    [McpServerTool(Name = "execute", Destructive = true, ReadOnly = false)]
    [Description("Execute a discovered operation with its typed arguments. May be destructive; inspect list-operations. Do not blindly retry unknown outcomes.")]
    public async Task<CallToolResult> ExecuteAsync(string operation, JsonElement? arguments = null,
        CancellationToken cancellationToken = default) {
        IReadOnlyDictionary<string, object?>? values;
        try {
            if (arguments is { ValueKind: not JsonValueKind.Object }) return Render(new(false, "invalid-arguments"));
            values = arguments is null ? null : (IReadOnlyDictionary<string, object?>?)JsonArguments.Convert(arguments.Value);
        }
        catch (Exception error) when (error is JsonException or ArgumentException) {
            return Render(new(false, "invalid-arguments"));
        }
        try {
            OperationResult result;
            var scope = scopes.CreateAsyncScope();
            try {
                result = await scope.ServiceProvider.GetRequiredService<IClioComposition>().ExecuteAsync(new(operation, Arguments: values), cancellationToken: cancellationToken);
            }
            finally { await ResultOutput.DisposeAsync(scope); }
            return Render(result);
        }
        catch (CoreResolutionException error) { return Render(new(false, error.Code)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Render(ResultOutput.UnknownOutcome()); }
    }
    private static CallToolResult Render(OperationResult result) {
        var output = ResultOutput.Render(result);
        return new() { IsError = output.IsError, Content = [new TextContentBlock { Text = output.Json }] };
    }
}
