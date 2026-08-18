namespace Clio.Command.McpServer;

/// <summary>
/// What a dispatch site must actually do with a call, once the router has read the tool's declared
/// execution metadata.
/// </summary>
/// <remarks>
/// The distinction between <see cref="InProcessPendingWorkerPath"/> and <see cref="Worker"/> is the whole
/// point of this enum: a tool declared <see cref="McpToolExecutionLocation.Worker"/> is REPORTED as such by
/// the router today, but still EXECUTES in the host process because no worker path is wired into dispatch
/// yet (Stage 6). Collapsing the two would make a router that decides nothing indistinguishable from one
/// that decides correctly.
/// </remarks>
public enum McpExecutionDisposition {

	/// <summary>
	/// The tool declares <see cref="McpToolExecutionLocation.InProcess"/>: it runs in the host process and
	/// will keep doing so after the worker path exists.
	/// </summary>
	InProcessByClassification,

	/// <summary>
	/// The tool declares <see cref="McpToolExecutionLocation.Worker"/>, but this router was built with no
	/// worker path to route to. Production no longer produces this (Stage 6 wired the relay); it survives
	/// as the shape a router with no destination answers, which is what keeps "reports Worker" and
	/// "executes in a worker" separable in a test.
	/// </summary>
	InProcessPendingWorkerPath,

	/// <summary>
	/// The tool declares <see cref="McpToolExecutionLocation.Worker"/> and a worker path exists, but the
	/// tool is not in <see cref="McpWorkerCohort"/> — its supervision (sticky lifetime, terminal-stage
	/// bounding, per-family reservations) is later-stage work that does not exist yet. The declaration is
	/// still reported verbatim, so "classified worker" and "moved to a worker" stay distinguishable, which
	/// is exactly what AC-05 asserts.
	/// </summary>
	InProcessOutsideCohort,

	/// <summary>
	/// A cohort tool that would have been relayed, refused because this process may not spawn workers at
	/// all: it serves a transport other than stdio, so the credential channel a child would need does not
	/// exist (Stage 5 deferred). See <see cref="IMcpWorkerPathGate"/>.
	/// </summary>
	InProcessTransportGated,

	/// <summary>
	/// A cohort tool that would have been relayed, refused because THIS process is already a worker.
	/// Relaying would hand the child the very call the parent relayed and spawn workers without end.
	/// </summary>
	InProcessWorkerRecursionGuard,

	/// <summary>
	/// The routing key resolved to no declared execution metadata at all (an unknown name, or a synthetic
	/// tool in a test assembly). Fail-closed: an unclassified call is never relayed.
	/// </summary>
	InProcessUnclassified,

	/// <summary>
	/// The call is relayed to a supervised child worker. Produced for a cohort tool
	/// (<see cref="McpWorkerCohort"/>) in a stdio host that is not itself a worker; a dispatch site with no
	/// worker dispatcher to hand it to refuses the call rather than executing it silently in-process.
	/// </summary>
	Worker
}

/// <summary>
/// One routing decision: which canonical tool a call names, where that tool is DECLARED to execute, and
/// what the dispatch site must therefore do right now.
/// </summary>
/// <param name="RoutingKey">
/// The canonical tool name the decision was made for — after unwrapping <c>clio-run</c> and canonicalising
/// a deprecated alias. <c>null</c> only when the caller supplied no tool name at all.
/// </param>
/// <param name="DeclaredLocation">
/// The <see cref="McpToolExecutionLocation"/> the tool declares, verbatim. Reported even when the call
/// executes in-process anyway, so "the router says worker" and "the call ran in the host" stay separable.
/// </param>
/// <param name="Disposition">What the dispatch site must do with this call.</param>
/// <param name="Metadata">
/// The full declared metadata row, or <c>null</c> when the routing key carries none. Stage 6 reads
/// <c>BudgetPolicy</c> / <c>Lifetime</c> / <c>OperationFamily</c> off it; nothing does today.
/// </param>
public sealed record McpExecutionRoute(
	string RoutingKey,
	McpToolExecutionLocation DeclaredLocation,
	McpExecutionDisposition Disposition,
	McpToolExecutionMetadata Metadata) {

	/// <summary>
	/// <c>true</c> when the dispatch site must run the call in the host process — every disposition except
	/// <see cref="McpExecutionDisposition.Worker"/>.
	/// </summary>
	public bool ExecutesInProcess => Disposition != McpExecutionDisposition.Worker;
}

/// <summary>
/// The single authority answering "where does this call execute", for every MCP dispatch site.
/// </summary>
/// <remarks>
/// <para>
/// There are THREE dispatch sites, and they must agree — a tool reached as a matched name, through a
/// deprecated alias (unmatched, via <see cref="IMcpDurableCallToolHandler"/>), and through
/// <c>clio-run</c> is ONE tool and must route to ONE place. Duplicating the rule into each site is exactly
/// the drift <c>spec/adr/adr-mcp-worker-execution-boundary.md</c> §9 exists to prevent, which is why this
/// mirrors <see cref="McpReadDeadlineGate"/>: one authority, resolved by canonical tool name, called from
/// every site rather than reimplemented in any of them.
/// </para>
/// <para>
/// The router is a PURE DECISION: it reads declared metadata and answers. It never dispatches, never
/// spawns anything, and holds no state. The dispatch site acts on the answer.
/// </para>
/// <para>
/// The resolved name is PASSED IN rather than read off the <c>RequestContext</c>, because
/// <c>ClioRunExecutor.DispatchAsync</c> mutates that context in place while a call is running, and because
/// at the matched-filter seam the canonical name lives on the matched primitive rather than in the params.
/// </para>
/// </remarks>
public interface IMcpExecutionRouter {

	/// <summary>
	/// Decides where a call executes.
	/// </summary>
	/// <param name="toolName">
	/// The tool name the call arrived under. May be a deprecated alias, or one of the generic executors
	/// (<c>clio-run</c> / <c>clio-run-destructive</c>).
	/// </param>
	/// <param name="innerCommand">
	/// The <c>command</c> argument when <paramref name="toolName"/> is a generic executor, so the decision
	/// keys on the UNWRAPPED inner tool (ADR rule 7); <c>null</c> for a direct call. Ignored when
	/// <paramref name="toolName"/> is not an executor.
	/// </param>
	/// <returns>
	/// The routing decision. Never <c>null</c>: an unknown or unclassified name resolves to
	/// <see cref="McpExecutionDisposition.InProcessUnclassified"/> rather than throwing, so a routing
	/// question can never break a call that would otherwise have worked.
	/// </returns>
	McpExecutionRoute Resolve(string toolName, string innerCommand);
}
