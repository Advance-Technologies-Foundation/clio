using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Relay;
using Clio.Common.McpWorker;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// The WORKER side of the private completion signal: the one notification a sticky worker sends when the
/// long-running operation it was spawned for has actually finished, so the parent may reap it and release
/// the target's configuration-build reservation.
/// </summary>
/// <remarks>
/// <para>
/// <b>The signal is emitted from ONE choke point, never from a tool.</b> It used to be sent by each
/// long-running tool at the single place its happy path ended, which meant every path that returned
/// EARLIER — a validation refusal, a reservation refusal, a failed restart request, <c>waitReady=false</c>
/// — returned without it and stranded the worker for its whole hard lifetime, holding an admission slot
/// and the target's reservation while doing nothing. Patching those return statements would have left the
/// next one to be found the same way, so emission moved to
/// <see cref="RunToolCallAsync{TResult}(global::ModelContextProtocol.Server.McpServer, McpToolExecutionMetadata, Func{Task{TResult}})"/>,
/// which the MCP call-tool filter runs around EVERY tool call. A tool cannot return without passing
/// through it, so there is no longer a branch on which the signal can be forgotten.
/// </para>
/// <para>
/// <b>"Over" versus "still running".</b> The whole point of stickiness is that these families answer the
/// caller with an in-progress envelope at the MCP response deadline and keep working detached, so the tool
/// method returning is NOT the operation ending. The ledger distinguishes the two by counting operations
/// rather than by trusting a tool to say so:
/// <see cref="McpProgressHeartbeat"/> — the ONE mechanism that detaches work in every one of these
/// families — takes a lease (<see cref="BeginOperation"/>) before it starts the work and releases it where
/// the work really ends, including on the past-deadline continuation. The signal is therefore sent when
/// the call has ended AND no leased operation is outstanding: a call that never got as far as starting one
/// is over the moment it returns, and a call that left one running is not.
/// </para>
/// <para>
/// <b>Silent outside a worker, by design.</b> In the ordinary in-process host there is no parent to tell
/// and no slot to return, so no ledger is opened at all and the lease is inert — the parent's path is
/// byte-identical to what it was before this existed.
/// </para>
/// <para>
/// <b>A static facade, for the reason <see cref="McpToolExecutionLock"/> is one.</b> The alternative is a
/// constructor parameter on every long-running tool type, and these tools are constructed by the MCP SDK
/// from the container; the choke point also lives in a static request filter that has no constructor to
/// inject into. There is no runtime configuration to inject: the whole state of this facade is "am I a
/// worker", which <see cref="McpWorkerEnvironment.IsWorkerProcess"/> already answers process-wide.
/// </para>
/// </remarks>
internal static class WorkerOperationCompletionSignal {

	/// <summary>
	/// Exit code reported when the thing being judged threw rather than returning a result.
	/// </summary>
	private const int FailureExitCode = 1;

	/// <summary>
	/// The ledger for the tool call running on THIS async flow, or <see langword="null"/> when the call is
	/// not one that must be signalled.
	/// </summary>
	/// <remarks>
	/// An <see cref="AsyncLocal{T}"/> holding a MUTABLE object rather than a value: the detached
	/// continuation that finishes a past-deadline operation captured the execution context when
	/// <see cref="McpProgressHeartbeat"/> started it, so it observes the very same ledger instance the
	/// (long since returned) call opened. That is what lets the two agree on "exactly once" across the
	/// hand-off without either of them holding a reference to the other.
	/// </remarks>
	private static readonly AsyncLocal<StickyOperationLedger> AmbientLedger = new();

	/// <summary>
	/// Whether a call to the tool described by <paramref name="metadata"/> must end in a completion signal.
	/// </summary>
	/// <param name="metadata">The tool's declared execution metadata, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> for a sticky tool that STARTS an operation.</returns>
	/// <remarks>
	/// <b><c>StartsOperation</c> is not decoration here — dropping it reaps a healthy worker.</b>
	/// <c>compile-status</c> and <c>restart-status</c> are also <c>Lifetime = Sticky</c>, because they must
	/// reach the sticky worker that holds the operation. They start nothing, so a poll opens no ledger; a
	/// predicate keyed on <c>Sticky</c> alone would make every status poll report completion and reap the
	/// worker mid-compile — a worse defect than the one this class removes.
	/// </remarks>
	internal static bool RequiresCompletionSignal(McpToolExecutionMetadata metadata) =>
		metadata is { Lifetime: McpToolExecutionLifetime.Sticky, StartsOperation: true };

	/// <summary>
	/// THE CHOKE POINT. Runs one MCP tool call and guarantees that a sticky operation-starting tool cannot
	/// return — with a result, with a refusal, or by throwing — without the parent being told, unless it
	/// left an operation running.
	/// </summary>
	/// <typeparam name="TResult">The call's result type.</typeparam>
	/// <param name="server">The MCP server the call arrived on; the channel the signal travels back over.</param>
	/// <param name="metadata">The declared execution metadata of the tool being called.</param>
	/// <param name="invoke">The rest of the call.</param>
	/// <returns>Whatever <paramref name="invoke"/> produced.</returns>
	/// <remarks>
	/// <para>
	/// Nothing here changes what the call returns or how it fails: the result and any exception pass
	/// through untouched, and the signal is sent from a <c>finally</c>.
	/// </para>
	/// <para>
	/// <b>It must be the OUTERMOST thing in the filter</b>, not a wrapper around tool execution. The
	/// call-tool filter refuses some calls before any tool runs — an argument-binding diagnostic, a
	/// composite-argument hint, an unreachable routing authority — and by then the parent has already
	/// registered the sticky worker and taken the target's reservation, so such a refusal strands exactly
	/// as a validation refusal inside a tool would.
	/// </para>
	/// </remarks>
	internal static async Task<TResult> RunToolCallAsync<TResult>(
		global::ModelContextProtocol.Server.McpServer server,
		McpToolExecutionMetadata metadata,
		Func<Task<TResult>> invoke) {
		ArgumentNullException.ThrowIfNull(invoke);
		if (!McpWorkerEnvironment.IsWorkerProcess || !RequiresCompletionSignal(metadata)) {
			// Not a worker, or not a call that owns a sticky worker: no ledger, no lease, no signal. This
			// is the ordinary in-process host's whole experience of this class.
			return await invoke().ConfigureAwait(false);
		}
		StickyOperationLedger ledger = new(server, metadata.OperationFamily);
		StickyOperationLedger previous = AmbientLedger.Value;
		AmbientLedger.Value = ledger;
		int callExitCode = FailureExitCode;
		try {
			TResult result = await invoke().ConfigureAwait(false);
			callExitCode = DeriveExitCode(result);
			return result;
		}
		finally {
			AmbientLedger.Value = previous;
			ledger.CallEnded(callExitCode);
		}
	}

	/// <summary>
	/// Leases an operation against the ledger of the tool call running on this async flow, if there is one.
	/// </summary>
	/// <returns>
	/// A lease whose <see cref="WorkerOperationLease.Run{TResult}(Func{TResult})"/> reports the operation
	/// finished; inert when no ledger is open.
	/// </returns>
	/// <remarks>
	/// Taken BEFORE the work is scheduled, never inside it. Scheduling is not instantaneous, and a lease
	/// taken inside the work delegate would leave a window in which the call can end with nothing
	/// outstanding — which reads as "over" and reaps a worker whose operation has not started yet.
	/// </remarks>
	internal static WorkerOperationLease BeginOperation() {
		StickyOperationLedger ledger = AmbientLedger.Value;
		ledger?.OperationStarted();
		return new WorkerOperationLease(ledger);
	}

	/// <summary>
	/// Sends the completion notification. The ledger is its only caller.
	/// </summary>
	/// <param name="server">
	/// The MCP server the tool was invoked on. <see langword="null"/> on the resolve-failure paths and in
	/// unit tests, and treated as "nobody to tell" rather than as an error.
	/// </param>
	/// <param name="family">The operation family whose work has ended.</param>
	/// <param name="exitCode">The exit code the operation finished with.</param>
	/// <remarks>
	/// FIRE AND FORGET, and bounded to that on purpose: this can run in a <c>finally</c> on a detached
	/// continuation, so awaiting it would make the operation's own teardown depend on a pipe write to a
	/// parent that may already be gone. Failing to send is not an error either — the parent's lifetime
	/// bound reaps the worker regardless (AC-04); the signal only makes that prompt.
	/// </remarks>
	internal static void ReportCompleted(global::ModelContextProtocol.Server.McpServer server,
		McpToolOperationFamily family, int exitCode) {
		if (server is null) {
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

	/// <summary>
	/// Reads an exit code out of whatever a call or an operation produced.
	/// </summary>
	/// <remarks>
	/// One derivation shared by both, so a tool never has to hand-carry an exit code to the signal — the
	/// per-tool bookkeeping this replaced is exactly what drifted. It reproduces what the four families
	/// used to pass by hand: their work delegates all return a <see cref="CommandExecutionResult"/> whose
	/// exit code they copied, or a domain result that only exists when the operation succeeded. The value
	/// is diagnostic — the parent logs it and reaps either way.
	/// </remarks>
	private static int DeriveExitCode(object value) =>
		value switch {
			CommandExecutionResult commandResult => commandResult.ExitCode,
			CallToolResult { IsError: true } => FailureExitCode,
			_ => 0
		};

	/// <summary>
	/// A leased operation: the promise that something is still running, and the report that it stopped.
	/// </summary>
	/// <remarks>
	/// A <see langword="struct"/> wrapping a possibly-null ledger rather than a nullable service, so the
	/// call site in <see cref="McpProgressHeartbeat"/> needs no branch and the ordinary in-process host
	/// allocates nothing. It is per-call state, not a service — the same reason the relay's per-call
	/// standard-error drain is a nested private type rather than an injected one (ADR §3.4).
	/// </remarks>
	internal readonly struct WorkerOperationLease {

		private readonly StickyOperationLedger _ledger;

		internal WorkerOperationLease(StickyOperationLedger ledger) {
			_ledger = ledger;
		}

		/// <summary>
		/// Runs the leased operation and reports it finished, whatever it did.
		/// </summary>
		/// <typeparam name="TResult">The operation's result type.</typeparam>
		/// <param name="work">The operation.</param>
		/// <returns>Whatever <paramref name="work"/> produced.</returns>
		internal TResult Run<TResult>(Func<TResult> work) {
			if (_ledger is null) {
				return work();
			}
			int exitCode = FailureExitCode;
			try {
				TResult result = work();
				exitCode = DeriveExitCode(result);
				return result;
			}
			finally {
				_ledger.OperationFinished(exitCode);
			}
		}
	}

	/// <summary>
	/// The per-call bookkeeping behind the guarantee: one signal, sent once, when the call is over and
	/// nothing it started is still running.
	/// </summary>
	internal sealed class StickyOperationLedger {

		private readonly object _gate = new();
		private readonly global::ModelContextProtocol.Server.McpServer _server;
		private readonly McpToolOperationFamily _family;
		private int _outstandingOperations;
		private bool _callEnded;
		private bool _signalled;
		private bool _operationReported;
		private int _operationExitCode;

		internal StickyOperationLedger(global::ModelContextProtocol.Server.McpServer server,
			McpToolOperationFamily family) {
			_server = server;
			_family = family;
		}

		/// <summary>Records that a detachable operation has been leased.</summary>
		internal void OperationStarted() {
			lock (_gate) {
				_outstandingOperations++;
			}
		}

		/// <summary>Records that a leased operation has ended, and signals when it was the last one.</summary>
		/// <param name="exitCode">The exit code the operation ended with.</param>
		internal void OperationFinished(int exitCode) {
			bool send;
			lock (_gate) {
				_outstandingOperations--;
				_operationReported = true;
				_operationExitCode = exitCode;
				// Only the call's own end may authorise the signal: an operation that finishes while the
				// call is still in flight is the ordinary fast path, and that call may yet start another.
				send = TryClaimSignal(_callEnded);
			}
			if (send) {
				ReportCompleted(_server, _family, exitCode);
			}
		}

		/// <summary>
		/// Records that the tool call has returned, and signals unless it left an operation running.
		/// </summary>
		/// <param name="callExitCode">
		/// The outcome of the call itself, used only when the call never started an operation — there is no
		/// operation exit code in that case, and the call's own is the closest true statement available.
		/// </param>
		internal void CallEnded(int callExitCode) {
			bool send;
			int exitCode;
			lock (_gate) {
				_callEnded = true;
				send = TryClaimSignal(_outstandingOperations == 0);
				exitCode = _operationReported ? _operationExitCode : callExitCode;
			}
			if (send) {
				ReportCompleted(_server, _family, exitCode);
			}
		}

		// Both entry points can reach the "over" state concurrently (work finishing in the same instant the
		// deadline fires is the designed-for race), so the claim is made under the gate and exactly one of
		// them wins it.
		private bool TryClaimSignal(bool isOver) {
			if (!isOver || _signalled) {
				return false;
			}
			_signalled = true;
			return true;
		}
	}
}
