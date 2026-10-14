using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <summary>
/// A <see cref="ICdpTransport"/> over Chromium's <c>--remote-debugging-pipe</c>: every message is UTF-8 JSON followed by
/// one NUL byte, in both directions.
/// </summary>
/// <remarks>
/// The pipe reaches the browser target, not a page; <see cref="CdpSession"/> attaches to the page over it. A message may
/// arrive split across reads, or several in one read; the transport hands the session one whole message at a time.
/// </remarks>
public sealed class PipeCdpTransport : ICdpTransport {

	// A page answer is small; this bounds memory if the browser ever pushes something huge (the rest is discarded).
	internal const int MaxMessageBytes = 4 * 1024 * 1024;

	// The top-level member of the sentinel an oversized reply is replaced with; CDP never sends it, so the session
	// fails that reply's command with CdpReplyTooLargeException instead of leaving it to its timeout.
	internal const string ReplyTooLargeMarker = "clio.replyTooLarge";

	private const int SentinelPrefixBytes = 64;
	private const int ReadBufferBytes = 64 * 1024;

	// A CDP reply starts with its id ({"id":12,"result":...}), so the id survives in the kept prefix.
	private static readonly Regex LeadingId = new(
		@"\A\s*\{\s*""id""\s*:\s*(?<id>\d{1,9})\s*[,}]",
		RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
		TimeSpan.FromSeconds(1));

	private readonly Stream _fromBrowser;
	private readonly Stream _toBrowser;
	private readonly int _maxMessageBytes;
	private readonly byte[] _buffer = new byte[ReadBufferBytes];
	private readonly MemoryStream _message = new();
	private int _start;
	private int _end;
	private bool _overflow;
	private bool _closed;
	// A read the session stopped waiting for (it was cancelled) is still running on a pipe read; the next receive takes it
	// over instead of starting a second read on the same stream.
	private Task<int> _pendingRead;

	/// <summary>Wraps the two ends of the DevTools pipe; the transport owns and disposes both.</summary>
	/// <param name="fromBrowser">The stream the browser writes replies and events to.</param>
	/// <param name="toBrowser">The stream the browser reads commands from.</param>
	public PipeCdpTransport(Stream fromBrowser, Stream toBrowser) : this(fromBrowser, toBrowser, MaxMessageBytes) {
	}

	internal PipeCdpTransport(Stream fromBrowser, Stream toBrowser, int maxMessageBytes) {
		_fromBrowser = fromBrowser ?? throw new ArgumentNullException(nameof(fromBrowser));
		_toBrowser = toBrowser ?? throw new ArgumentNullException(nameof(toBrowser));
		_maxMessageBytes = maxMessageBytes;
	}

	/// <inheritdoc />
	public async Task SendAsync(string message, CancellationToken ct) {
		ArgumentNullException.ThrowIfNull(message);
		byte[] bytes = new byte[Encoding.UTF8.GetByteCount(message) + 1];
		Encoding.UTF8.GetBytes(message, bytes);
		// A NUL inside the JSON would split it; the serializer escapes every control character, so there is none.
		await _toBrowser.WriteAsync(bytes, ct).ConfigureAwait(false);
		await _toBrowser.FlushAsync(ct).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<string> ReceiveAsync(CancellationToken ct) {
		while (!_closed) {
			if (_start == _end) {
				int read = await ReadAsync(ct).ConfigureAwait(false);
				if (read == 0) {
					// The browser exited or closed its end. A message cut off by the exit is never answered: drop it.
					_closed = true;
					break;
				}
				_start = 0;
				_end = read;
			}
			int terminator = Array.IndexOf(_buffer, (byte)0, _start, _end - _start);
			int chunkEnd = terminator < 0 ? _end : terminator;
			Append(_buffer.AsSpan(_start, chunkEnd - _start));
			_start = terminator < 0 ? _end : terminator + 1;
			if (terminator >= 0) {
				return TakeMessage();
			}
		}
		return null;
	}

	private async Task<int> ReadAsync(CancellationToken ct) {
		Task<int> read = _pendingRead ?? _fromBrowser.ReadAsync(_buffer.AsMemory(), CancellationToken.None).AsTask();
		_pendingRead = read;
		// A blocking pipe read does not observe a token on every platform; the wait does, so a disposing session is
		// never held by a browser that has not exited yet.
		int count = await read.WaitAsync(ct).ConfigureAwait(false);
		_pendingRead = null;
		return count;
	}

	private void Append(ReadOnlySpan<byte> chunk) {
		if (_overflow) {
			return;
		}
		if (_message.Length + chunk.Length <= _maxMessageBytes) {
			_message.Write(chunk);
			return;
		}
		// Over the cap: keep only the head, where a reply's id is, and skip the rest up to the terminator.
		_overflow = true;
		int missing = SentinelPrefixBytes - (int)_message.Length;
		if (missing > 0) {
			_message.Write(chunk[..Math.Min(missing, chunk.Length)]);
		}
		_message.SetLength(Math.Min(_message.Length, SentinelPrefixBytes));
	}

	private string TakeMessage() {
		byte[] content = _message.GetBuffer();
		int length = (int)_message.Length;
		string message = _overflow
			? OversizedMessageSentinel(content.AsSpan(0, length))
			: Encoding.UTF8.GetString(content, 0, length);
		_message.SetLength(0);
		_overflow = false;
		return message;
	}

	// What replaces a message over the cap: a sentinel carrying the reply's id, so its command fails with a clear error;
	// an empty object (ignored) when no id leads the message, which is an event nobody waits for.
	internal static string OversizedMessageSentinel(ReadOnlySpan<byte> prefix) {
		string head = Encoding.UTF8.GetString(prefix[..Math.Min(prefix.Length, SentinelPrefixBytes)]);
		Match match = LeadingId.Match(head);
		return match.Success
			? $"{{\"id\":{match.Groups["id"].Value},\"{ReplyTooLargeMarker}\":true}}"
			: "{}";
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync() {
		// Closing the browser's command pipe is Chromium's signal to shut down; its own end of the reply pipe then closes,
		// which ends a read still pending here.
		_toBrowser.Dispose();
		_fromBrowser.Dispose();
		_message.Dispose();
		_pendingRead?.ContinueWith(static read => _ = read.Exception, CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
		return ValueTask.CompletedTask;
	}
}
