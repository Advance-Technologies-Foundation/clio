using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.McpWorker;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// Continuously drains one worker's standard error, keeping a bounded tail for diagnostics.
/// </summary>
/// <remarks>
/// <para>
/// <b>Draining is not diagnostics, it is liveness.</b> A worker that fills its standard-error pipe
/// buffer BLOCKS on the write and goes silent, which the parent then observes as a call that never
/// answers — a hang, attributed to the stand, caused by clio. The bounded tail is the useful
/// by-product: without it a worker that fails at startup yields only "the worker closed its transport
/// before answering".
/// </para>
/// <para>
/// <b>Promoted at Stage 7, when the SECOND lease consumer arrived.</b> Until then this was a private
/// nested type, on the stated grounds that the per-call dispatch was the only caller of
/// <see cref="IWorkerProcessSupervisor.SpawnContainedAsync"/>. The sticky path is the second, and ADR
/// §3.4 is explicit that a lease consumer which forgets to drain is not a missing log line but a worker
/// blocked on a full pipe and then reported as a stalled backend — so the drain became shared machinery
/// rather than a second copy.
/// </para>
/// <para>
/// It is promoted WITHOUT an <c>IWorkerStandardErrorDrain</c> interface, and therefore without the
/// <c>*Factory</c> that ADR §3.4 warned the interface would cost: CLIO001 treats a <c>Clio.*</c> class as
/// a DI service only when an interface named exactly <c>I&lt;TypeName&gt;</c> exists
/// (<c>DependencyInjectionManualConstructionAnalyzer</c>), and this one is created per lease from a live
/// stream no container can supply — so the interface would buy nothing and cost a class.
/// </para>
/// </remarks>
public sealed class WorkerStandardErrorDrain {

	private readonly Stream _stream;
	private readonly int _limit;
	private readonly StringBuilder _tail = new();
	private readonly object _tailLock = new();
	private long _observedCharacters;
	private Task _pump;

	internal WorkerStandardErrorDrain(Stream stream, int limit) {
		_stream = stream;
		_limit = limit;
	}

	internal void Start() {
		if (_stream is null) {
			return;
		}
		_pump = Task.Run(async () => {
			try {
				using StreamReader reader = new(_stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
					bufferSize: 1024, leaveOpen: true);
				char[] buffer = new char[1024];
				int read;
				while ((read = await reader.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false)) > 0) {
					lock (_tailLock) {
						// Counted BEFORE the trim and never reset: this running total against the limit is
						// the only surviving evidence that anything was dropped — a front-trimmed buffer
						// sitting at its bound looks identical whether or not anything was cut from it.
						_observedCharacters += read;
						_tail.Append(buffer, 0, read);
						if (_tail.Length > _limit) {
							_tail.Remove(0, _tail.Length - _limit);
						}
					}
				}
			}
			catch (Exception) {
				// The pipe closing when the worker dies is the ordinary end of this loop, not a failure,
				// and a drain must never be able to fail the call it exists to keep alive.
			}
		});
	}

	internal McpWorkerCallDispatcher.WorkerStandardErrorTail Tail() {
		lock (_tailLock) {
			if (_tail.Length == 0) {
				return null;
			}
			bool truncated = _observedCharacters > _limit;
			// An UNTRIMMED buffer reaches the caller byte for byte: nothing was cut, so no line can be
			// partial and there is nothing to protect the reader from.
			return truncated
				? new McpWorkerCallDispatcher.WorkerStandardErrorTail(WithoutOrphanedFirstLine(_tail.ToString()), Truncated: true)
				: new McpWorkerCallDispatcher.WorkerStandardErrorTail(_tail.ToString().Trim(), Truncated: false);
		}
	}

	/// <summary>
	/// Drops the leading PARTIAL line of a trimmed tail, returning
	/// <see cref="McpWorkerCallDispatcher.StandardErrorNoCompleteLineNotice"/> when no complete line survived the bound.
	/// </summary>
	/// <param name="trimmedTail">The kept tail, which begins wherever the bound happened to cut.</param>
	/// <returns>The text safe to hand to the redactor and then to the caller.</returns>
	/// <remarks>
	/// <para>
	/// <b>This is a SECURITY rule, not tidiness.</b> The bound trims from the front at an arbitrary
	/// offset — wherever the buffer stood when the next chunk arrived — so the tail routinely begins
	/// mid-token. <c>SensitiveErrorTextRedactor</c>'s credential pattern needs the KEY
	/// (<c>password</c>, <c>token</c>, …) in order to redact the value that follows it, so a cut
	/// landing inside <c>password=</c> leaves <c>word=&lt;secret&gt;</c>, which matches no pattern and
	/// is copied verbatim onto the failure envelope the client reads. Truncation is an upstream
	/// transformation that can UN-redact text the redactor would otherwise have caught, and the only
	/// place to fix it is before the redactor runs.
	/// </para>
	/// <para>
	/// <b>The design call: drop the first partial line, unconditionally, whenever anything was
	/// trimmed.</b> It costs at most one line, and that line is one the reader could not have
	/// interpreted anyway — it starts mid-sentence, mid-frame or mid-token. The cheaper-looking
	/// alternative, "drop it only when the cut really landed mid-token", would put the redactor's
	/// pattern list into the drain and would then have to be kept in step with it forever; the
	/// alternative of remembering whether the character before the cut was a line break would add
	/// pump state to recover, on the rare aligned cut, a line we are content to pay.
	/// </para>
	/// <para>
	/// <b>Nothing is dropped silently.</b> A tail with no line break at all is one unbroken partial
	/// line, so there is nothing left after the drop — and returning an empty string there would make
	/// <c>worker-stderr</c>, the truncation marker AND the caveat sentence all disappear, telling the
	/// reader "the worker said nothing" when the truth is "clio withheld what it kept". The explicit
	/// notice keeps that distinction, and keeps the envelope's own rule — an absent marker means the
	/// diagnosis is whole — true.
	/// </para>
	/// <para>
	/// <b>Residual, so nobody reads this as more than it is.</b> A line break is a boundary no pattern
	/// can be cut INSIDE, but it is not one the patterns cannot SPAN: <c>CredentialPairRegex</c>
	/// separates key from value with <c>\s*</c>, and <c>\s</c> includes the line break. A key that ends
	/// the dropped partial line with its value beginning the surviving one is therefore still orphaned
	/// — recorded in the credential threat model under T-6/R-7, not fixed here, because once the key is
	/// on the discarded side of the cut nothing local can recover it.
	/// </para>
	/// <para>
	/// Applied at SNAPSHOT time rather than in the pump: <see cref="Tail"/> is called on paths that run
	/// before <see cref="StopAsync"/>, so the buffer may still be growing, and making the drop a
	/// property of the snapshot keeps it correct under that concurrency while leaving the hot path
	/// allocation-free. It is not the weaker placement — the trim runs after every append, so the
	/// buffer holds the last <see cref="_limit"/> characters regardless of where the chunks fell.
	/// </para>
	/// </remarks>
	private static string WithoutOrphanedFirstLine(string trimmedTail) {
		// IndexOf('\n') is correct for both line endings: on CRLF the '\r' belongs to the dropped
		// partial line, and a cut landing between '\r' and '\n' leaves the '\n' as the first break.
		int firstLineBreak = trimmedTail.IndexOf('\n', StringComparison.Ordinal);
		string survivingLines = firstLineBreak < 0
			? string.Empty
			: trimmedTail[(firstLineBreak + 1)..].Trim();
		return survivingLines.Length == 0 ? McpWorkerCallDispatcher.StandardErrorNoCompleteLineNotice : survivingLines;
	}

	internal async Task StopAsync() {
		if (_pump is null) {
			return;
		}
		// Bounded: the worker's pipes are closed by the lease dispose that follows, which ends the read.
		// Waiting unbounded here would let a stuck pipe hold the response open, which is the failure
		// class this whole execution boundary removes.
		await Task.WhenAny(_pump, Task.Delay(TimeSpan.FromMilliseconds(250))).ConfigureAwait(false);
	}
}
