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
/// Cohort membership is DATA, not a switch: a tool routes to a worker because its <c>Location</c> metadata
/// says <c>Worker</c> (ADR §5, "No feature toggle" — the branch is the test environment, so a toggle
/// defaulting to off would mean the branch's own suites exercise the old path). There is therefore nothing
/// here reading <c>IFeatureToggleService</c>, and no <c>features</c> entry to enable.
/// </para>
/// <para>
/// What keeps behaviour identical to before this type existed is not a flag but the ABSENCE OF A
/// DESTINATION: <see cref="_workerPathWired"/> is <c>false</c> in the constructor DI uses, so every call
/// resolves to one of the in-process dispositions and every dispatch site takes its in-process branch. The
/// declared location is still reported, so the decision is observable before it is actionable.
/// </para>
/// <para>
/// Name resolution (executor unwrap + alias canonicalisation) is NOT re-implemented here — it is delegated
/// to <see cref="IMcpToolExecutionMetadataReader.TryGetMetadata"/> / <c>ResolveRoutingKey</c>, so there is
/// exactly one implementation of it in the process.
/// </para>
/// </remarks>
public sealed class McpExecutionRouter : IMcpExecutionRouter {

	private readonly IMcpToolExecutionMetadataReader _metadataReader;

	// Whether a worker dispatch path exists to route TO. Not a feature toggle: it is not configurable, not
	// runtime-readable, and not persisted — it is a compile-time statement of what is wired. Stage 6 wires
	// the relay and flips the production default; until then the only thing that can set it true is the
	// internal test constructor below, which is what proves this router decides rather than always
	// answering "in-process".
	private readonly bool _workerPathWired;

	/// <summary>
	/// Builds the router over the declared execution metadata. Used by DI.
	/// </summary>
	/// <param name="metadataReader">The tool-name → declared execution metadata authority.</param>
	/// <exception cref="ArgumentNullException">When <paramref name="metadataReader"/> is <c>null</c>.</exception>
	public McpExecutionRouter(IMcpToolExecutionMetadataReader metadataReader)
		: this(metadataReader, workerPathWired: false) {
	}

	/// <summary>
	/// Builds the router with an explicit statement of whether a worker dispatch path exists. Exposed for
	/// tests so the <see cref="McpExecutionDisposition.Worker"/> decision — unreachable in production until
	/// Stage 6 — can be exercised, and so the difference between "reports Worker" and "executes in a worker"
	/// is assertable rather than assumed.
	/// </summary>
	/// <param name="metadataReader">The tool-name → declared execution metadata authority.</param>
	/// <param name="workerPathWired">Whether a worker dispatch path is wired into the dispatch sites.</param>
	/// <exception cref="ArgumentNullException">When <paramref name="metadataReader"/> is <c>null</c>.</exception>
	internal McpExecutionRouter(IMcpToolExecutionMetadataReader metadataReader, bool workerPathWired) {
		ArgumentNullException.ThrowIfNull(metadataReader);
		_metadataReader = metadataReader;
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
		return new McpExecutionRoute(routingKey, metadata.Location, Decide(metadata.Location), metadata);
	}

	// The three-way decision, in one place. A Worker-classified tool is reported as Worker-classified
	// whether or not a worker path exists; only the DISPOSITION changes with _workerPathWired.
	private McpExecutionDisposition Decide(McpToolExecutionLocation location) {
		if (location != McpToolExecutionLocation.Worker) {
			return McpExecutionDisposition.InProcessByClassification;
		}
		return _workerPathWired
			? McpExecutionDisposition.Worker
			: McpExecutionDisposition.InProcessPendingWorkerPath;
	}

	/// <summary>
	/// The single refusal shape every dispatch site returns when the router decides
	/// <see cref="McpExecutionDisposition.Worker"/> but the site has no worker path to hand the call to.
	/// </summary>
	/// <remarks>
	/// Unreachable in production today (the DI-built router never returns that disposition), and
	/// deliberately fail-closed rather than a silent in-process fallthrough: a site that quietly ran a
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
