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
		// The using-scoped source is disposed at the END of Execute — strictly after the finally
		// block has detached the handlers and drained. Do not narrow this scope or dispose earlier:
		// the detach-before-dispose ordering is precisely what keeps a late signal off the disposed
		// source.
		using var cts = new CancellationTokenSource();

		// Capture the signal handlers in locals (rather than inline lambdas) so the finally
		// block can detach them before the cancellation source is disposed. When standard
		// input reaches end of file the host loop returns normally, yet a still-subscribed
		// process-exit handler could cancel the already-disposed source and crash an
		// otherwise-clean exit with an unhandled ObjectDisposedException.
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
			// Ctrl+C / ProcessExit path: the triggered token makes RunAsync throw here. A plain
			// stdin EOF instead returns from RunAsync normally, without throwing.
		} finally {
			// Detach the OS-signal handlers before the cancellation source is disposed so a
			// late signal can no longer reach the disposed source. This unsubscribe is the
			// deterministic fix for the EOF/ProcessExit race; the guard inside RequestShutdown
			// is only a defense-in-depth net for the residual concurrent-teardown window.
			// Detaching the Ctrl+C handler here also means a second Ctrl+C during the drain
			// below is no longer intercepted and will hard-kill the process — intended, so a
			// stuck drain stays interruptible.
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
	/// <see cref="CancellationTokenSource.Cancel()"/> also runs the host's cancellation callbacks
	/// synchronously, so a callback that throws during teardown surfaces as an
	/// <see cref="AggregateException"/>; that is swallowed for the same reason, since the process is
	/// already terminating and the fault is not actionable.
	/// </remarks>
	/// <param name="cancellationTokenSource">The shutdown token source driving the MCP host loop.</param>
	internal static void RequestShutdown(CancellationTokenSource cancellationTokenSource) {
		try {
			cancellationTokenSource.Cancel();
		} catch (ObjectDisposedException) {
			// The host already shut down (EOF teardown disposed the source); nothing to cancel.
		} catch (AggregateException) {
			// Cancel() runs the host's cancellation callbacks synchronously; a callback that throws
			// during teardown surfaces here. Swallow it so the shutdown signal still exits cleanly —
			// the process is already terminating, mirroring the disposed-source case above.
		}
	}
}
