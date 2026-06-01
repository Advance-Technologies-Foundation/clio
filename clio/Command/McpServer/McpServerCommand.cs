using System;
using System.Threading;
using Clio.Command.McpServer.Tools;
using CommandLine;

namespace Clio.Command.McpServer;


[Verb("mcp-server", Aliases = ["mcp"], HelpText = "Starts mcp server in stdio mode")]
public class McpServerCommandOptions : BaseCommandOptions
{ }


public class McpServerCommand(ModelContextProtocol.Server.McpServer server) : Command<McpServerCommandOptions>{
	public override int Execute(McpServerCommandOptions options) {
		McpLogNotifier.Initialize(server);
		using var cts = new CancellationTokenSource();
		Console.CancelKeyPress += (_, e) => {
			e.Cancel = true;
			cts.Cancel();
		};
		AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();
		try {
			server.RunAsync(cts.Token).GetAwaiter().GetResult();
		} catch (OperationCanceledException) {
			// Graceful shutdown — expected when CancellationToken is triggered.
		} finally {
			// Flush any in-flight background CDN refreshes before the process
			// exits. Without this, the fire-and-forget Task.Run tasks are killed
			// by the runtime as soon as the main (foreground) thread exits,
			// leaving the on-disk cache stale indefinitely.
			ComponentRegistryClient.DrainAsync(TimeSpan.FromSeconds(10))
				.GetAwaiter().GetResult();
			McpLogNotifier.Reset();
		}
		return 0;
	}
}
