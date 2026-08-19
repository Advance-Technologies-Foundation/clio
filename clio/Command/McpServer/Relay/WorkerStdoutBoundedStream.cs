using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// A read-only view over a worker's standard output that refuses a single MESSAGE larger than a fixed
/// bound.
/// </summary>
/// <remarks>
/// <para>
/// R-11 (ENG-95262 credential threat model) asks for worker output to be bounded on both streams. The
/// standard-error half is the drain's tail limit; this is the standard-output half. The property it
/// defends is narrow and worth stating exactly: the parent serves EVERY tenant, so a single child that
/// emits an unterminated stream of bytes must not be able to grow the parent's memory until the host
/// dies. Before the execution boundary that failure took one process down with it; behind the boundary
/// the same runaway would take down every session the host is serving.
/// </para>
/// <para>
/// <b>Per MESSAGE, not per session.</b> A long-lived worker legitimately writes many megabytes across
/// many messages, so a cumulative cap would kill healthy workers for doing their job. The line feed that
/// delimits JSON-RPC messages on this transport is therefore the reset point, and the bound applies to
/// one message at a time.
/// </para>
/// <para>
/// <b>Where the number comes from.</b> The largest real payload in this repository is the live component
/// registry snapshot at ~598 KB (<c>clio.tests/Command/McpServer/Fixtures/ComponentRegistry.live-snapshot.json</c>),
/// and the largest responses the tool surface can produce — schema hierarchies with bodies, package
/// inventories on a large environment — are of that order, single-digit megabytes at worst. The bound is
/// set two orders of magnitude above that, so it cannot truncate a legitimate answer: a cap that fires on
/// a working call converts a feature into a regression, which is a worse outcome than the memory it
/// saves. What it does catch is the runaway, which is orders of magnitude away from any real response
/// and is the only case worth failing.
/// </para>
/// <para>
/// Per-call runtime state rather than a DI service, like its neighbours in this namespace: it wraps one
/// worker's stream for that worker's lifetime and carries no <c>Clio.*</c> interface, so the assembly
/// interface scan does not see it.
/// </para>
/// </remarks>
internal sealed class WorkerStdoutBoundedStream : Stream {

	/// <summary>
	/// The largest single message the relay will read from a worker, in bytes.
	/// </summary>
	internal const long DefaultMaxMessageBytes = 64L * 1024 * 1024;

	private readonly Stream _inner;
	private readonly long _maxMessageBytes;
	private long _currentMessageBytes;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkerStdoutBoundedStream"/> class.
	/// </summary>
	/// <param name="inner">The worker's readable standard output.</param>
	/// <param name="maxMessageBytes">The largest single message to accept.</param>
	internal WorkerStdoutBoundedStream(Stream inner, long maxMessageBytes = DefaultMaxMessageBytes) {
		_inner = inner ?? throw new ArgumentNullException(nameof(inner));
		if (maxMessageBytes <= 0) {
			throw new ArgumentOutOfRangeException(nameof(maxMessageBytes));
		}
		_maxMessageBytes = maxMessageBytes;
	}

	/// <inheritdoc />
	public override bool CanRead => _inner.CanRead;

	/// <inheritdoc />
	public override bool CanSeek => false;

	/// <inheritdoc />
	public override bool CanWrite => false;

	/// <inheritdoc />
	public override long Length => throw new NotSupportedException();

	/// <inheritdoc />
	public override long Position {
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	/// <inheritdoc />
	public override void Flush() => _inner.Flush();

	/// <inheritdoc />
	public override int Read(byte[] buffer, int offset, int count) {
		int read = _inner.Read(buffer, offset, count);
		Account(new ReadOnlySpan<byte>(buffer, offset, read));
		return read;
	}

	/// <inheritdoc />
	public override int Read(Span<byte> buffer) {
		int read = _inner.Read(buffer);
		Account(buffer[..read]);
		return read;
	}


	/// <inheritdoc />
	public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
		CancellationToken cancellationToken = default) {
		int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
		Account(buffer.Span[..read]);
		return read;
	}

	/// <inheritdoc />
	public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
		CancellationToken cancellationToken) =>
		ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();

	/// <inheritdoc />
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	/// <inheritdoc />
	public override void SetLength(long value) => throw new NotSupportedException();

	/// <inheritdoc />
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	/// <inheritdoc />
	protected override void Dispose(bool disposing) {
		if (disposing) {
			// Forwarded deliberately: the transport disposes the streams it was handed, and a wrapper that
			// swallowed that would leave the worker's pipe open for the process's lifetime.
			_inner.Dispose();
		}
		base.Dispose(disposing);
	}

	/// <inheritdoc />
	public override async ValueTask DisposeAsync() {
		await _inner.DisposeAsync().ConfigureAwait(false);
		await base.DisposeAsync().ConfigureAwait(false);
	}

	// Counts bytes since the last message delimiter. Scanning the bytes the reader actually received —
	// rather than trusting any framing above — is what makes the accounting independent of how the SDK
	// chunks its reads.
	private void Account(ReadOnlySpan<byte> justRead) {
		if (justRead.IsEmpty) {
			return;
		}
		int lastDelimiter = justRead.LastIndexOf((byte)'\n');
		if (lastDelimiter >= 0) {
			// Everything up to and including the delimiter belongs to messages that are now complete; only
			// the remainder is the message still being read.
			_currentMessageBytes = justRead.Length - (lastDelimiter + 1);
			return;
		}
		_currentMessageBytes += justRead.Length;
		if (_currentMessageBytes > _maxMessageBytes) {
			long limitMegabytes = _maxMessageBytes / (1024 * 1024);
			throw new IOException(string.Create(CultureInfo.InvariantCulture,
				$"The MCP worker sent a single message larger than the {limitMegabytes} MB limit, so the relay stopped reading it. This is a runaway worker, not a large answer: the limit is far above any response clio produces."));
		}
	}
}
