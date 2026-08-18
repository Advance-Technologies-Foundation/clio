using System;
using System.Threading;
using Clio.Command.McpServer.Relay;
using Clio.Common.McpWorker;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// The WORKER side of the private completion signal: the one call a long-running tool makes when its
/// operation has actually finished, so the parent may reap the sticky worker that was holding an
/// admission slot for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Call it where the WORK ends, not where the tool method returns.</b> Every family here answers its
/// caller with an in-progress envelope at the MCP response deadline and keeps running detached; the tool
/// method has long returned by then. The correct call site is therefore the same <c>finally</c> that
/// releases the configuration-build reservation — the innermost one, inside the heartbeat work delegate,
/// which runs on the detached continuation.
/// </para>
/// <para>
/// <b>A static facade, for the reason <see cref="McpToolExecutionLock"/> is one.</b> The alternative is a
/// constructor parameter on every long-running tool type, and these tools are constructed by the MCP SDK
/// from the container; threading one more service through them (and through every fixture that builds
/// them) would be churn for no behavioural gain. There is no runtime configuration to inject: the whole
/// state of this facade is "am I a worker", which
/// <see cref="McpWorkerEnvironment.IsWorkerProcess"/> already answers process-wide.
/// </para>
/// <para>
/// <b>Silent outside a worker, by design.</b> In the ordinary in-process host there is no parent to tell
/// and no slot to return, so the call is a no-op — which is what lets the call site be unconditional and
/// therefore impossible to forget on one branch.
/// </para>
/// </remarks>
internal static class WorkerOperationCompletionSignal {

	/// <summary>
	/// Tells the parent that this worker's long-running operation has ended.
	/// </summary>
	/// <param name="server">
	/// The MCP server the tool was invoked on. <see langword="null"/> on the resolve-failure paths and in
	/// unit tests, and treated as "nobody to tell" rather than as an error.
	/// </param>
	/// <param name="family">The operation family whose work has ended.</param>
	/// <param name="exitCode">The exit code the operation finished with.</param>
	/// <remarks>
	/// FIRE AND FORGET, and bounded to that on purpose: this runs in a <c>finally</c> on a detached
	/// continuation, so awaiting it would make the operation's own teardown depend on a pipe write to a
	/// parent that may already be gone. Failing to send is not an error either — the parent's lifetime
	/// bound reaps the worker regardless (AC-04); the signal only makes that prompt.
	/// </remarks>
	internal static void ReportCompleted(global::ModelContextProtocol.Server.McpServer server,
		McpToolOperationFamily family, int exitCode) {
		if (server is null || !McpWorkerEnvironment.IsWorkerProcess) {
			return;
		}
		try {
			_ = server.SendNotificationAsync(
				WorkerOperationSignalContract.NotificationMethod,
				WorkerOperationSignalContract.BuildParams(family, exitCode),
				cancellationToken: CancellationToken.None);
		}
		catch (Exception) {
			// A worker that cannot tell its parent it has finished must still finish. The parent's sticky
			// lifetime bound is what guarantees the slot comes back either way, so there is nothing here
			// worth failing the operation — or worth writing to a standard error the parent parses.
		}
	}
}
