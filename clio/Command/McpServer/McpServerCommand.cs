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

		// Delegates captured in locals (not inline lambdas) so they can be detached in
		// `finally` before `cts` is disposed. EOF on stdin makes RunAsync return normally;
		// a still-subscribed ProcessExit handler would otherwise call Cancel() on the
		// already-disposed source and crash the process with ObjectDisposedException
		// during an otherwise-clean exit.
		ConsoleCancelEventHandler onCancelKeyPress = (_, e) => {
			e.Cancel = true;
			RequestShutdown(cts);
		};
		EventHandler onProcessExit = (_, _) => RequestShutdown(cts);

		Console.CancelKeyPress += onCancelKeyPress;
		AppDomain.CurrentDomain.ProcessExit += onProcessExit;
		try {
			server.RunAsync(cts.Token).GetAwaiter().GetResult();
		} catch (OperationCanceledException) {
			// Graceful shutdown — expected when CancellationToken is triggered.
		} finally {
			// Detach the OS-signal handlers before `cts` is disposed so a late
			// signal can no longer reach the disposed source.
			Console.CancelKeyPress -= onCancelKeyPress;
			AppDomain.CurrentDomain.ProcessExit -= onProcessExit;
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

	/// <summary>
	/// Requests graceful shutdown from an OS-signal handler (Ctrl+C or process exit) in a way
	/// that tolerates the source already being disposed.
	/// </summary>
	/// <remarks>
	/// EOF on stdin is a legitimate stdio-transport termination signal: it makes the host loop
	/// return and disposes <paramref name="cancellationTokenSource"/>. A <see cref="AppDomain.ProcessExit"/>
	/// callback can still fire afterwards, and calling <see cref="CancellationTokenSource.Cancel()"/> on the
	/// disposed source would raise an unhandled <see cref="ObjectDisposedException"/> that crashes
	/// the process during an otherwise-clean shutdown. Swallowing it keeps the exit code at 0.
	/// </remarks>
	/// <param name="cancellationTokenSource">The shutdown token source driving the MCP host loop.</param>
	internal static void RequestShutdown(CancellationTokenSource cancellationTokenSource) {
		try {
			cancellationTokenSource.Cancel();
		} catch (ObjectDisposedException) {
			// The host already shut down (EOF teardown disposed the source); nothing to cancel.
		}
	}
}
