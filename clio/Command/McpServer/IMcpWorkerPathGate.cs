using System;
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

	private readonly Func<McpHostTransportKind> _transportReader;
	private readonly Func<bool> _workerProcessReader;

	/// <summary>
	/// Initializes a new instance of the <see cref="McpWorkerPathGate"/> class reading the ambient process
	/// state: the transport declared by the host entry point and clio's own worker-mode flag.
	/// </summary>
	public McpWorkerPathGate()
		: this(() => McpHostTransport.Current, () => McpWorkerEnvironment.IsWorkerProcess) {
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="McpWorkerPathGate"/> class over explicit readers, so a
	/// test can state a transport and a worker-mode flag without mutating process-wide state (which would
	/// leak across a parallel fixture).
	/// </summary>
	/// <param name="transportReader">Reads the transport this host serves.</param>
	/// <param name="workerProcessReader">Reads whether this process is itself a worker.</param>
	/// <exception cref="ArgumentNullException">A reader is missing.</exception>
	internal McpWorkerPathGate(Func<McpHostTransportKind> transportReader, Func<bool> workerProcessReader) {
		ArgumentNullException.ThrowIfNull(transportReader);
		ArgumentNullException.ThrowIfNull(workerProcessReader);
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
		return _transportReader() == McpHostTransportKind.Stdio
			? McpWorkerPathAvailability.Available
			: McpWorkerPathAvailability.HostTransportNotStdio;
	}
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
