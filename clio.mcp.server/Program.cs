using System.Runtime.InteropServices;
using System.Text.Json;

namespace Clio.McpServer;

internal static class Program {
	private static async Task<int> Main() {
		ApplyOptionalSettingsHomeOverride();

		JsonSerializerOptions jsonOptions = new() {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = false
		};

		ClioFacade facade = new();
		McpToolRegistry toolRegistry = new(facade, jsonOptions);
		StdioJsonRpcTransport transport = new(jsonOptions);
		McpServer server = new(toolRegistry, transport, jsonOptions);

		await server.RunAsync(CancellationToken.None);
		return 0;
	}

	private static void ApplyOptionalSettingsHomeOverride() {
		string? overrideRoot = Environment.GetEnvironmentVariable("CLIO_MCP_HOME");
		if (string.IsNullOrWhiteSpace(overrideRoot)) {
			return;
		}

		string rootVariable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "LOCALAPPDATA" : "HOME";
		Environment.SetEnvironmentVariable(rootVariable, overrideRoot);
	}
}
