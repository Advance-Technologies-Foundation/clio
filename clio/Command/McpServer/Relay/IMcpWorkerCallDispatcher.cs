using System;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// Executes one MCP <c>tools/call</c> in a short-lived child worker and returns its answer — the
/// destination the routing authority has been deciding for since Stage 4b.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type CONNECTS three components; it does not reimplement any of them.</b>
/// <c>IWorkerProcessSupervisor</c> owns process creation, platform containment, the concurrency cap and
/// the kill; <see cref="IWorkerMcpRelay"/> owns the child's transport read loop and the handshake;
/// <see cref="IMcpExecutionRouter"/> owns the decision. What is left, and all that lives here, is the
/// lease/relay lifecycle of a single call: spawn, attach, hand the call over verbatim, bound it, and tear
/// down in an order that does not lose the answer.
/// </para>
/// <para>
/// <b>The call is relayed VERBATIM.</b> Whatever <see cref="CallToolRequestParams"/> the dispatch site
/// received is what the child receives, <c>_meta</c> object included — a rebuilt <c>_meta</c> re-issues a
/// progress token of the parent's own making and makes ClioRing drop every stage event of the run, and its
/// correlation failure is silent. This is also why the <c>clio-run</c> site relays its OWN params rather
/// than the unwrapped inner call: the child's <c>clio-run</c> executes any tool directly, so an unwrapped
/// relay would only add a rebuild step that can lose arguments.
/// </para>
/// <para>
/// <b>The budget clock starts at SPAWN, never at admission</b> (ADR §2.4, story 2 AC-07). The deadline is
/// read off <c>IWorkerLease.BudgetExpiresAtUtc</c>, which the supervisor anchors on the moment the process
/// was created. At concurrency width 16 on a four-core box a perfectly healthy call waited 16.9 s just to
/// reach <c>initialize</c>; a budget measured from admission would have killed it for being queued, which
/// is a failure mode this fix would otherwise have invented.
/// </para>
/// </remarks>
public interface IMcpWorkerCallDispatcher {

	/// <summary>
	/// Runs one routed call in a worker.
	/// </summary>
	/// <param name="route">
	/// The routing decision. Must carry <see cref="McpExecutionDisposition.Worker"/>: this method executes
	/// the decision and never re-makes it, so handing it an in-process route is a caller defect.
	/// </param>
	/// <param name="parameters">The call parameters, relayed to the worker unchanged.</param>
	/// <param name="parentSession">
	/// The parent leg — the live MCP session the REAL client is connected to, so the child's sampling
	/// requests and notifications reach that client rather than being answered by the parent. A dispatch
	/// site builds it with <see cref="McpServerParentSession"/> over its request context's server.
	/// </param>
	/// <param name="cancellationToken">
	/// The caller's token. Cancelling it kills the worker; it is reported as cancellation, never as a
	/// budget expiry, so a client that gave up and a stand that never answered stay distinguishable.
	/// </param>
	/// <returns>
	/// The worker's <see cref="CallToolResult"/>, or a structured bounded error when the budget expired or
	/// the relay failed. Never <see langword="null"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException">A required argument is missing.</exception>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
	ValueTask<CallToolResult> DispatchAsync(
		McpExecutionRoute route,
		CallToolRequestParams parameters,
		IParentMcpSession parentSession,
		CancellationToken cancellationToken);
}
