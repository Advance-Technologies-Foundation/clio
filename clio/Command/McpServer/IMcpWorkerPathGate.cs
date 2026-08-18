using System;
using System.Threading;
using Clio.Common;
using Clio.Common.McpWorker;

namespace Clio.Command.McpServer;

/// <summary>
/// Which MCP transport this process is serving. The worker execution boundary is STDIO-ONLY, so the
/// answer is a correctness input to routing rather than a diagnostic.
/// </summary>
public enum McpHostTransportKind {

	/// <summary>
	/// Nothing declared a transport. The zero value is deliberately the fail-closed one: an ordinary CLI
	/// process, a hand-built test container, or a future host that forgot to declare all read as
	/// "not stdio", so the worker path stays shut rather than opening on an unstated assumption.
	/// </summary>
	Unknown = 0,

	/// <summary>The standard-input/output host (<c>clio mcp-server</c>). The only transport that may spawn workers.</summary>
	Stdio,

	/// <summary>The HTTP host (<c>clio mcp-http</c>). Must never spawn workers — see <see cref="IMcpWorkerPathGate"/>.</summary>
	Http
}

/// <summary>
/// Why the worker path is or is not available in this process.
/// </summary>
public enum McpWorkerPathAvailability {

	/// <summary>The call may be relayed to a supervised child worker.</summary>
	Available,

	/// <summary>
	/// This process is not the stdio host, so no credential channel to a child exists — Stage 5 is
	/// deferred. Relaying here would either fail the call or, worse, execute it under a DIFFERENT identity.
	/// </summary>
	HostTransportNotStdio,

	/// <summary>
	/// This process IS a worker. A worker that routed to a worker would spawn children without end: the
	/// child receives the very <c>tools/call</c> the parent relayed, so the same routing decision that sent
	/// it here would send it on again.
	/// </summary>
	ProcessIsWorker
}

/// <summary>
/// Answers whether THIS process may relay a call to a child worker at all, independently of which tool
/// the call names.
/// </summary>
/// <remarks>
/// <para>
/// <b>The stdio-only rule is a correctness requirement, not a preference</b> (ADR §5, Stage 5 deferred
/// 2026-08-18). On stdio no secret crosses the parent/child boundary: the child reads
/// <c>appsettings.json</c> itself and is given only the environment NAME. On <c>mcp-http</c> the caller's
/// credentials live in the parent's <c>HttpContext</c>, and the channel that would hand them down is
/// exactly the thing Stage 5 was going to build and did not. A cohort tool relayed over <c>mcp-http</c>
/// would therefore either fail outright or fall back to whatever identity the child could find on its
/// own — a privilege boundary crossed silently, which is strictly worse than failing.
/// </para>
/// <para>
/// <b>Gated by the declared transport, never by "the credential context happens to be null".</b> An
/// absent credential context is an accident of a particular request; the transport is a decision. The
/// difference matters the day <c>mcp-http</c> serves one unauthenticated request: a null-context check
/// would open the worker path for it, a transport check would not.
/// </para>
/// <para>
/// <b>Reviving <c>mcp-http</c> (OQ-9) means building the credential channel and then lifting this gate
/// deliberately</b> — never deleting the check because the tests went green.
/// </para>
/// <para>
/// <b>The refusal is stated, not silent.</b> A gated host serves every cohort tool in-process and
/// returns ordinary successful results, so nothing about a call reveals that the boundary is off. The
/// implementation therefore says so once per process, on the first gated evaluation
/// (<see cref="McpWorkerPathGate.WorkerBoundaryInactiveOnHttpNotice"/>) — once, because a per-call
/// statement on a long-lived server is noise, and only on HTTP, because the fail-closed
/// <see cref="McpHostTransportKind.Unknown"/> value also covers a mis-wired stdio host whose standard
/// output is the protocol channel.
/// </para>
/// </remarks>
public interface IMcpWorkerPathGate {

	/// <summary>Evaluates whether this process may relay calls to child workers.</summary>
	/// <returns>
	/// <see cref="McpWorkerPathAvailability.Available"/>, or the named reason it is not.
	/// </returns>
	McpWorkerPathAvailability Evaluate();
}

/// <inheritdoc cref="IMcpWorkerPathGate"/>
public sealed class McpWorkerPathGate : IMcpWorkerPathGate {

	/// <summary>
	/// The statement an HTTP host makes, once, the first time it refuses to relay a cohort call.
	/// </summary>
	/// <remarks>
	/// It says what is INACTIVE and what that costs, not merely that a gate fired: on this transport a
	/// cohort tool executes exactly as it did before the worker boundary existed, so the stalled-request
	/// defect the boundary removes is still present here. Without it the gated disposition is completely
	/// silent — every cohort call runs in the host process, returns an ordinary successful result, and
	/// nothing anywhere says the mitigation is off.
	/// </remarks>
	internal const string WorkerBoundaryInactiveOnHttpNotice =
		"MCP worker execution boundary is INACTIVE on this host: it serves the HTTP transport, and the "
		+ "credential channel a child worker would need does not exist yet, so every tool call runs in "
		+ "this process. The stalled-request defect the boundary removes (a call that returns nothing "
		+ "and issues no request to Creatio, permanently, for that environment) is therefore NOT "
		+ "mitigated here. Use the stdio host (clio mcp-server) where that matters.";

	// Process-wide, because "once per server session" is a process-scoped statement and the gate is not
	// the only instance a session can build: mcp-http keeps a container per tenant, so an instance-level
	// flag would restate this per environment. Claimed only by a gate that was given a logger, so a
	// test-built gate (null logger) can never consume the claim from a host that would have used it.
	private static int _inactiveNoticeStated;

	private readonly Func<McpHostTransportKind> _transportReader;
	private readonly Func<bool> _workerProcessReader;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="McpWorkerPathGate"/> class reading the ambient process
	/// state: the transport declared by the host entry point and clio's own worker-mode flag.
	/// </summary>
	/// <param name="logger">
	/// Host logger, used ONCE per process to state that the worker boundary is inactive on an HTTP host.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
	public McpWorkerPathGate(ILogger logger)
		: this(logger, () => McpHostTransport.Current, () => McpWorkerEnvironment.IsWorkerProcess) {
		ArgumentNullException.ThrowIfNull(logger);
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="McpWorkerPathGate"/> class over explicit readers, so a
	/// test can state a transport and a worker-mode flag without mutating process-wide state (which would
	/// leak across a parallel fixture). States nothing: with no logger the gate is a pure decision.
	/// </summary>
	/// <param name="transportReader">Reads the transport this host serves.</param>
	/// <param name="workerProcessReader">Reads whether this process is itself a worker.</param>
	/// <exception cref="ArgumentNullException">A reader is missing.</exception>
	internal McpWorkerPathGate(Func<McpHostTransportKind> transportReader, Func<bool> workerProcessReader)
		: this(null, transportReader, workerProcessReader) {
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="McpWorkerPathGate"/> class over explicit readers AND a
	/// logger, so the one-time notice is assertable without a running host.
	/// </summary>
	/// <param name="logger">Host logger, or <see langword="null"/> to state nothing.</param>
	/// <param name="transportReader">Reads the transport this host serves.</param>
	/// <param name="workerProcessReader">Reads whether this process is itself a worker.</param>
	/// <exception cref="ArgumentNullException">A reader is missing.</exception>
	internal McpWorkerPathGate(ILogger logger, Func<McpHostTransportKind> transportReader,
		Func<bool> workerProcessReader) {
		ArgumentNullException.ThrowIfNull(transportReader);
		ArgumentNullException.ThrowIfNull(workerProcessReader);
		_logger = logger;
		_transportReader = transportReader;
		_workerProcessReader = workerProcessReader;
	}

	/// <inheritdoc/>
	public McpWorkerPathAvailability Evaluate() {
		// The recursion guard is checked FIRST because it is the one that cannot be argued with: a worker
		// serves stdio too, so a transport-only check would pass and every relayed call would spawn a
		// child of its own.
		if (_workerProcessReader()) {
			return McpWorkerPathAvailability.ProcessIsWorker;
		}
		McpHostTransportKind transport = _transportReader();
		if (transport == McpHostTransportKind.Stdio) {
			return McpWorkerPathAvailability.Available;
		}
		StateInactiveBoundaryOnce(transport);
		return McpWorkerPathAvailability.HostTransportNotStdio;
	}

	// Said once, on the FIRST gated call rather than per call: per call it would repeat on every tool
	// invocation of a long-lived server and become noise nobody reads.
	private void StateInactiveBoundaryOnce(McpHostTransportKind transport) {
		// HTTP only, deliberately. Unknown is the fail-closed zero value, and one of the processes it
		// covers is a mis-wired STDIO host — whose standard output IS the JSON-RPC channel that
		// ConsoleLogger writes to, so a notice there would corrupt the protocol frames it is trying to
		// explain. The enum already names Unknown a wiring defect; it is not made louder by breaking the
		// transport.
		if (transport != McpHostTransportKind.Http || _logger is null) {
			return;
		}
		if (Interlocked.Exchange(ref _inactiveNoticeStated, 1) == 0) {
			_logger.WriteWarning(WorkerBoundaryInactiveOnHttpNotice);
		}
	}

	/// <summary>
	/// Forgets that the one-time notice was stated, so a test can assert the once-per-session behaviour
	/// deterministically whatever else ran first in the same process.
	/// </summary>
	internal static void ResetInactiveNoticeForTests() => Interlocked.Exchange(ref _inactiveNoticeStated, 0);
}

/// <summary>
/// The transport the running MCP host declared, set once at startup by the host entry point.
/// </summary>
/// <remarks>
/// A process-wide property rather than an injected value for the same reason as
/// <see cref="McpWorkerEnvironment.IsWorkerProcess"/>: the composition root must be able to answer the
/// question, and the answer is fixed before any container is built. The DI-visible seam is
/// <see cref="IMcpWorkerPathGate"/>, which is what tests substitute; nothing outside a host entry point
/// should assign this.
/// </remarks>
public static class McpHostTransport {

	/// <summary>
	/// Gets or sets the transport this process serves. Defaults to
	/// <see cref="McpHostTransportKind.Unknown"/>, which is fail-closed.
	/// </summary>
	public static McpHostTransportKind Current { get; set; } = McpHostTransportKind.Unknown;
}
