using Clio10.SupervisorComposition.McpHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// A real MCP server, stdio transport, over the supervisor+ledger logic S1-S10 already measure directly.
// Exists so a real MCP client (not this probe's synthetic line protocol) can exercise operation
// correlation across a real backend swap -- kirillkrylov's "a surviving synthetic pipe is not this
// proof", and Alexandr's five requirements for a shared client harness.
if (args.Length < 2) { Console.Error.WriteLine("usage: mcphost <backendDllPath> <workDir>"); return 2; }
string backendDllPath = args[0];
string workDir = args[1];
Directory.CreateDirectory(workDir);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
builder.Logging.ClearProviders(); // stdout carries MCP protocol traffic only
builder.Services.AddSingleton(new SupervisorState(backendDllPath, workDir));
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<SupervisorTools>();
using var host = builder.Build();
await host.RunAsync();
return 0;
