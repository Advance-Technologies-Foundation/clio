using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.McpWorker;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// Sends a call to a sticky worker that ALREADY EXISTS, without going anywhere near admission.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a separate component for one structural reason, and it is the whole mechanism.</b> ADR
/// §3.2c's binding rule — admission governs CREATING a worker, never TALKING to one that already exists —
/// cannot be enforced by a comment on a class that also holds
/// <see cref="IWorkerProcessSupervisor"/>. An implementer who routed a <c>compile-status</c> poll through
/// <see cref="IWorkerProcessSupervisor.SpawnContainedAsync"/> would satisfy every word of "the poll
/// reaches the same worker" and would ship a deadlock: the slot the poll waits for is HELD BY THE VERY
/// WORKER it is reaching, which is hold-and-wait rather than starvation and does not resolve under load.
/// This type is injected with <see cref="IWorkerReach"/> and nothing else that can create a process, so
/// routing a poll through admission stops being a mistake somebody can make and becomes code that does
/// not compile.
/// </para>
/// <para>
/// <b>It reuses the worker's OPEN relay session; it never builds a second transport.</b>
/// <see cref="IWorkerMcpRelay.OpenAsync"/> states the constraint: the session becomes the transport's
/// ONLY consumer, because <c>ITransport.MessageReader</c> is a channel reader and a second consumer
/// steals messages. A poll that attached its own transport to the same standard output would pass
/// against a fixture worker and eat protocol frames in production.
/// <see cref="IWorkerReach.ReachExisting"/> is used for what it is for — asking whether the process is
/// still alive — not as a way to obtain streams.
/// </para>
/// </remarks>
public interface IStickyWorkerPoll {

	/// <summary>
	/// Sends one call to the live sticky worker for <paramref name="key"/>.
	/// </summary>
	/// <param name="key">The sticky worker to reach.</param>
	/// <param name="parameters">The call to send, already stripped of the parent session's own metadata.</param>
	/// <param name="budget">How long to wait for the worker's answer.</param>
	/// <param name="cancellationToken">Caller cancellation.</param>
	/// <returns>
	/// The worker's result, or <see langword="null"/> when there is no live sticky worker for that key —
	/// which the caller must treat as "nothing was reached", never as "the worker answered nothing".
	/// </returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
	/// <remarks>
	/// <b>A cancelled poll decides the worker's SESSION, and that is a different thing from its
	/// operation.</b> The caller's cancellation is always rethrown and never ends an operation by itself.
	/// But the SDK's send lock guarantees a completed send rather than an atomic one, so a token firing
	/// mid-send can leave an unterminated line on the child's stdin (ADR §3.2a): a session in that state is
	/// RETIRED — the worker is reaped, which also ends its operation, because the lease is what kills the
	/// process. A session whose request was fully written is kept, and the next call proves it with a
	/// bounded liveness probe before reusing it.
	/// </remarks>
	ValueTask<CallToolResult> ReachAndCallAsync(StickyWorkerKey key, CallToolRequestParams parameters,
		TimeSpan budget, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IStickyWorkerPoll"/>
public sealed class StickyWorkerPoll : IStickyWorkerPoll {

	private readonly IWorkerReach _reach;
	private readonly IStickyWorkerRegistry _registry;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="StickyWorkerPoll"/> class.
	/// </summary>
	/// <param name="reach">
	/// The NARROW supervisor surface: it can hand back a non-owning channel to a worker that already
	/// exists and has no member that acquires an admission slot. Taking
	/// <see cref="IWorkerProcessSupervisor"/> here instead would re-open ADR §3.2c's cycle.
	/// </param>
	/// <param name="registry">The parent's record of the sticky workers it supervises.</param>
	/// <param name="logger">Host logger.</param>
	/// <exception cref="ArgumentNullException">A dependency is missing.</exception>
	public StickyWorkerPoll(IWorkerReach reach, IStickyWorkerRegistry registry, ILogger logger) {
		_reach = reach ?? throw new ArgumentNullException(nameof(reach));
		_registry = registry ?? throw new ArgumentNullException(nameof(registry));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <inheritdoc/>
	public async ValueTask<CallToolResult> ReachAndCallAsync(StickyWorkerKey key,
		CallToolRequestParams parameters, TimeSpan budget, CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(parameters);
		if (!_registry.TryReach(key, out StickyWorkerEntry entry)) {
			return null;
		}
		// Costs no admission and never waits — which is also why the prototype could measure a
		// 0.00–0.02 s compile-status poll. A poll that queued for a slot could not be that fast.
		IWorkerChannel channel = _reach.ReachExisting(entry.Lease);
		if (channel.HasExited) {
			// Reaching is not an aliveness assertion: the worker may exit between the registry lookup and
			// this read. That race is answered HERE rather than by a reach that throws, because the caller
			// has one sensible response to it and it is the same as "there was no worker".
			await _registry.ReapAsync(key, entry).ConfigureAwait(false);
			return null;
		}
		using CancellationTokenSource budgetSource =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		budgetSource.CancelAfter(budget);
		try {
			if (entry.RequiresLivenessProof) {
				// The call before this one was abandoned by its caller AFTER its request had been written, so
				// the worker was told to stop through `notifications/cancelled`. The transport is whole — that
				// is what makes this session reusable at all — but a worker that ignores the notification is
				// still executing the abandoned call, and reusing it would put a second request on a session
				// whose first one is still in flight. BOUNDED, and the bound is the point: the one worker
				// state this asks about is a worker whose pipe is open and which answers nothing, so an
				// unbounded proof would hang on exactly the worker it exists to catch.
				if (!await entry.Session.ProbeLivenessAsync(budgetSource.Token).ConfigureAwait(false)) {
					_logger.WriteWarning(
						$"A sticky MCP worker for operation family '{key.Family}' did not answer the liveness "
						+ "probe that follows an abandoned call and was reaped.");
					await _registry.ReapAsync(key, entry).ConfigureAwait(false);
					return null;
				}
				entry.MarkProvedAlive();
			}
			CallToolResult result =
				await entry.Session.CallToolAsync(parameters, budgetSource.Token).ConfigureAwait(false);
			if (entry.IsCompleted) {
				// The linger exists so THIS poll can be answered out of the process that holds the operation
				// record; once it has been, nobody needs the worker and holding its admission slot for the
				// rest of the window would refuse the next long operation on a host whose sticky capacity is
				// small — one or two on the machines this actually runs on. The window stays as the backstop
				// for a caller that never polls at all. Reaped by ENTRY: a starter may have superseded this
				// finished worker between the send and the answer, and reaping by key alone would end the
				// operation that just replaced it.
				await _registry.ReapAsync(key, entry).ConfigureAwait(false);
			}
			return result;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			// The CALLER gave up, and the OPERATION is not what that decides: a client that stopped waiting
			// for a STATUS answer is not a reason to end the compile that status was about. The SESSION is a
			// different question, and it is DECIDED here rather than assumed (ADR §3.2a). The SDK's send lock
			// guarantees a COMPLETED send, not an atomic one — it takes this very token for the payload write,
			// the newline write and the flush — so a token firing between them releases that lock over an
			// unterminated line on the child's stdin, and the next writer's JSON is appended to it. The relay
			// already knows which of the two happened: an incomplete send sets the session's closure BEFORE it
			// rethrows, so this needs no probe and no I/O to tell them apart.
			if (entry.Session.IsRetired) {
				// Retired means never written to again, so this worker can no longer be reached at all —
				// leaving it registered would hand the next poll a transport that may hold half a frame, and
				// leaving it unreaped would hold its admission slot and its shared-resource reservation until
				// expiry for a process nothing can talk to. Reaping ends the operation as well, because the
				// lease is what kills the process; that is the price of §3.2a and it is paid deliberately.
				// By ENTRY, never by key: a starter may have superseded this worker between the send and now.
				_logger.WriteWarning(
					$"A sticky MCP worker for operation family '{key.Family}' was retired: its call was "
					+ "cancelled before the request had been written, so its transport may hold a partial "
					+ "JSON-RPC frame and the worker was reaped rather than reused.");
				await _registry.ReapAsync(key, entry).ConfigureAwait(false);
			}
			else {
				// The request WAS written, so the transport is whole and the worker is left alone. It is not
				// yet reusable though: it still has the abandoned call, so the next poll proves it first.
				entry.MarkCallAbandoned();
			}
			throw;
		}
		catch (Exception exception) {
			// The session is the only way to talk to this worker, so a failed send means the worker is no
			// longer reachable through it. Reap and answer "nothing was reached": the caller then takes the
			// ordinary per-call path, which is exactly what it would have done had the parent restarted.
			_logger.WriteWarning(
				$"A sticky MCP worker for operation family '{key.Family}' could not be reached and was "
				+ $"reaped: {SensitiveErrorTextRedactor.Redact(exception.Message)}");
			await _registry.ReapAsync(key, entry).ConfigureAwait(false);
			return null;
		}
	}
}
