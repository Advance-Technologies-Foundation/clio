using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// Real MCP client, real MCP wire protocol, over stdio to a real McpHost process -- the requirement
// kirillkrylov and Alexandr both named ("a surviving synthetic pipe is not this proof"; "the client
// must be a real MCP SDK client, not a hand-rolled JSON-RPC writer"). One connection, opened once,
// never reconnected, per the "we do not control how people use their clients" position this thread
// converged on.
if (args.Length < 2) { Console.Error.WriteLine("usage: mcpclientprobe <mcpHostDllPath> <backendDllPath>"); return 2; }
string mcpHostDllPath = args[0];
string backendDllPath = args[1];
string workDir = Directory.CreateTempSubdirectory("mcp-client-probe-").FullName;
var observations = new List<object>();
bool failed = false;
void Check(string name, bool ok, object detail) {
    observations.Add(new { name, passed = ok, detail });
    if (!ok) failed = true;
}

var hostStart = new ProcessStartInfo("dotnet") {
    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
    UseShellExecute = false,
};
hostStart.ArgumentList.Add(mcpHostDllPath);
hostStart.ArgumentList.Add(backendDllPath);
hostStart.ArgumentList.Add(workDir);
using Process hostProcess = Process.Start(hostStart) ?? throw new InvalidOperationException("McpHost did not start");
try {
    // One connection for the whole scenario -- opened once, used through a real swap, never reconnected.
    await using McpClient client = await McpClient.CreateAsync(
        new StreamClientTransport(hostProcess.StandardInput.BaseStream, hostProcess.StandardOutput.BaseStream));

    // Start work that outlives this call's response, exactly the create-app-section shape.
    CallToolResult startResult = await client.CallToolAsync("start-operation",
        new Dictionary<string, object?> { ["target"] = "envA", ["workMs"] = 800, ["outcome"] = "succeed" });
    string id = ReadText(startResult).GetProperty("id").GetString()!; // opaque: read once, never parsed further

    // Query while still running -- state 1 of the three-outcome requirement.
    await Task.Delay(150);
    JsonElement earlyQuery = await QueryAsync(client, id);
    Check("early query, before the swap, reports Running",
        earlyQuery.GetProperty("state").GetString() == "Running",
        new { state = earlyQuery.GetProperty("state").GetString() });

    // Trigger a real backend swap -- gated on the same reserve-then-drain primitive S2/S4/S6-S9 use,
    // now reached through a real MCP tool call rather than this probe's own function calls.
    CallToolResult swapResult = await client.CallToolAsync("trigger-swap",
        new Dictionary<string, object?> { ["target"] = "envA", ["drainBudgetSeconds"] = 20 });
    JsonElement swapPayload = ReadText(swapResult);
    string swapOutcome = swapPayload.GetProperty("result").GetString() ?? "";
    int newPid = swapPayload.GetProperty("currentBackendPid").GetInt32();
    Check("trigger-swap over MCP reports a real V1->V2 PID change, not a deferral",
        swapOutcome.StartsWith("swapped ", StringComparison.Ordinal),
        new { swapOutcome, newPid });

    // Query again, SAME connection, SAME opaque id, after a real process was killed and replaced
    // underneath the operation this id refers to.
    JsonElement postSwapQuery = await QueryAsync(client, id);
    Check("post-swap query, same connection, same id, reports Succeeded -- correlation survived a real swap",
        postSwapQuery.GetProperty("state").GetString() == "Succeeded",
        new { state = postSwapQuery.GetProperty("state").GetString() });

    // A second operation on the new backend, through the same connection, to show the new backend
    // is genuinely serving -- not just that the old id's record survived in the ledger.
    CallToolResult secondStart = await client.CallToolAsync("start-operation",
        new Dictionary<string, object?> { ["target"] = "envA", ["workMs"] = 50, ["outcome"] = "succeed" });
    string secondId = ReadText(secondStart).GetProperty("id").GetString()!;
    JsonElement secondTerminal = await WaitTerminalAsync(client, secondId, TimeSpan.FromSeconds(10));
    Check("a new operation on the swapped-to backend succeeds, same connection throughout",
        secondTerminal.GetProperty("state").GetString() == "Succeeded",
        new { state = secondTerminal.GetProperty("state").GetString() });

    Console.WriteLine(JsonSerializer.Serialize(new {
        transport = "real MCP client, StreamClientTransport, one connection, never reconnected",
        cases = observations
    }, new JsonSerializerOptions { WriteIndented = true }));
}
finally {
    if (!hostProcess.HasExited) { try { hostProcess.Kill(entireProcessTree: true); } catch { /* best effort */ } }
}
return failed ? 1 : 0;

static async Task<JsonElement> QueryAsync(McpClient client, string id) {
    CallToolResult result = await client.CallToolAsync("query-operation", new Dictionary<string, object?> { ["id"] = id });
    return ReadText(result);
}

static async Task<JsonElement> WaitTerminalAsync(McpClient client, string id, TimeSpan budget) {
    DateTime deadline = DateTime.UtcNow + budget;
    while (DateTime.UtcNow < deadline) {
        JsonElement result = await QueryAsync(client, id);
        string state = result.GetProperty("state").GetString()!;
        if (state is not "Running") return result;
        await Task.Delay(25);
    }
    throw new TimeoutException($"operation {id} did not reach a terminal state in time");
}

static JsonElement ReadText(CallToolResult result) {
    string text = result.Content.OfType<TextContentBlock>().Single().Text;
    using JsonDocument document = JsonDocument.Parse(text);
    return document.RootElement.Clone();
}
