using System;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer;

/// <summary>
/// Default <see cref="IMcpExecutionRouter"/>. Reads the tool's declared
/// <see cref="McpToolExecutionAttribute"/> through <see cref="IMcpToolExecutionMetadataReader"/> and turns
/// it into a dispatch decision.
/// </summary>
/// <remarks>
/// <para>
/// Cohort membership is DATA, not a switch (ADR §5, "No feature toggle" — the branch is the test
/// environment, so a toggle defaulting to off would mean the branch's own suites exercise the old path).
/// There is therefore nothing here reading <c>IFeatureToggleService</c>, and no <c>features</c> entry to
/// enable.
/// </para>
/// <para>
/// <b>Three independent conditions must all hold before a call is relayed</b>, and each answers with its
/// own named disposition so a call that stayed in the host says WHY:
/// </para>
/// <list type="number">
/// <item><description>
/// the tool declares <see cref="McpToolExecutionLocation.Worker"/> — otherwise
/// <see cref="McpExecutionDisposition.InProcessByClassification"/>;
/// </description></item>
/// <item><description>
/// it is in the <see cref="IMcpWorkerCohort"/> — otherwise
/// <see cref="McpExecutionDisposition.InProcessOutsideCohort"/>. 153 tools declare <c>Worker</c> because
/// Stage 1 classified every tool with its eventual location, so the declaration alone would move the whole
/// catalog at once, sticky and deploy families included;
/// </description></item>
/// <item><description>
/// this process may spawn workers at all (<see cref="IMcpWorkerPathGate"/>: stdio host, not itself a
/// worker) — otherwise <see cref="McpExecutionDisposition.InProcessTransportGated"/> or
/// <see cref="McpExecutionDisposition.InProcessWorkerRecursionGuard"/>.
/// </description></item>
/// </list>
/// <para>
/// The declared location is reported verbatim in every case, so "the router says worker" and "the call ran
/// in a worker" stay separable rather than collapsing into one unobservable answer.
/// </para>
/// <para>
/// Name resolution (executor unwrap + alias canonicalisation) is NOT re-implemented here — it is delegated
/// to <see cref="IMcpToolExecutionMetadataReader.TryGetMetadata"/> / <c>ResolveRoutingKey</c>, so there is
/// exactly one implementation of it in the process.
/// </para>
/// </remarks>
public sealed class McpExecutionRouter : IMcpExecutionRouter {

	private readonly IMcpToolExecutionMetadataReader _metadataReader;
	private readonly IMcpWorkerCohort _cohort;
	private readonly IMcpWorkerPathGate _workerPathGate;

	// Whether a worker dispatch path exists to route TO. Not a feature toggle: it is not configurable, not
	// runtime-readable, and not persisted — it is a compile-time statement of what is wired. Stage 6 wired
	// the relay, so the constructor DI uses passes true; the false case survives only for the test that
	// proves this router decides rather than always answering the same thing.
	private readonly bool _workerPathWired;

	/// <summary>
	/// Builds the router over the declared execution metadata and the process-level worker-path gate.
	/// Used by DI.
	/// </summary>
	/// <param name="metadataReader">The tool-name → declared execution metadata authority.</param>
	/// <param name="cohort">Which worker-classified tools have a worker path built for them yet.</param>
	/// <param name="workerPathGate">Whether this process may spawn workers at all.</param>
	/// <exception cref="ArgumentNullException">When an argument is <c>null</c>.</exception>
	public McpExecutionRouter(IMcpToolExecutionMetadataReader metadataReader, IMcpWorkerCohort cohort,
		IMcpWorkerPathGate workerPathGate)
		: this(metadataReader, cohort, workerPathGate, workerPathWired: true) {
	}

	/// <summary>
	/// Builds the router with an explicit statement of whether a worker dispatch path exists. Exposed for
	/// tests so the difference between "reports Worker" and "executes in a worker" is assertable rather
	/// than assumed.
	/// </summary>
	/// <param name="metadataReader">The tool-name → declared execution metadata authority.</param>
	/// <param name="cohort">Which worker-classified tools have a worker path built for them yet.</param>
	/// <param name="workerPathGate">Whether this process may spawn workers at all.</param>
	/// <param name="workerPathWired">Whether a worker dispatch path is wired into the dispatch sites.</param>
	/// <exception cref="ArgumentNullException">When an argument is <c>null</c>.</exception>
	internal McpExecutionRouter(IMcpToolExecutionMetadataReader metadataReader, IMcpWorkerCohort cohort,
		IMcpWorkerPathGate workerPathGate, bool workerPathWired) {
		ArgumentNullException.ThrowIfNull(metadataReader);
		ArgumentNullException.ThrowIfNull(cohort);
		ArgumentNullException.ThrowIfNull(workerPathGate);
		_metadataReader = metadataReader;
		_cohort = cohort;
		_workerPathGate = workerPathGate;
		_workerPathWired = workerPathWired;
	}

	/// <inheritdoc />
	public McpExecutionRoute Resolve(string toolName, string innerCommand) {
		string routingKey = _metadataReader.ResolveRoutingKey(toolName, innerCommand);
		if (!_metadataReader.TryGetMetadata(toolName, innerCommand, out McpToolExecutionMetadata metadata)) {
			// Fail-closed. An unclassified name is never relayed: the coverage test keeps every enabled
			// canonical tool classified, so reaching here means the name is not a clio tool at all (or is a
			// synthetic one in a test assembly), and relaying something the metadata does not describe would
			// route on a guess.
			return new McpExecutionRoute(
				routingKey,
				McpToolExecutionLocation.Unspecified,
				McpExecutionDisposition.InProcessUnclassified,
				Metadata: null);
		}
		return new McpExecutionRoute(routingKey, metadata.Location, Decide(metadata.Location, routingKey), metadata);
	}

	// The whole decision, in one place. A Worker-classified tool is reported as Worker-classified whatever
	// happens below; only the DISPOSITION narrows, and it narrows through named reasons rather than a
	// single anonymous "no".
	private McpExecutionDisposition Decide(McpToolExecutionLocation location, string routingKey) {
		if (location != McpToolExecutionLocation.Worker) {
			return McpExecutionDisposition.InProcessByClassification;
		}
		if (!_workerPathWired) {
			return McpExecutionDisposition.InProcessPendingWorkerPath;
		}
		// Cohort membership is checked BEFORE the process gate so the two questions stay independent: a
		// non-cohort tool reads as "not moved yet" on every transport, rather than as "gated" on http and
		// "not moved yet" on stdio, which would make the AC-05 assertion transport-dependent.
		if (!_cohort.Contains(routingKey)) {
			return McpExecutionDisposition.InProcessOutsideCohort;
		}
		return _workerPathGate.Evaluate() switch {
			McpWorkerPathAvailability.Available => McpExecutionDisposition.Worker,
			McpWorkerPathAvailability.ProcessIsWorker => McpExecutionDisposition.InProcessWorkerRecursionGuard,
			_ => McpExecutionDisposition.InProcessTransportGated
		};
	}

	/// <summary>
	/// The single refusal shape every dispatch site returns when the router decides
	/// <see cref="McpExecutionDisposition.Worker"/> but the site has no worker path to hand the call to.
	/// </summary>
	/// <remarks>
	/// Unreachable in a production stdio host, where all three sites now have a dispatcher; it is what a
	/// host reached WITHOUT one answers, and it is deliberately fail-closed rather than a silent
	/// in-process fallthrough: a site that quietly ran a
	/// worker-routed call in the host process would reintroduce the exact wedge this work removes, and
	/// would do it invisibly. Shared rather than hand-rolled per site for the same reason the router itself
	/// is shared — three copies of a refusal drift.
	/// </remarks>
	/// <param name="route">The decision that could not be honoured.</param>
	/// <returns>A structured error result naming the tool and the reason.</returns>
	internal static CallToolResult WorkerPathNotWiredResult(McpExecutionRoute route) =>
		new() {
			IsError = true,
			Content = [
				new TextContentBlock {
					Text = $"Tool '{route?.RoutingKey ?? "<unknown>"}' is routed to a worker process, but this "
						+ "dispatch site has no worker path wired. The call was NOT executed in the host process, "
						+ "because doing so would silently bypass the execution boundary."
				}
			]
		};

	/// <summary>
	/// The refusal returned by the MATCHED dispatch site when the routing authority cannot be resolved from
	/// the request's service provider at all.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The two constructor-injected sites (the unmatched durable handler and the <c>clio-run</c> inner
	/// dispatch) cannot even be CONSTRUCTED without the router, so an absent authority is a build-time
	/// failure there. The matched site is a static delegate, so it service-locates instead — and the whole
	/// point of this result is that the two remaining possibilities stay symmetric: either the authority
	/// answers, or the call is refused. The alternative — continuing in-process — is the exact wedge this
	/// work removes, and it would take it SILENTLY: once the relay is wired, an unresolvable router would
	/// mean "run every worker-cohort tool in the host process" and nothing anywhere would say so.
	/// </para>
	/// <para>
	/// Unreachable in a healthy process: the router is registered on the transport-neutral
	/// <c>BindingsModule.RegisterInto</c> path, so stdio, mcp-http and the per-request tenant containers all
	/// resolve it. Reaching this result therefore means a wiring defect, and it names one.
	/// </para>
	/// <para>
	/// It is a RESULT rather than a log line on purpose: the stdio transport frames JSON-RPC on stdout, the
	/// same stream <c>ConsoleLogger</c> writes to, so "make the absence loud" cannot be done by logging
	/// without corrupting the protocol.
	/// </para>
	/// </remarks>
	/// <param name="toolName">The matched tool the call named, for the refusal text.</param>
	/// <returns>A structured error result naming the missing authority.</returns>
	internal static CallToolResult RoutingAuthorityUnreachableResult(string toolName) =>
		new() {
			IsError = true,
			Content = [
				new TextContentBlock {
					Text = $"Tool '{toolName ?? "<unknown>"}' was NOT executed: the MCP execution-routing "
						+ $"authority ({nameof(IMcpExecutionRouter)}) could not be resolved from this request's "
						+ "service provider, so where the call must execute is unknown. This is a host wiring "
						+ "defect, not a caller error. Running it in the host process anyway would silently "
						+ "bypass the execution boundary, so the call is refused instead."
				}
			]
		};
}
