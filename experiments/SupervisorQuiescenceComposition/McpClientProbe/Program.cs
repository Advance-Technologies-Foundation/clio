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

    // MUTATION A: work longer than the drain budget. The original case used 800ms work against a
    // 20s budget, so the operation always finished BEFORE the kill. Here it cannot.
    CallToolResult startA = await client.CallToolAsync("start-operation",
        new Dictionary<string, object?> { ["target"] = "envA", ["workMs"] = 5000, ["outcome"] = "succeed" });
    string idA = ReadText(startA).GetProperty("id").GetString()!;
    await Task.Delay(150);
    string beforeA = (await QueryAsync(client, idA)).GetProperty("state").GetString()!;
    CallToolResult swapA = await client.CallToolAsync("trigger-swap",
        new Dictionary<string, object?> { ["target"] = "envA", ["drainBudgetSeconds"] = 1 });
    string outcomeA = ReadText(swapA).GetProperty("result").GetString() ?? "";
    Check("MUTATION A: with work genuinely in flight, the swap does not happen at all",
        beforeA == "Running" && outcomeA == "deferred",
        new { stateAtSwapTime = beforeA, swapOutcome = outcomeA,
              note = "the original case only ever swapped an idle backend" });

    // MUTATION B: force the swap anyway, with the lease still outstanding. What does the client get?
    CallToolResult startB = await client.CallToolAsync("start-operation",
        new Dictionary<string, object?> { ["target"] = "envB", ["workMs"] = 5000, ["outcome"] = "succeed" });
    string idB = ReadText(startB).GetProperty("id").GetString()!;
    await Task.Delay(150);
    CallToolResult swapB = await client.CallToolAsync("trigger-swap",
        new Dictionary<string, object?> { ["target"] = "envB", ["drainBudgetSeconds"] = 1, ["force"] = true });
    string outcomeB = ReadText(swapB).GetProperty("result").GetString() ?? "";
    var seen = new List<string>();
    for (int i = 0; i < 8; i++) {
        await Task.Delay(1000);
        seen.Add((await QueryAsync(client, idB)).GetProperty("state").GetString()!);
    }
    string finalB = seen[^1];
    Check("MUTATION B: after a forced swap the orphaned operation is reported truthfully (not an eternal Running)",
        finalB is "Unknown" or "Failed",
        new { swapOutcome = outcomeB, statesOverEightSeconds = seen, finalState = finalB,
              note = "the work was killed with the lease outstanding; Running here means the client waits forever" });

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

static JsonElement ReadText(CallToolResult result) {
    string text = result.Content.OfType<TextContentBlock>().Single().Text;
    using JsonDocument document = JsonDocument.Parse(text);
    return document.RootElement.Clone();
}
