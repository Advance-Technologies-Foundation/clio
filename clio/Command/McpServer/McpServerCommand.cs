using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Common.Telemetry;
using CommandLine;

namespace Clio.Command.McpServer;


[Verb("mcp-server", Aliases = ["mcp"], HelpText = "Starts mcp server in stdio mode")]
public class McpServerCommandOptions : BaseCommandOptions
{ }


public class McpServerCommand(ModelContextProtocol.Server.McpServer server,
	ITelemetryFlushScheduler flushScheduler) : Command<McpServerCommandOptions>{
	public override int Execute(McpServerCommandOptions options) {
		McpLogNotifier.Initialize(server);
		using var cts = new CancellationTokenSource();
		Console.CancelKeyPress += (_, e) => {
			e.Cancel = true;
			cts.Cancel();
		};
		AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();
		// Drain the telemetry spool left over from previous sessions; fire-and-forget,
		// the server starts serving immediately.
		flushScheduler.TryScheduleFlush();
		try {
			server.RunAsync(cts.Token).GetAwaiter().GetResult();
		} catch (OperationCanceledException) {
			// Graceful shutdown — expected when CancellationToken is triggered.
		} finally {
			// Flush any in-flight background work (CDN refreshes, telemetry uploads)
			// before the process exits. Without this, the fire-and-forget Task.Run
			// tasks are killed by the runtime as soon as the main (foreground)
			// thread exits, leaving the on-disk cache stale indefinitely. The two
			// drains run concurrently so shutdown stays bounded at ~10 seconds.
			Task.WhenAll(
					ComponentRegistryClient.DrainAsync(TimeSpan.FromSeconds(10)),
					flushScheduler.DrainAsync(TimeSpan.FromSeconds(10)))
				.GetAwaiter().GetResult();
			McpLogNotifier.Reset();
		}
		return 0;
	}
}
