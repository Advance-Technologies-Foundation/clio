using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// Attaches an MCP transport to a worker process the supervisor already started, WITHOUT letting the
/// transport own that process.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <c>StdioClientTransport</c> with a command line.</b> That transport spawns the child
/// itself and documents that it manages the whole process lifecycle, force-terminating it after its own
/// shutdown timeout (5 s by default). Two independent constraints forbid it here. Containment requires
/// ownership of CREATION — on Windows the job object must be assigned between <c>CREATE_SUSPENDED</c> and
/// <c>ResumeThread</c>, because "start it, then assign it" measurably leaked a grandchild that survived
/// the parent's force-kill (ADR §2.4) — and <c>Process.Start</c> cannot express that. And a
/// <c>deploy-creatio</c> child that the transport killed after 5 s would break ADR rule 4, under which
/// the parent waits for the authoritative terminal stage.
/// </para>
/// <para>
/// <b>Attaching costs nothing measured.</b> <c>StdioClientTransport.ConnectAsync</c> returns a
/// <c>StdioClientSessionTransport</c>, which DERIVES from the <c>StreamClientSessionTransport</c> that
/// <see cref="StreamClientTransport"/> returns (verified by reflection over the shipped 2.2.0 assembly).
/// Newline framing, JSON (de)serialisation and the pipe-ordered <see cref="ITransport.MessageReader"/> —
/// everything the relay measurements exercised — live in that shared base; the stdio subclass only adds
/// the process ownership this design must not have.
/// </para>
/// <para>
/// <b>Concurrent writers: measured, not assumed — and the guarantee is narrower than it looks.</b> The relay
/// writes to one child from several places at once (<c>WorkerRelaySession.RequestAsync</c> on the caller's
/// thread; <c>HandshakeAsync</c>; <c>AnswerChildRequestAsync</c> and <c>RespondWithErrorAsync</c>, both
/// dispatched off the read loop so a slow client cannot stall notification forwarding; and the
/// <c>notifications/cancelled</c> emit on a canceller's path), so whether the transport serialises writes is
/// load-bearing. Read off the shipped <b>SDK 2.2.0</b> assembly (<c>ModelContextProtocol.Core</c>
/// 2.2.0+6fa3825): <c>StreamClientSessionTransport.SendMessageAsync</c> holds a
/// <c>SemaphoreSlim _sendLock</c> — taken through <c>SynchronizationExtensions.LockAsync(_sendLock, token)</c>
/// — across serialize, the payload <c>WriteAsync</c>, the newline <c>WriteAsync</c> and the flush. So NO send
/// gate is needed in the relay, and adding one would buy nothing.
/// </para>
/// <para>
/// The guarantee is per-COMPLETED-send, though, and that distinction is the whole of it. Each of those three
/// awaits takes the CALLER's token, so a token that fires between the payload write and the newline write
/// leaves an unterminated line on the child's stdin and releases the lock anyway; the next writer's JSON then
/// lands on the same line, the worker gets one frame it cannot parse, and it answers nothing — which reads as
/// a sick environment rather than as a cancelled call. The relay's answer is to RETIRE a session whose send
/// did not complete, never to write to that transport again; see the <c>WorkerRelaySession</c> remarks.
/// </para>
/// </remarks>
public interface IWorkerChildTransportOwner {

	/// <summary>
	/// Connects a transport to one worker's redirected standard input and output.
	/// </summary>
	/// <param name="workerStandardInput">
	/// The WRITABLE end of the worker's standard input — where the relay's requests go.
	/// </param>
	/// <param name="workerStandardOutput">
	/// The READABLE end of the worker's standard output — where the worker's messages arrive.
	/// </param>
	/// <param name="cancellationToken">Cancels the connect.</param>
	/// <returns>
	/// The connected transport. Disposing it closes the streams only; the worker process itself lives and
	/// dies with the supervisor's lease, which is the whole point of attaching rather than spawning.
	/// </returns>
	/// <exception cref="ArgumentNullException">A stream is missing.</exception>
	/// <exception cref="ArgumentException">A stream is oriented the wrong way round.</exception>
	Task<ITransport> ConnectAsync(Stream workerStandardInput, Stream workerStandardOutput,
		CancellationToken cancellationToken);
}

/// <inheritdoc cref="IWorkerChildTransportOwner"/>
public sealed class WorkerChildTransportOwner : IWorkerChildTransportOwner {

	private readonly long _maxWorkerMessageBytes;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkerChildTransportOwner"/> class.
	/// </summary>
	public WorkerChildTransportOwner()
		: this(WorkerStdoutBoundedStream.DefaultMaxMessageBytes) {
	}

	// The bound is injectable only from inside the assembly, so a test can prove what a worker exceeding
	// it actually does to the transport without writing 64 MB to a pipe to find out.
	internal WorkerChildTransportOwner(long maxWorkerMessageBytes) =>
		_maxWorkerMessageBytes = maxWorkerMessageBytes;

	/// <inheritdoc/>
	public async Task<ITransport> ConnectAsync(Stream workerStandardInput, Stream workerStandardOutput,
		CancellationToken cancellationToken) {
		if (workerStandardInput is null) {
			throw new ArgumentNullException(nameof(workerStandardInput));
		}
		if (workerStandardOutput is null) {
			throw new ArgumentNullException(nameof(workerStandardOutput));
		}
		// Checked here rather than left to the first read/write: swapped streams produce a transport that
		// simply never yields a message, which looks exactly like a worker that failed to start.
		if (!workerStandardInput.CanWrite) {
			throw new ArgumentException("The worker's standard input must be writable.",
				nameof(workerStandardInput));
		}
		if (!workerStandardOutput.CanRead) {
			throw new ArgumentException("The worker's standard output must be readable.",
				nameof(workerStandardOutput));
		}
		// R-11's standard-output half. Applied HERE because this is the one place all three dispatch paths
		// — per-call, sticky and terminal-stage — hand the worker's stream to the SDK, so a bound placed
		// anywhere else would cover some of them and quietly miss the rest.
		Stream boundedOutput = new WorkerStdoutBoundedStream(workerStandardOutput, _maxWorkerMessageBytes);
		// StreamClientTransport is a value-like SDK wrapper over the two streams, not a clio behaviour
		// class, so constructing it here is the intended use and not a DI bypass.
		StreamClientTransport transport = new(workerStandardInput, boundedOutput, loggerFactory: null);
		return await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
	}
}
