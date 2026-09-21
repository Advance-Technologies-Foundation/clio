using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio10.SupervisorComposition.McpHost;

/// <summary>
/// Real MCP tool surface over <see cref="SupervisorState"/>, following the same shape as
/// <c>Clio10.Mcp.CreatioTools</c> (discovery-free here: a fixed, small tool set for the probe).
/// </summary>
[McpServerToolType]
public sealed class SupervisorTools(SupervisorState state) {
    [McpServerTool(Name = "start-operation", Destructive = true, ReadOnly = false)]
    [Description("Start detached work on a target that outlives this call's response. Returns an opaque operation id.")]
    public async Task<CallToolResult> StartOperationAsync(string target, int workMs, string outcome) {
        string id = await state.StartOperationAsync(target, workMs, outcome);
        return Json(new { id });
    }

    [McpServerTool(Name = "query-operation", ReadOnly = true, Destructive = false)]
    [Description("Truthful state for an operation id: Running, Succeeded, Failed, Unknown, or NotFound.")]
    public CallToolResult QueryOperation(string id) => Json(new { id, state = state.QueryOperation(id) });

    [McpServerTool(Name = "trigger-swap", Destructive = true, ReadOnly = false)]
    [Description("Test-only: gate a real backend replacement on the ledger (reserve-then-drain), then swap. "
        + "For deterministic test timing; not a claim about how a real host decides when to swap.")]
    public async Task<CallToolResult> TriggerSwapAsync(string? target, int drainBudgetSeconds, bool force = false) {
        string result = await state.TriggerSwapAsync(target, TimeSpan.FromSeconds(drainBudgetSeconds), force);
        return Json(new { result, currentBackendPid = state.CurrentBackendPid });
    }

    private static CallToolResult Json(object payload) =>
        new() { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload) }] };
}
