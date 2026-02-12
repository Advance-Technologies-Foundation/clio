using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace Clio.McpServer;

internal static class Program {
	private static async Task Main(string[] args) {
		ApplyOptionalSettingsHomeOverride();

		HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
		builder.Logging.AddConsole(options => {
			options.LogToStandardErrorThreshold = LogLevel.Trace;
		});

		builder.Services.AddSingleton<ClioFacade>();
		builder.Services
			.AddMcpServer()
			.WithStdioServerTransport()
			.WithToolsFromAssembly();

		await builder.Build().RunAsync();
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
